using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KfChatDotNetGui.Models;
using KfChatDotNetGui.ViewModels;
using KfChatDotNetGui.Views;
using NLog;

namespace KfChatDotNetGui
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var logger = LogManager.GetCurrentClassLogger();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var dataContext = new MainWindowViewModel();
                if (File.Exists("rooms.json"))
                {
                    var rooms = JsonSerializer.Deserialize<RoomSettingsModel>(File.ReadAllText("rooms.json"));
                    dataContext.RoomList = rooms!.Rooms;

                }
                dataContext.Messages.Add(new MainWindowViewModel.MessageViewModel
                {
                    Author = "SneedChat",
                    Messages = new ObservableCollection<MainWindowViewModel.InnerMessageViewModel>{
                        new(){
                            Message = "Welcome to my shitty chat client.",
                            MessageId = 0,
                            OwnMessage = false
                        },
                        new()
                        {
                            Message = "Click on Settings -> Identity to configure your XenForo token so you may connect to SneedChat",
                            MessageId = 0,
                            OwnMessage = false
                        }
                    },
                    PostedAt = DateTimeOffset.Now,
                    AuthorId = -1
                });
                if (dataContext.RoomList.Count == 0)
                {
                    dataContext.Messages[0].Messages.Add(new MainWindowViewModel.InnerMessageViewModel
                    {
                        Message = "Also it looks like you have no rooms configured. Click on Settings -> Rooms to configure the room list",
                        MessageId = 0,
                        OwnMessage = false
                    });
                }
                desktop.MainWindow = new MainWindow
                {
                    DataContext = dataContext
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}