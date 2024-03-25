using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KfChatDotNetGui.Models;
using KfChatDotNetGui.ViewModels;
using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using Newtonsoft.Json;
using NLog;
using Websocket.Client;

namespace KfChatDotNetGui.Views
{
    public partial class MainWindow : Window
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();
        // Using an empty config as we can update it later through the UpdateConfig method
        // Having this instance created early is handy for wiring up the events
        private ChatClient _chatClient = new(new ChatClientConfigModel());
        private SettingsModel _settings;
        private int _currentRoom;
        private ForumIdentityModel _forumIdentity;
        
        public MainWindow()
        {
            InitializeComponent();
            
            _chatClient.OnMessages += OnMessages;
            _chatClient.OnUsersJoined += OnUsersJoined;
            _chatClient.OnUsersParted += OnUsersParted;
            _chatClient.OnWsReconnect += OnOnWsReconnect;
            _chatClient.OnWsDisconnection += OnOnWsDisconnection;
            _chatClient.OnDeleteMessages += OnDeleteMessages;
            _chatClient.OnFailedToJoinRoom += OnFailedToJoinRoom;
        }

        private void OnFailedToJoinRoom(object sender, string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatus($"Failed to join room, room ID {_currentRoom} is probably invalid");
            });
        }

        private void OnDeleteMessages(object sender, List<int> messageIds)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _logger.Info($"Received delete event for following message IDs: {string.Join(',', messageIds)}");
                // Gotta make a copy of all the messages (annoyingly) as we'll be deleting stuff and .NET has a very obvious limitation there
                var messages = (DataContext as MainWindowViewModel).Messages.ToList();
                foreach (var message in messages)
                {
                    foreach (var innerMessage in message.Messages.Where(m => messageIds.Contains(m.MessageId)))
                    {
                        // Remove the parent message thingy as otherwise it just shows as a blank item
                        if (message.Messages.Count == 1)
                        {
                            _logger.Info("Removing parent message box");
                            (DataContext as MainWindowViewModel).Messages.Remove(message);
                        }
                        // Go scavenging if there are multiple messages and we don't want to lose the lot
                        else
                        {
                            (DataContext as MainWindowViewModel)
                                .Messages[(DataContext as MainWindowViewModel).Messages.IndexOf(message)].Messages
                                .Remove(innerMessage);
                        }

                        _logger.Info($"Removed {innerMessage.MessageId}");
                    }
                }
            });
        }

        private void OnOnWsDisconnection(object sender, DisconnectionInfo disconnectionInfo)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatus($"Disconnected from SneedChat due to {disconnectionInfo.Type}. Client should automatically attempt to reconnect");
            });
        }

        private void OnOnWsReconnect(object sender, ReconnectionInfo reconnectionInfo)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatus("Reconnected to SneedChat. Reason was " + reconnectionInfo.Type);
                (DataContext as MainWindowViewModel).Messages.Clear();
                (DataContext as MainWindowViewModel).UserList.Clear();
            });
            
            _chatClient.JoinRoom(_currentRoom);
        }

        private void IdentitySettings_OnClick(object? sender, RoutedEventArgs e)
        {
            var context = new IdentitySettingsWindowViewModel();
            if (File.Exists("settings.json"))
            {
                var settings = JsonConvert.DeserializeObject<SettingsModel>(File.ReadAllText("settings.json"));
                context.WsUri = settings.WsUri;
                context.XfSessionToken = settings.XfSessionToken;
                context.ReconnectTimeout = settings.ReconnectTimeout;
                context.AntiDdosPow = settings.AntiDdosPow;
                context.Username = settings.Username;
            }
            var identitySettingsWindow = new IdentitySettingsWindow
            {
                DataContext = context
            };
            identitySettingsWindow.ShowDialog(this);
            identitySettingsWindow.Closed += (o, args) =>
            {
                ReloadSettings();
            };
        }

        private void ExitMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private async Task ConnectToSneedChat()
        {
            UpdateStatus("Connecting to SneedChat");
            if (!File.Exists("settings.json"))
            {
                _logger.Error("Cannot find settings.json and therefore unable to connect to SneedChat, notifying the user through the status bar");
                UpdateStatus("Unable to connect as client has not been configured (settings.json missing)");
                return;
            }
            ReloadSettings();
            UpdateStatus("Testing XenForo token validity");
            ForumIdentityModel forumIdentity;
            if (string.IsNullOrEmpty(_settings.Username))
            {
                try
                {
                    forumIdentity = await Helpers.ForumIdentity.GetForumIdentity(_settings.XfSessionToken,
                        new Uri($"https://{_settings.WsUri.Host}/test-chat"), _settings.AntiDdosPow);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    UpdateStatus("Failed to test XenForo token, caught exception " + ex.Message);
                    return;
                }
            }
            else
            {
                forumIdentity = new ForumIdentityModel
                {
                    Username = _settings.Username,
                    Id = int.MaxValue
                };
            }
            
            if (forumIdentity == null)
            {
                UpdateStatus("Failed to deserialize account info on SneedChat page");
                return;
            }

            if (forumIdentity.Id == 0)
            {
                UpdateStatus("Token failed, SneedChat page returned Guest");
                return;
            }
            
            UpdateStatus("Token works! It belongs to " + forumIdentity.Username);
            _forumIdentity = forumIdentity;
            (DataContext as MainWindowViewModel).UserId = _forumIdentity.Id;
            var roomListControl = this.FindControl<ListBox>("RoomList");
            RoomSettingsModel.RoomList initialRoom;
            if (roomListControl.SelectedItem == null)
            {
                initialRoom = (DataContext as MainWindowViewModel).RoomList.First();
            }
            else
            {
                initialRoom = (RoomSettingsModel.RoomList) roomListControl.SelectedItem;
            }

            _chatClient.UpdateConfig(new ChatClientConfigModel
            {
                CookieDomain = _settings.WsUri.Host,
                ReconnectTimeout = _settings.ReconnectTimeout,
                WsUri = _settings.WsUri,
                XfSessionToken = _settings.XfSessionToken
            });
            
            await _chatClient.StartWsClient();
            _chatClient.JoinRoom(initialRoom.Id);
            _currentRoom = initialRoom.Id;
            UpdateStatus("Connected!");
        }

        private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var user in users)
                {
                    if ((DataContext as MainWindowViewModel).UserList.FirstOrDefault(x => x.Id == user.Id) != null)
                    {
                        _logger.Info($"{user.Username} ({user.Id}) is already in the list but has joined again. New tab? Ignoring!");
                        continue;
                    }
                    (DataContext as MainWindowViewModel).UserList.Add(new MainWindowViewModel.UserListViewModel
                    {
                        Id = user.Id,
                        Name = user.Username
                    });
                }
                UpdateUserTotalStatus();
            });
        }

        private void OnUsersParted(object sender, List<int> userIds)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var id in userIds)
                {
                    var row = (DataContext as MainWindowViewModel).UserList.FirstOrDefault(x => x.Id == id);
                    if (row == null)
                    {
                        _logger.Info($"A user ({id}) who isn't in the list has parted, ignoring!");
                        continue;
                    }
                    (DataContext as MainWindowViewModel).UserList.Remove(row);
                }
                UpdateUserTotalStatus();
            });
        }

        private void OnMessages(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var previousMessage = (DataContext as MainWindowViewModel).Messages.LastOrDefault();
                if (previousMessage == null)
                {
                    previousMessage = new MainWindowViewModel.MessageViewModel {AuthorId = -1};
                }
                foreach (var message in messages)
                {
                    _logger.Info("Received message, data payload next");
                    _logger.Info(JsonConvert.SerializeObject(message, Formatting.Indented));
                    if (message.RoomId != _currentRoom)
                    {
                        _logger.Info($"Message {message.MessageId} belongs to another room (we're in {_currentRoom}, this one was for {message.RoomId}), ignoring.");
                        continue;
                    }

                    if (message.MessageEditDate != null)
                    {
                        _logger.Info("Received an edit. Going to rewrite message if it already exists, " +
                                     "if it doesn't, nothing will happen as this would occur when loading historically modified messages.");
                        foreach (var msg in (DataContext as MainWindowViewModel).Messages)
                        {
                            foreach (var innerMsg in msg.Messages.Where(m => m.MessageId == message.MessageId))
                            {
                                innerMsg.Message = WebUtility.HtmlDecode(message.MessageRaw);
                                _logger.Info("Found the original message, text has been overwritten");
                                return;
                            }
                        }
                        _logger.Info("Never ended up finding the original message so this was probably historical");
                    }
                    
                    if (previousMessage.AuthorId == message.Author.Id)
                    {
                        _logger.Info("Found a message from the same author, merging");
                        var lastMessage = (DataContext as MainWindowViewModel).Messages.Last();
                        lastMessage.Messages.Add(new MainWindowViewModel.InnerMessageViewModel
                        {
                            Message = WebUtility.HtmlDecode(message.MessageRaw),
                            MessageId = message.MessageId,
                            OwnMessage = _forumIdentity.Username == message.Author.Username
                        });
                        continue;
                    }

                    var viewMessage = new MainWindowViewModel.MessageViewModel
                    {
                        Author = message.Author.Username,
                        Messages = new ObservableCollection<MainWindowViewModel.InnerMessageViewModel>
                        {
                            new()
                            {
                                Message = WebUtility.HtmlDecode(message.MessageRaw),
                                MessageId = message.MessageId,
                                OwnMessage = _forumIdentity.Username == message.Author.Username
                            }
                        },
                        PostedAt = message.MessageDate.LocalDateTime,
                        AuthorId = message.Author.Id
                    };
                    (DataContext as MainWindowViewModel).Messages.Add(viewMessage);
                    previousMessage = viewMessage;
                }
                var messagesControl = this.FindControl<ListBox>("ChatMessageList");
                messagesControl.ScrollIntoView((DataContext as MainWindowViewModel).Messages.Last());
            });
        }

        private void UpdateStatus(string newStatus)
        {
            (DataContext as MainWindowViewModel)!.Status = newStatus;
        }

        private void ConnectMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => ConnectToSneedChat(), DispatcherPriority.Background);
        }

        private void RoomSettingsMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            var context = new RoomSettingsWindowViewModel();
            if (File.Exists("rooms.json"))
            {
                var settings = JsonConvert.DeserializeObject<RoomSettingsModel>(File.ReadAllText("rooms.json"));
                context.RoomList.Clear();
                foreach (var room in settings.Rooms)
                {
                    context.RoomList.Add(new RoomSettingsModel.RoomList
                    {
                        Id = room.Id,
                        Name = room.Name
                    });
                }
            }
            var roomSettingsWindow = new RoomSettingsWindow
            {
                DataContext = context
            };
            roomSettingsWindow.ShowDialog(this);
            roomSettingsWindow.Closed += (o, args) =>
            {
                ReloadRoomList();
            };
        }

        private void ReloadSettings()
        {
            if (!File.Exists("settings.json"))
            {
                _logger.Error("Was asked to reload the settings but settings.json doesn't exist so I won't bother");
                return;
            }
            var settings = JsonConvert.DeserializeObject<SettingsModel>(File.ReadAllText("settings.json"));
            _settings = settings;
        }

        private void ReloadRoomList()
        {
            if (!File.Exists("rooms.json"))
            {
                _logger.Error("Was asked to reload the room list but rooms.json doesn't exist so I won't bother");
                return;
            }
            var rooms = JsonConvert.DeserializeObject<RoomSettingsModel>(File.ReadAllText("rooms.json"));
            (DataContext as MainWindowViewModel)!.RoomList = rooms!.Rooms;
        }

        private void RoomList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_chatClient.IsConnected())
            {
                UpdateStatus("Cannot join room as client is not connected");
                return;
            }
            var roomListControl = this.FindControl<ListBox>("RoomList");
            var room = (RoomSettingsModel.RoomList) roomListControl.SelectedItem!;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (room == null)
            {
                _logger.Info("Got a selection change on room list with a null selected item. This seems to just happen sometimes, ignoring.");
                return;
            }
            UpdateStatus($"Connected! Changing to {room.Name}");
            (DataContext as MainWindowViewModel).Messages.Clear();
            (DataContext as MainWindowViewModel).UserList.Clear();
            _chatClient.JoinRoom(room.Id);
            _currentRoom = room.Id;
        }

        private MainWindowViewModel.MessageViewModel? GetCurrentlySelectedMessage()
        {
            var messagesControl = this.FindControl<ListBox>("ChatMessageList");
            return messagesControl.SelectedItem as MainWindowViewModel.MessageViewModel;
        }

        private void NewChatMessage_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Return))
            {
                return;
            }
            TrySendMessageFromTextBox();
        }

        private void NewChatMessageSubmitButton_OnClick(object? sender, RoutedEventArgs e)
        {
            TrySendMessageFromTextBox();
        }

        private void TrySendMessageFromTextBox()
        {
            if (!_chatClient.IsConnected())
            {
                UpdateStatus("Cannot send a message while disconnected");
                return;
            }
            var newChatMessage = this.FindControl<TextBox>("NewChatMessage");
            _chatClient.SendMessage(newChatMessage.Text);
            newChatMessage.Clear();
        }

        private void UpdateUserTotalStatus()
        {
            var userCount = (DataContext as MainWindowViewModel).UserList.Count;
            UpdateStatus($"Connected! {userCount} users in chat");
        }

        private void MessageEditButton_OnClick(object? sender, RoutedEventArgs e)
        {
            _logger.Info("Edit button clicked for " + ((e.Source as Button).DataContext as MainWindowViewModel.InnerMessageViewModel).MessageId);
        }

        private void CopyButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var message = (e.Source as Button).DataContext as MainWindowViewModel.InnerMessageViewModel;
            if (message == null)
            {
                _logger.Info("Caught a null when trying to access the inner message model instance for the purposes of copying");
                return;
            }
            _logger.Info($"Copying {message.MessageId} to clipboard");
            GetTopLevel(e.Source as Button)?.Clipboard?.SetTextAsync(message.Message).Wait();
        }

        private void InnerMessageRow_OnPointerEnter(object? sender, PointerEventArgs e)
        {
            ((e.Source as ListBoxItem).DataContext as MainWindowViewModel.InnerMessageViewModel).IsHighlighted = true;
        }

        private void InnerMessageRow_OnPointerLeave(object? sender, PointerEventArgs e)
        {
            ((e.Source as ListBoxItem).DataContext as MainWindowViewModel.InnerMessageViewModel).IsHighlighted = false;
        }

        private void OuterMessageRow_OnPointerEnter(object? sender, PointerEventArgs e)
        {
            var children = (e.Source as ListBox).GetLogicalChildren().Cast<ListBoxItem>();
            foreach (var child in children)
            {
                // Bit of a ghetto hack but it ensures that there's only ever one subscriber
                child.PointerEntered -= InnerMessageRow_OnPointerEnter;
                child.PointerExited -= InnerMessageRow_OnPointerLeave;
                child.PointerEntered += InnerMessageRow_OnPointerEnter;
                child.PointerExited += InnerMessageRow_OnPointerLeave;
            }
        }

        private void MessageDeleteButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var newChatMessage = this.FindControl<TextBox>("NewChatMessage");
            var messageId = ((e.Source as Button).DataContext as MainWindowViewModel.InnerMessageViewModel).MessageId;
            newChatMessage.Text = "/delete " + messageId;
        }

        private void AuthorNameButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var message = (e.Source as Button).DataContext as MainWindowViewModel.MessageViewModel;
            var newChatMessage = this.FindControl<TextBox>("NewChatMessage");
            newChatMessage.Text += $"@{message.Author}, ";
            newChatMessage.Focus();
            newChatMessage.SelectionStart = newChatMessage.Text.Length;
            newChatMessage.SelectionEnd = newChatMessage.Text.Length;
        }
    }
}