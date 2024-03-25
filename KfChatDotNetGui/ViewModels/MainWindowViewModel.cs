using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using JetBrains.Annotations;
using KfChatDotNetGui.Models;
using ReactiveUI;

namespace KfChatDotNetGui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public class InnerMessageViewModel : INotifyPropertyChanged
        {
            private string _message;
            
            public string Message
            {
                get => _message;
                set
                {
                    if (_message == value) return;
                    _message = value;
                    OnPropertyChanged();
                }
            }
            
            private bool _isHighlighted = false;

            public bool IsHighlighted
            {
                get => _isHighlighted;
                set
                {
                    if (_isHighlighted == value) return;
                    _isHighlighted = value;
                    OnPropertyChanged();
                }
            }

            public int MessageId { get; set; }
            public bool OwnMessage { get; set; }
            
            public event PropertyChangedEventHandler? PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        
        public class MessageViewModel : INotifyPropertyChanged
        {
            private ObservableCollection<InnerMessageViewModel> _messages;

            public ObservableCollection<InnerMessageViewModel> Messages
            {
                get => _messages;
                set
                {
                    if (_messages == value) return;
                    _messages = value;
                    OnPropertyChanged();
                }
            }

            public DateTimeOffset PostedAt { get; set; }
            public string Author { get; set; }
            public int AuthorId { get; set; }
            public event PropertyChangedEventHandler? PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class UserListViewModel
        {
            public string Name { get; set; }
            public int Id { get; set; }
        }

        private string _statusText = "Not connected";

        public string Status
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        private int _userId;

        public int UserId
        {
            get => _userId;
            set => this.RaiseAndSetIfChanged(ref _userId, value);
        }

        private List<RoomSettingsModel.RoomList> _roomList = new();

        public List<RoomSettingsModel.RoomList> RoomList
        {
            get => _roomList;
            set => this.RaiseAndSetIfChanged(ref _roomList, value);
        }

        private ObservableCollection<UserListViewModel> _userList = new();

        public ObservableCollection<UserListViewModel> UserList
        {
            get => _userList;
            set => this.RaiseAndSetIfChanged(ref _userList, value);
        }

        private ObservableCollection<MessageViewModel> _messages = new();

        public ObservableCollection<MessageViewModel> Messages
        {
            get => _messages;
            set => this.RaiseAndSetIfChanged(ref _messages, value);
        }
    }
}