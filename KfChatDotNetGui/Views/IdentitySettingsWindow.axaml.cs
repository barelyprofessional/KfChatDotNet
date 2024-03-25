using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using KfChatDotNetGui.Helpers;
using KfChatDotNetGui.Models;
using KfChatDotNetGui.ViewModels;
using Newtonsoft.Json;
using NLog;

namespace KfChatDotNetGui.Views;

public partial class IdentitySettingsWindow : Window
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    public IdentitySettingsWindow()
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
            var settings = new SettingsModel
            {
                XfSessionToken = (DataContext as IdentitySettingsWindowViewModel).XfSessionToken,
                WsUri = (DataContext as IdentitySettingsWindowViewModel).WsUri,
                ReconnectTimeout = (DataContext as IdentitySettingsWindowViewModel).ReconnectTimeout,
                AntiDdosPow = (DataContext as IdentitySettingsWindowViewModel).AntiDdosPow,
                Username = (DataContext as IdentitySettingsWindowViewModel).Username
            };
            File.WriteAllText("settings.json", JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            saveResult.Foreground = Brushes.Red;
            saveResult.Text = "Failed to save settings due to an error: " + ex.Message;
            saveResult.IsVisible = true;
            return;
        }
        saveResult.Foreground = Brushes.Green;
        saveResult.Text = "Successfully saved settings!";
        saveResult.IsVisible = true;
    }

    private void TestTokenButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var saveResult = this.FindControl<TextBlock>("SaveResult");
        saveResult.Foreground = Brushes.Yellow;
        saveResult.Text = "Testing XenForo token";
        saveResult.IsVisible = true;
        var kfHost = (DataContext as IdentitySettingsWindowViewModel).WsUri.Host;
        Dispatcher.UIThread.Post(
            () => TestXfToken((DataContext as IdentitySettingsWindowViewModel).XfSessionToken, kfHost, (DataContext as IdentitySettingsWindowViewModel).AntiDdosPow),
            DispatcherPriority.Background);
    }

    public void UpdateSaveText(ISolidColorBrush brush, string text)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var saveResult = this.FindControl<TextBlock>("SaveResult");
            saveResult.Foreground = brush;
            saveResult.Text = text;
            if (!saveResult.IsVisible)
            {
                saveResult.IsVisible = true;
            }
        });
    }

    public async Task TestXfToken(string xfToken, string kfHost, string? antiDdosPowToken = null)
    {
        ForumIdentityModel forumIdentity;
        try
        {
            forumIdentity = await ForumIdentity.GetForumIdentity(xfToken, new Uri($"https://{kfHost}/test-chat"), antiDdosPowToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            UpdateSaveText(Brushes.Red, "Caught exception while testing token: " + ex.Message);
            return;
        }

        if (forumIdentity == null)
        {
            UpdateSaveText(Brushes.Red, "Failed to parse SneedChat page, got a null when deserializing the user info");
            return;
        }

        if (forumIdentity.Id == 0)
        {
            UpdateSaveText(Brushes.Red, "Token is invalid, SneedChat page returned Guest");
            return;
        }

        UpdateSaveText(Brushes.Green, "Success! Token belongs to " + forumIdentity.Username);
    }
}