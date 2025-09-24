using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;

namespace KfChatDotNetBot.Extensions;

public static class MoneyExtensions
{
    /// <summary>
    /// Format an amount of money using configured symbols
    /// </summary>
    /// <param name="amount">The amount you wish to format</param>
    /// <param name="suffixSymbol">Whether to suffix the symbol</param>
    /// <param name="prefixSymbol">Whether to prefix the symbol</param>
    /// <param name="wrapInPlainBbCode">Whether to wrap the resulting string in [plain][/plain] BBCode to avoid characters being interpreted as emotes</param>
    /// <returns></returns>
    public static async Task<string> FormatKasinoCurrencyAsync(this decimal amount, bool suffixSymbol = true,
        bool prefixSymbol = false, bool wrapInPlainBbCode = true)
    {
        var settings = await
            SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.MoneySymbolPrefix, BuiltIn.Keys.MoneySymbolSuffix]);
        var result = string.Empty;
        if (wrapInPlainBbCode)
        {
            result = "[plain]";
        }

        if (prefixSymbol)
        {
            result += settings[BuiltIn.Keys.MoneySymbolPrefix].Value;
        }

        result += $"{amount:N2}";

        if (suffixSymbol)
        {
            result += $" {settings[BuiltIn.Keys.MoneySymbolSuffix].Value}";
        }

        if (wrapInPlainBbCode)
        {
            result += "[/plain]";
        }

        return result;
    }
}
