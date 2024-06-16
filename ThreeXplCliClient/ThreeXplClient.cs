using System.Text.Json;
using Spectre.Console;
using ThreeXplWsClient.Events;

namespace ThreeXplCliClient;

public class ThreeXplClient
{
    private List<string> _addresses =
    [
        "MC8TiBEsnQVjxbvLtTsUXjTBZTQaR8fe8X",
        "ltc1qks2m7hvmhs3c20zrfvptv9pvk82p8g70sgw5mk"
    ];
    public async Task Start()
    {
        var client = new ThreeXplWsClient.ThreeXplWsClient();
        client.OnThreeXplPush += OnThreeXplEvent;
        await client.StartWsClient();
        while (true)
        {
            var prompt = AnsiConsole.Ask<string>("Channel: ");
            client.SendSubscribeRequest(prompt);
        }
    }

    private void OnThreeXplEvent(object sender, ThreeXplPushModel e, int connectionId)
    {
        AnsiConsole.MarkupLine("[blue]Received event from 3xpl[/]");
        foreach (var txn in e.Pub.Data.Data)
        {
            if (txn.Address == null) return;
            if (_addresses.Contains(txn.Address))
            {
                AnsiConsole.MarkupLine($"[green]Saw txn I'm interested in: {txn.Address}, effect {txn.Effect}, currency {txn.Currency}[/]");
            }
        }
    }
}