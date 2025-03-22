using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class AlmanacShill(ChatBot kfChatBot) : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Task? _almanacShillTask;
    private CancellationTokenSource _almanacShillCts = new();
    
    private async Task AlmanacShillTask()
    {
        var interval = await Helpers.GetValue(BuiltIn.Keys.BotAlmanacInterval);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Convert.ToInt32(interval.Value)));
        while (await timer.WaitForNextTickAsync(_almanacShillCts.Token))
        {
            _logger.Info("Time to shill the almanac in chat");
            var text = await Helpers.GetValue(BuiltIn.Keys.BotAlmanacText);
            await kfChatBot.SendChatMessageAsync($":!: {text.Value}", true);
        }
    }

    public bool IsShillTaskRunning()
    {
        if (_almanacShillTask == null) return false;
        if (_almanacShillTask.Status != TaskStatus.Running) return false;
        if (_almanacShillCts.IsCancellationRequested) return false;
        return true;
    }

    public async Task StopShillTaskAsync()
    {
        await _almanacShillCts.CancelAsync();
        _almanacShillTask?.Dispose();
    }
    
    public void StartShillTask()
    {
        _almanacShillCts = new CancellationTokenSource();
        _almanacShillTask = Task.Run(AlmanacShillTask, _almanacShillCts.Token);
    }

    public void Dispose()
    {
        _almanacShillCts.Cancel();
        _almanacShillTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}