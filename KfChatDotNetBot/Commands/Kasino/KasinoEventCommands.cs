using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

public class KasinoNewEventCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex("^kasino event new$", RegexOptions.IgnoreCase),
        new Regex(@"^kasino event new (?<type>\w+)$", RegexOptions.IgnoreCase),
        new Regex(@"^kasino event new (?<type>\w+) (?<description>.+)$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Create a new kasino event";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventWeightWinAgainstSelectionTime, BuiltIn.Keys.KasinoEventData,
            BuiltIn.Keys.KasinoEventTextLengthLimit
        ]);
        if (!arguments.TryGetValue("type", out var type))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !kasino event new <win-lose, time-prediction> <description>",
                true, autoDeleteAfter: TimeSpan.FromSeconds(60));
            return;
        }

        if (!arguments.TryGetValue("description", out var description))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !kasino event new <win-lose, time-prediction> <description>",
                true, autoDeleteAfter: TimeSpan.FromSeconds(60));
            return;
        }

        if (description.Length > settings[BuiltIn.Keys.KasinoEventTextLengthLimit].ToType<int>())
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your event description / text with a length of " +
                                                   $"{description.Length} characters exceeds the limit of " +
                                                   $"{settings[BuiltIn.Keys.KasinoEventTextLengthLimit].ToType<int>()} " +
                                                   $"characters", true);
            return;
        }

        var useTimeWeightedPayout = settings[BuiltIn.Keys.KasinoEventWeightWinAgainstSelectionTime].ToBoolean();
        KasinoEventType eventType;
        var guide = string.Empty;
        if (type.Value.Equals("win-lose", StringComparison.CurrentCultureIgnoreCase))
        {
            eventType = KasinoEventType.WinLose;
            guide = "Add option: !kasino event {EventId} options add|new <text>[br]" +
                    "Remove option: !kasino event {EventId} options del|remove <option id>";
        }
        else if (type.Value.Equals("time-prediction", StringComparison.CurrentCultureIgnoreCase))
        {
            eventType = KasinoEventType.Prediction;
        }
        else
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, unknown event type given. Options are win-lose or time-prediction",
                true, autoDeleteAfter: TimeSpan.FromSeconds(60));
            return;
        }

        var eventData = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];
        var newEvent = new KasinoEventModel
        {
            EventText = description.Value,
            EventId = Money.GenerateEventId(),
            Options = [],
            EventType = eventType,
            EventAnnouncementReceived = null,
            EventState = KasinoEventState.Incomplete,
            SelectionTimeWeightedPayout = useTimeWeightedPayout
        };
        eventData.Add(newEvent);
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KasinoEventData, eventData);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, new incomplete kasino event created. Event ID is {newEvent.EventId}.[br]" +
            guide.Replace("{EventId}", newEvent.EventId) +
            $"Start the event: !kasino event {newEvent.EventId} start[br]" +
            $"Abandon the event: !kasino event {newEvent.EventId} abandon[br]" +
            $"Get event info: !kasino event {newEvent.EventId} info", true);
    }
}

public class KasinoEventStart : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^kasino event (?<event_id>\w+) start$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Start a Kasino event";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(300);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventData
        ]);
        var eventId = arguments["event_id"].Value; 
        var eventList = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];
        var targetEvent = eventList.FirstOrDefault(x => x.EventId == eventId);
        if (targetEvent == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, this event does not exist", true);
            return;
        }

        if (targetEvent.EventState != KasinoEventState.Incomplete)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, the event is in state '{targetEvent.EventState.Humanize()}'. Only incomplete events can be started",
                true);
            return;
        }


        var guide = $"Submit your prediction: !predict {targetEvent.EventId} <wager> 1h30m15s";
        if (targetEvent.EventType == KasinoEventType.WinLose)
        {
            if (targetEvent.Options.Count == 0)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you can't start a win-lose event with no options.", true);
                return;
            }

            guide = targetEvent.Options.Aggregate(string.Empty,
                (current, option) => current + $"{option.OptionId}: {option.OptionText}[br]");
            guide += "Submit your choice: !";
        }
        targetEvent.EventState = KasinoEventState.PendingAnnouncement;
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KasinoEventData, eventList);
        var msg = $":!: :!: A Keno Kasino event has started! :!: :!:[br]{targetEvent.EventText}[br]{guide}";
        var msgIds = await botInstance.SendChatMessagesAsync(msg.FancySplitMessage(partSeparator: "[br]"), true);
        var i = 0;
        while (msgIds[0].ChatMessageId != null)
        {
            i++;
            await Task.Delay(TimeSpan.FromMilliseconds(100), ctx);
            if (i > 3000) return;
        }

        targetEvent.EventState = KasinoEventState.Started;
        targetEvent.EventAnnouncementReceived = msgIds[0].SentAt;
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KasinoEventData, eventList);
    }
}

