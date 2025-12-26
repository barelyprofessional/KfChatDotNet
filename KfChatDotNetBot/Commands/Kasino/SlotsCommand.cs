using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Numerics;
using RandN;
using RandN.Compat;
using Raylib_cs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Webp;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class SlotsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^slots (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^slots (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^slots$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!slots [bet amount]";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(30)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel messagen, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        if (!arguments.TryGetValue("amount", out var amount)) //if user just enters !keno
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you need to bet something to play. !slots [bet]",
                true, autoDeleteAfter: TimeSpan.FromSeconds(30));
            return;
        }

        var wager = Convert.ToDecimal(amount.Value);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(30));
            return;
        }
        
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(500,900,"KiwiSlot");
        
        var board = new KiwiSlotBoard(wager);
        board.LoadAssets();
        board.ExecuteGameLoop();
        //var finalImage = board.GenerateAnimatedWebp(board.SlotFrames);
        using (var finalImage = board.GenerateAnimatedWebp(board.SlotFrames))
        {
            board.UnloadAssets(); // beep boop says to more aggressively destroy every frame
            if (finalImage == null) throw new InvalidOperationException("finalimage was null");
            var imageUrl = await Zipline.Upload(finalImage, new MediaTypeHeaderValue("image/webp"), "1h", ctx);
            if (imageUrl == null) throw new InvalidOperationException("Image failed to upload/failed to get URL");
            await botInstance.SendChatMessageAsync($"[img]{imageUrl}[/img]", true,
                autoDeleteAfter: TimeSpan.FromMinutes(3));
        }

        board.UnloadAssets();
        Raylib.CloseWindow();

        var winnings = board.RunningTotalDisplay;
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        decimal newBalance;
        if (winnings == 0) //dud spin
        {
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Slots, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()} you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]lost[/color]. Current balance: {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: TimeSpan.FromSeconds(30));
            return;
        }

        //if you win
        var featureAddOn = board.GotFeature ? "Congrats on the feature." : "";
        winnings -= wager;
        newBalance = await Money.NewWagerAsync(gambler.Id, wager, winnings, WagerGame.Slots, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]won[/color] {await winnings.FormatKasinoCurrencyAsync()}! Current balance: {await newBalance.FormatKasinoCurrencyAsync()}" +
            $"{featureAddOn}", true, autoDeleteAfter: TimeSpan.FromSeconds(30));
    }

    private class KiwiSlotBoard
    {
        private const char WILD = 'K', FEATURE = 'L', EXPANDER = 'M';
        public readonly List<Raylib_cs.Image> SlotFrames = [];
        private readonly Dictionary<char, Texture2D> _symbolTextures = new();
        private readonly Dictionary<int, Texture2D> _expanderTextures = new();
        private Texture2D _headerTexture;

        private readonly char[,] _preboard = new char[5, 5];
        private char[,] _board = new char[5, 5];
        private readonly decimal _userBet;
        public decimal RunningTotalDisplay;
        public bool GotFeature;
        private int _activeFeatureTier;
        private int _currentFeatureSpin; // Tracks progress through the feature
        private bool _showGoldCircle;

        private string SlotSkin = "Default";

        private readonly RandomShim<StandardRng> _rand = RandomShim.Create(StandardRng.Create());
        private static readonly List<char> ExpanderWild =
            ['N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '1', '2'];
        private readonly Dictionary<char, double> _multiTable = new()
        {
            { 'N', 2 }, { 'O', 3 }, { 'P', 4 }, { 'Q', 5 }, { 'R', 6 }, { 'S', 7 },
            { 'T', 8 }, { 'U', 9 }, { 'V', 10 }, { 'W', 15 }, { 'X', 20 }, { 'Y', 25 },
            { 'Z', 50 }, { '1', 100 }, { '2', 200 }
        };
        private readonly Dictionary<string, double> _payoutTable = new()
        {
            { "A3", 0.2 }, { "A4", 1.0 }, { "A5", 5.0 }, { "B3", 0.2 }, { "B4", 1.0 }, { "B5", 5.0 },
            { "C3", 0.3 }, { "C4", 1.5 }, { "C5", 7.5 }, { "D3", 0.3 }, { "D4", 1.5 }, { "D5", 7.5 },
            { "E3", 0.4 }, { "E4", 2.0 }, { "E5", 10.0 }, { "F3", 1.0 }, { "F4", 5.0 }, { "F5", 15.0 },
            { "G3", 1.0 }, { "G4", 5.0 }, { "G5", 15.0 }, { "H3", 1.5 }, { "H4", 7.5 }, { "H5", 17.5 },
            { "I3", 1.5 }, { "I4", 7.5 }, { "I5", 17.5 }, { "J3", 2.0 }, { "J4", 10.0 }, { "J5", 20.0 },
            { "K5", 25.0 }, { "L5", 25.0 }, { "M5", 25.0 }
        };
        private readonly List<(int row, int col)[]> _payoutLines = new()
        {
            new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },
            new[] { (1, 0), (1, 1), (1, 2), (1, 3), (1, 4) },
            new[] { (2, 0), (2, 1), (2, 2), (2, 3), (2, 4) },
            new[] { (3, 0), (3, 1), (3, 2), (3, 3), (3, 4) },
            new[] { (4, 0), (4, 1), (4, 2), (4, 3), (4, 4) },
            new[] { (0, 0), (1, 1), (2, 2), (3, 3), (4, 4) },
            new[] { (4, 0), (3, 1), (2, 2), (1, 3), (0, 4) },
            new[] { (1, 0), (0, 1), (1, 2), (0, 3), (1, 4) },
            new[] { (2, 0), (1, 1), (2, 2), (1, 3), (2, 4) },
            new[] { (3, 0), (2, 1), (3, 2), (2, 3), (3, 4) },
            new[] { (4, 0), (3, 1), (4, 2), (3, 3), (4, 4) },
            new[] { (0, 0), (1, 1), (0, 2), (1, 3), (0, 4) },
            new[] { (1, 0), (2, 1), (1, 2), (2, 3), (1, 4) },
            new[] { (2, 0), (3, 1), (2, 2), (3, 3), (2, 4) },
            new[] { (3, 0), (4, 1), (3, 2), (4, 3), (3, 4) },
            new[] { (2, 0), (1, 1), (0, 2), (1, 3), (2, 4) },
            new[] { (3, 0), (2, 1), (1, 2), (2, 3), (3, 4) },
            new[] { (2, 0), (3, 1), (4, 2), (3, 3), (2, 4) },
            new[] { (1, 0), (2, 1), (3, 2), (2, 3), (1, 4) }
        };

        public KiwiSlotBoard(decimal bet) {
            _userBet = bet;
            RunningTotalDisplay = 0;
        }

        public void LoadAssets()
        {
            var assetPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "Assets", SlotSkin);
            _headerTexture = Raylib.LoadTexture(Path.Combine(assetPath, "header.png"));
            foreach (var c in "ABCDEFGHIJKL") _symbolTextures[c] = Raylib.LoadTexture(Path.Combine(assetPath, $"{c}.png"));
            for (var i = 1; i <= 5; i++) _expanderTextures[i] = Raylib.LoadTexture(Path.Combine(assetPath, $"exp{i}.png"));
        }

        public void UnloadAssets()
        {
            Raylib.UnloadTexture(_headerTexture);
            foreach (var t in _symbolTextures.Values) Raylib.UnloadTexture(t);
            foreach (var t in _expanderTextures.Values) Raylib.UnloadTexture(t);
            foreach (var img in SlotFrames) // claude edited this for some reason
            {
                Raylib.UnloadImage(img);
            }
            SlotFrames.Clear();
        }

        public void ExecuteGameLoop(int featureSpins = 0)
        {
            if (featureSpins is not 0) GotFeature = true;
            GeneratePreBoard(featureSpins);
            
            var fCount = 0;
            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 5; j++)
                    if (_preboard[i, j] == FEATURE) fCount++;

            if (featureSpins == 0)
            {
                _showGoldCircle = false;
                _activeFeatureTier = fCount >= 5 ? 5 : (fCount >= 3 ? fCount : 0);
                _currentFeatureSpin = 0;
            }
            else
            {
                _showGoldCircle = true;
                _currentFeatureSpin = featureSpins;
            }

            ConsoleDisplay();

            var totalSpins = _activeFeatureTier switch { 3 => 3, 4 => 5, 5 => 10, _ => 0 };
            if (featureSpins == 0)
                for (var s = 1; s <= totalSpins; s++) ExecuteGameLoop(s);
        }

        
        public void GeneratePreBoard(int feature = 0, char rigged = '0')
        {
            var fCount = 0;
            var exCols = new HashSet<int>();
            var riggedCounter = 0;
            var maxWinRiggedCounter = 0;
            for (var i = 0; i < 5; i++)
            {
                for (var j = 0; j < 5; j++)
                {
                    if (rigged == '0')
                    {
                        /*
                         * LOWEST - A, B
                         * LOW - C, D
                         * LOWMID - E
                         * MID - F, G
                         * HIGH - H, I
                         * HIGHEST - J
                         * WILD - K
                         * FEATURE - L
                         * EXPANDER - M
                         * EXPANDERWILD multis 2 - 10, 15, 20, 25, 50, 100, 200x - N O P Q R S T U V W X Y Z 1 2
                         */
                        var r = _rand.NextDouble()*100.6;
                        if (feature!=0 && j > 2) r*=1.05;
                        if (r < 22) _preboard[i, j] = 'A';
                        else if (r < 44) _preboard[i, j] = 'B';
                        else if (r < 52) _preboard[i, j] = 'C';
                        else if (r < 66) _preboard[i, j] = 'D';
                        else if (r < 78) _preboard[i, j] = 'E';
                        else if (r < 84) _preboard[i, j] = 'F';
                        else if (r < 89) _preboard[i, j] = 'G';
                        else if (r < 92) _preboard[i, j] = 'H';
                        else if (r < 95) _preboard[i, j] = 'I';
                        else if (r < 97) _preboard[i, j] = 'J';
                        else if (r < 98.5) _preboard[i, j] = WILD;
                        else if (r < j switch {<=2 => 99, _ => 99.5}) { if (!exCols.Contains(j)) { _preboard[i, j] = EXPANDER; exCols.Add(j); } else _preboard[i, j] = WILD; }
                        else
                        {
                            if (fCount < 5) { _preboard[i, j] = FEATURE; fCount++; }
                            else _preboard[i, j] = WILD;
                        }
                    }
                    else
                    {
                        if (riggedCounter < 5 || (rigged != EXPANDER && rigged != FEATURE))
                        {
                            _preboard[i, j] = rigged;
                            riggedCounter++;
                        }
                        else if (rigged == EXPANDER && maxWinRiggedCounter < 5)
                        {
                            _preboard[i, j] = FEATURE;
                            maxWinRiggedCounter++;
                        }
                        else if (rigged == EXPANDER && maxWinRiggedCounter >= 5)
                        {
                            _preboard[i,j] = WILD;
                        }
                        else
                        {
                            _preboard[i, j] = 'A';
                            rigged = '0';
                        }
                    }
                }
            }
        }

        private void RenderFrame(int dropOffset = 500, List<WinDetail>? activeWins = null)
        {
            var target = Raylib.LoadRenderTexture(500, 900);
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(Raylib_cs.Color.Black);

            Raylib.DrawTexture(_headerTexture, 0, 0, Raylib_cs.Color.White);

            Raylib.BeginScissorMode(0, 200, 500, 500);
            var occupied = new bool[5, 5];
            for (var j = 0; j < 5; j++) {
                for (var i = 0; i < 5; i++) {
                    if (occupied[i, j]) continue;
                    var sym = _board[i, j];
                    int x = j * 100, currentY = (200 + (i * 100)) - (500 - dropOffset);
                    if (sym == EXPANDER || _multiTable.ContainsKey(sym)) {
                        var h = 0;
                        for (var k = i; k < 5; k++) { if (_board[k, j] == sym) h++; else break; }
                        if (_expanderTextures.TryGetValue(h, out var texture)) {
                            Raylib.DrawTexture(texture, x, currentY, Raylib_cs.Color.White);
                            if (_multiTable.TryGetValue(sym, out var mVal)) {
                                var mText = $"x{mVal}";
                                const int fsM = 30; var twM = Raylib.MeasureText(mText, fsM);
                                Raylib.DrawText(mText, x + 50 - twM / 2 + 2, currentY + (h * 50) - fsM / 2 + 2, fsM, Raylib_cs.Color.Black);
                                Raylib.DrawText(mText, x + 50 - twM / 2, currentY + (h * 50) - fsM / 2, fsM, Raylib_cs.Color.Yellow);
                            }
                        }
                        for (var k = 0; k < h; k++) occupied[i + k, j] = true;
                    } else if (_symbolTextures.TryGetValue(sym, out var texture)) Raylib.DrawTexture(texture, x, currentY, Raylib_cs.Color.White);
                }
            }

            if (activeWins != null) {
                foreach (var win in activeWins) {
                    for (var i = 0; i < win.Path.Length - 1; i++) {
                        var s = new Vector2(win.Path[i].col * 100 + 50, 200 + (win.Path[i].row * 100) + 50);
                        var e = new Vector2(win.Path[i+1].col * 100 + 50, 200 + (win.Path[i+1].row * 100) + 50);
                        Raylib.DrawLineEx(s, e, 8.0f, Raylib_cs.Color.White);
                    }
                    var amt = $"${win.Amount:F2}";
                    const int fsW = 25; var twW = Raylib.MeasureText(amt, fsW);
                    int tx = win.Path[win.Path.Length / 2].col * 100 + 50, ty = 200 + (win.Path[win.Path.Length / 2].row * 100) + 50;
                    Raylib.DrawRectangle(tx - twW / 2 - 5, ty - fsW / 2 - 2, twW + 10, fsW + 4, new Raylib_cs.Color(0, 0, 0, 200));
                    Raylib.DrawText(amt, tx - twW / 2, ty - fsW / 2, fsW, Raylib_cs.Color.Green);
                }
            }
            Raylib.EndScissorMode();

            // FOOTER
            Raylib.DrawRectangle(0, 700, 500, 200, new Raylib_cs.Color(15, 15, 15, 255));
            Raylib.DrawLineEx(new Vector2(0, 700), new Vector2(500, 700), 2, Raylib_cs.Color.Gold);
            
            // Top Row UI - Compacted
            Raylib.DrawText("BET", 20, 710, 12, Raylib_cs.Color.LightGray);
            Raylib.DrawText($"${_userBet:F2}", 20, 725, 18, Raylib_cs.Color.White);

            // SPIN COUNTER (Progress) - Center between Bet and Win
            if (_currentFeatureSpin > 0)
            {
                var totalSpins = _activeFeatureTier switch { 3 => 3, 4 => 5, 5 => 10, _ => 0 };
                var spinProgress = $"SPIN {_currentFeatureSpin}/{totalSpins}";
                var spinFs = 20;
                var spinTw = Raylib.MeasureText(spinProgress, spinFs);
                Raylib.DrawText(spinProgress, 250 - (spinTw / 2), 715, spinFs, Raylib_cs.Color.SkyBlue);
            }

            var tallyStr = $"WIN: ${RunningTotalDisplay:F2}";
            var tallySize = 26;
            var tallyWidth = Raylib.MeasureText(tallyStr, tallySize);
            Raylib.DrawText(tallyStr, 480 - tallyWidth, 712, tallySize, Raylib_cs.Color.Gold);

            // Feature Symbol Area
            var iconY = 760;
            var textY = 865;
            int[] xCoords = { 80, 250, 420 };
            int[] tiers = { 3, 4, 5 };

            for (var i = 0; i < 3; i++)
            {
                var tier = tiers[i];
                var x = xCoords[i] - 50;

                if (_showGoldCircle && _activeFeatureTier == tier)
                {
                    Raylib.DrawCircle(x + 50, iconY + 50, 48, Raylib_cs.Color.Gold);
                }

                if (_symbolTextures.ContainsKey(FEATURE))
                {
                    Raylib.DrawTexture(_symbolTextures[FEATURE], x, iconY, Raylib_cs.Color.White);
                }

                var tierLabel = $"x{tier}";
                var fsL = 20;
                var twL = Raylib.MeasureText(tierLabel, fsL);
                Raylib.DrawText(tierLabel, x + 50 - twL / 2, textY, fsL, Raylib_cs.Color.White);
            }

            Raylib.EndTextureMode();
            var finalImage = Raylib.LoadImageFromTexture(target.Texture);
            Raylib.ImageFlipVertical(ref finalImage);
            SlotFrames.Add(finalImage);
            Raylib.UnloadRenderTexture(target);
        }

        private void ConsoleDisplay(bool riggedMaxWin = false)
        {
            // 1. Initial Setup and Drop Animation
            _board = (char[,])_preboard.Clone();
            for (var offset = 0; offset <= 500; offset += 50) RenderFrame(offset);
            for (var offset = 500; offset <= 520; offset += 20) RenderFrame(offset);
            for (var offset = 520; offset >= 500; offset -= 20) RenderFrame(offset);

            // 2. Handle Expander Multipliers
            var multis = new List<char>(_multiTable.Keys);
            for (var j = 0; j < 5; j++) {
                for (var i = 0; i < 5; i++)
                {
                    if (_preboard[i, j] != EXPANDER) continue;
                    
                    var hitWild = false;
                    for (var c = i; c < 5; c++) if (_preboard[c, j] == WILD) hitWild = true;
                        
                    char mSym;
                    if (!riggedMaxWin)
                    {
                        mSym = hitWild ? multis[_rand.Next(multis.Count)] : EXPANDER;
                    }
                    else mSym = '2';

                    for (var row = i; row < 5; row++) {
                        _board[row, j] = mSym;
                        // Brief pause frames for expansion effect
                        for(var f = 0; f < 1; f++) RenderFrame();
                    }
                    break;
                }
            }

            // 3. Winning Line Calculations and Accurate Accumulation
            var winners = GetWinningLinesCoordsWithPayouts();
            
            // Calculate the final target to prevent rounding errors
            var totalToWinThisSpin = winners.Sum(w => w.Amount);
            var finalTarget = RunningTotalDisplay + totalToWinThisSpin;

            // Iterate through each winning line
            for (var i = 0; i < winners.Count; i++) {
                var currentWin = winners[i];
                var increment = currentWin.Amount / (decimal)10.0;

                // Process 10 frames of animation per winning line
                for (var f = 0; f < 10; f++) {
                    RunningTotalDisplay += increment;

                    // If there's a next win, we show both currently active and next for smoothness
                    if (i < winners.Count - 1 && f > 7) {
                        RenderFrame(500, [currentWin, winners[i + 1]]);
                    } else {
                        RenderFrame(500, [currentWin]);
                    }
                }
            }

            // FINAL SNAP: Ensure floating point precision hasn't left us at 199.99 instead of 200.00
            RunningTotalDisplay = finalTarget;

            // 4. Feature Trigger Visualization
            if (_activeFeatureTier >= 3)
            {
                _showGoldCircle = true;
                // Hold on the final board state longer if a feature triggered
                for (var f = 0; f < 10; f++) RenderFrame();
            }
            else
            {
                // Short pause before the next spin/end of animation
                for (var f = 0; f < 5; f++) RenderFrame();
            }
        }
        
        private List<WinDetail> GetWinningLinesCoordsWithPayouts()
        {
            var results = new List<WinDetail>();
            foreach (var line in _payoutLines) {
                var checker = '0';
                var count = 0; double multi = 0; var special = true;
                foreach (var (r, c) in line) {
                    var cell = _board[r, c];
                    if (cell == WILD || cell == FEATURE || cell == EXPANDER || ExpanderWild.Contains(cell)) continue;
                    checker = cell; special = false; break; //finds the first valid symbol in the payline
                }
                if (!special) {
                    foreach (var (r, c) in line) {
                        var ch = _board[r, c];
                        if (ch == checker || ch == WILD || ch == FEATURE || ExpanderWild.Contains(ch) || ch == EXPANDER) {
                            count++;
                            if (ExpanderWild.Contains(ch)) multi += _multiTable[ch];
                        } else if (count < 3) { count = 0; break; } else break;
                    }
                }
                else {
                    checker = _board[line[0].row, line[0].col];
                    count = 5;
                    foreach (var (r, c) in line) if (ExpanderWild.Contains(_board[r, c])) multi += _multiTable[_board[r, c]];
                }
                if (count >= 3) {
                    if (multi == 0) multi = 1;
                    if (_payoutTable.TryGetValue($"{checker}{count}", out var baseWin)) {
                        var path = new (int, int)[count]; Array.Copy(line, path, count);
                        results.Add(new WinDetail { Path = path, Amount = _userBet * (decimal)baseWin * (decimal)multi });
                    }
                }
            }
            return results;
        }

        public unsafe MemoryStream? GenerateAnimatedWebp(List<Raylib_cs.Image> frames)
        {
            if (frames.Count == 0) return null;
            using var animated = new Image<Rgba32>(500, 900);
    
            foreach (var rImg in frames) {
                // FIX: Use Span to wrap unmanaged memory directly - NO ALLOCATION!
                var span = new Span<byte>(rImg.Data, rImg.Width * rImg.Height * 4);
                // LoadPixelData can work with Span directly
                using var frame = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(span, rImg.Width, rImg.Height);
                frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = 2;
                animated.Frames.AddFrame(frame.Frames.RootFrame);
                Raylib.UnloadImage(rImg);
            }
            frames.Clear();
    
            if (animated.Frames.Count > 1) animated.Frames.RemoveFrame(0);
    
            var outputStream = new MemoryStream();
            animated.Save(outputStream, new WebpEncoder { Quality = 80 });
            outputStream.Position = 0;
            return outputStream;
        }
    }

    private class WinDetail
    {
        public required (int row, int col)[] Path { get; set; }
        public decimal Amount { get; set; }
    }
}

