using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using HtmlAgilityPack;
using KfChatDotNetGui.Models;
using KfChatDotNetGui.ViewModels;
using Newtonsoft.Json;
using NLog;

namespace KfChatDotNetGui.Views;

public partial class RoomSettingsWindow : Window
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    public RoomSettingsWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var saveResult = this.FindControl<TextBlock>("SaveResult");
        try
        {
            var roomSettings = new RoomSettingsModel
            {
                Rooms = new List<RoomSettingsModel.RoomList>()
            };
            foreach (var room in (DataContext as RoomSettingsWindowViewModel).RoomList)
            {
                roomSettings.Rooms.Add(new RoomSettingsModel.RoomList
                {
                    Id = room.Id,
                    Name = room.Name
                });
            }
            File.WriteAllText("rooms.json", JsonConvert.SerializeObject(roomSettings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            _logger.Error(e);
            saveResult.Foreground = Brushes.Red;
            saveResult.Text = "Failed to save rooms due to an error: " + ex.Message;
            saveResult.IsVisible = true;
            return;
        }
        saveResult.Foreground = Brushes.Green;
        saveResult.Text = "Successfully saved rooms!";
        saveResult.IsVisible = true;
    }

    private void AddRowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as RoomSettingsWindowViewModel).RoomList.Add(new RoomSettingsModel.RoomList());
    }

    private void DeleteSelectedRowsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var roomGrid = this.FindControl<DataGrid>("RoomGrid");
        var roomList = (DataContext as RoomSettingsWindowViewModel).RoomList.ToList();
        foreach (var room in roomGrid.SelectedItems)
        {
            roomList.Remove(room as RoomSettingsModel.RoomList);
        }

        (DataContext as RoomSettingsWindowViewModel).RoomList =
            new ObservableCollection<RoomSettingsModel.RoomList>(roomList);
    }

    private void AutoDetectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var saveResult = this.FindControl<TextBlock>("SaveResult");
        saveResult.Foreground = Brushes.Yellow;
        saveResult.Text = "Downloading the SneedChat page";
        saveResult.IsVisible = true;

        Dispatcher.UIThread.Post(() => AutoDetectRooms(), DispatcherPriority.Background);
    }

    private async Task AutoDetectRooms()
    {
        var kfDomain = "kiwifarms.net";
        if (File.Exists("settings.json"))
        {
            var settings = JsonConvert.DeserializeObject<SettingsModel>(await File.ReadAllTextAsync("settings.json"));
            kfDomain = settings.WsUri.Host;
        }

        Uri sneedChatUri = new Uri($"https://{kfDomain}/test-chat");
        using (var client = new HttpClient(new HttpClientHandler {AutomaticDecompression = DecompressionMethods.All}))
        {
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("KfChatDotNetGui/1.0");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));

            HttpResponseMessage response = await client.GetAsync(sneedChatUri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"Got HTTP error {response.StatusCode} when fetching {sneedChatUri}");
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var saveResult = this.FindControl<TextBlock>("SaveResult");
                    saveResult.Foreground = Brushes.Red;
                    saveResult.Text = $"Failed to load the SneedChat page due to an HTTP error (Status code {response.StatusCode})";
                    saveResult.IsVisible = true;
                });
                return;
            }

            var html = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var roomList = document.DocumentNode.SelectNodes("//a[@class=\"chat-room\"]");
            if (roomList == null)
            {
                _logger.Error("Chat room list is null, xpath for it is probably broken");
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var saveResult = this.FindControl<TextBlock>("SaveResult");
                    saveResult.Foreground = Brushes.Red;
                    saveResult.Text = "Failed to parse the SneedChat page, list of rooms was null";
                    saveResult.IsVisible = true;
                });
                return;
            }

            List<RoomSettingsModel.RoomList> roomListModel = new List<RoomSettingsModel.RoomList>();
            foreach (var element in roomList)
            {
                roomListModel.Add(new RoomSettingsModel.RoomList
                {
                    Id = element.GetAttributeValue("data-id", 0),
                    Name = WebUtility.HtmlDecode(element.InnerText)
                });
            }
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                (DataContext as RoomSettingsWindowViewModel).RoomList.Clear();
                foreach (var room in roomListModel)
                {
                    (DataContext as RoomSettingsWindowViewModel).RoomList.Add(room);
                }

                var saveResult = this.FindControl<TextBlock>("SaveResult");
                saveResult.Foreground = Brushes.Green;
                saveResult.Text = "Populated list using SneedChat page. Remember to hit Save when you're done!";
                saveResult.IsVisible = true;
            });
        }
    }
}