public class KasinoNewEventOption : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^kasino event (?<event_id>\w+) options add (?<option_text>\.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^kasino event (?<event_id>\w+) options new (?<option_text>\.+)$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Add an option to a Kasino event";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventData, BuiltIn.Keys.KasinoEventOptionTextLengthLimit
        ]);
        var eventId = arguments["event_id"].Value; 
        var eventList = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];
        var targetEvent = eventList.FirstOrDefault(x => x.EventId == eventId);
        if (targetEvent == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, this event does not exist", true);
            return;
        }

        if (targetEvent.EventState != KasinoEventState.Incomplete)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, the event is in state '{targetEvent.EventState.Humanize()}'. Only incomplete events can have their options modified",
                true);
            return;
        }

        if (targetEvent.EventType == KasinoEventType.Prediction)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, events based around time predictions can't have options", true);
            return;
        }

        var optionText = arguments["option_text"].Value;
        if (optionText.Length > settings[BuiltIn.Keys.KasinoEventOptionTextLengthLimit].ToType<int>())
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your option text with a length of " +
                                                   $"{optionText.Length} characters exceeds the limit of " +
                                                   $"{settings[BuiltIn.Keys.KasinoEventOptionTextLengthLimit].ToType<int>()} " +
                                                   $"characters", true);
            return;
        }
        var newOption = new KasinoEventOptionModel
        {
            OptionId = Money.GenerateEventId(),
            OptionText = optionText,
            Won = false
        };
        targetEvent.Options.Add(newOption);
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KasinoEventData, eventList);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, created new option with id {newOption.OptionId}[br]To remove this option, run: !kasino event {targetEvent.EventId} options remove {newOption.OptionId}", true);
    }
}

public class KasinoRemoveEventOption : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^kasino event (?<event_id>\w+) options del (?<option_id>\.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^kasino event (?<event_id>\w+) options remove (?<option_id>\.+)$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Remove an option from a Kasino event";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventData
        ]);
        var eventId = arguments["event_id"].Value; 
        var eventList = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];
        var targetEvent = eventList.FirstOrDefault(x => x.EventId == eventId);
        if (targetEvent == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, this event does not exist", true);
            return;
        }

        if (targetEvent.EventState != KasinoEventState.Incomplete)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, the event is in state '{targetEvent.EventState.Humanize()}'. Only incomplete events can have their options modified",
                true);
            return;
        }

        if (targetEvent.EventType == KasinoEventType.Prediction)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, events based around time predictions can't have options", true);
            return;
        }

        var optionId = arguments["optionId"].Value.ToLower();
        var targetOption = targetEvent.Options.FirstOrDefault(x => x.OptionId == optionId);
        if (targetOption == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, the option ID you provided does not exist", true);
            return;
        }

        targetEvent.Options.Remove(targetOption);
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KasinoEventData, eventList);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, removed option with id {targetOption.OptionId}", true);
    }
}

public class KasinoGetEventInfo : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^kasino event (?<event_id>\w+) info$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Get Kasino event info";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventData
        ]);
        var eventId = arguments["event_id"].Value; 
        var eventList = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];
        var targetEvent = eventList.FirstOrDefault(x => x.EventId == eventId);
        if (targetEvent == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, this event does not exist", true);
            return;
        }

        var response = $"{user.FormatUsername()}, Event ID {targetEvent.EventId} with type {targetEvent.EventType.Humanize()} " +
                       $"which is in state {targetEvent.EventState.Humanize()} and has the following text:" +
                       $"[br]{targetEvent.EventText.TrimStart('/')}";
        if (targetEvent.EventType == KasinoEventType.WinLose)
        {
            response += "[br]Options:";
            foreach (var option in targetEvent.Options)
            {
                response += $"[br]{option.OptionId}: {option.OptionText}";
            }
        }

        response += $"[br]Event Announcement Received: {targetEvent.EventAnnouncementReceived?.ToString("o")}";
        response += $"[br]Selection Time Weighted Payout: {targetEvent.SelectionTimeWeightedPayout}";
        await botInstance.SendChatMessagesAsync(response.FancySplitMessage(partSeparator: "[br]"), true);
    }
}

public class KasinoGetEvents : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^kasino events$", RegexOptions.IgnoreCase),
        new Regex(@"^kasino event list$", RegexOptions.IgnoreCase),

    ];

    public string? HelpText => "Get Kasino events";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoEventData
        ]);
        var eventList = settings[BuiltIn.Keys.KasinoEventData].JsonDeserialize<List<KasinoEventModel>>() ?? [];

        var response = $"{user.FormatUsername()}, there are {eventList.Count} events in the database";
        foreach (var targetEvent in eventList)
        {
            response += $"[br]{targetEvent.EventId} ({targetEvent.EventState.Humanize()}): {targetEvent.EventText}";
        }
        
        await botInstance.SendChatMessagesAsync(response.FancySplitMessage(partSeparator: "[br]"), true);
    }
}