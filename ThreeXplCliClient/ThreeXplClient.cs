using System.Text.Json;
using Spectre.Console;
using ThreeXplWsClient.Events;

namespace ThreeXplCliClient;

public class ThreeXplClient
{
    private List<string> _addresses =
    [
        "0x3C736854AC7Cf8f24070aa3ceC72B8471d1f9781",
        "0x2d709a3c76a28b45594d6a54be72d4ab0203c546"
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
            AnsiConsole.MarkupLine($"[green]Subbed at {DateTime.Now:O}[/]");
        }
    }

    private void OnThreeXplEvent(object sender, ThreeXplPushModel e, int connectionId)
    {
        //AnsiConsole.MarkupLine("[blue]Received event from 3xpl[/]");
        foreach (var txn in e.Pub.Data.Data)
        {
            if (txn.Address == null) return;
            if (_addresses.Contains(txn.Address))
            {
                AnsiConsole.MarkupLine($"[green]Saw txn I'm interested in: {txn.Address}, effect {txn.Effect}, currency {txn.Currency}[/]");
                continue;
            }
            //AnsiConsole.MarkupLine($"[gray]Saw txn I'm not interested in: {txn.Address}, effect {txn.Effect}, currency {txn.Currency}[/]");
        }
    }
}