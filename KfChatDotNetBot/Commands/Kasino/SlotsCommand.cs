using System.Net.Http.Headers;
using RandN;
using RandN.Compat;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class SlotsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^slots (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^slots (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^slots$", RegexOptions.IgnoreCase),
        new Regex(@"^sluts (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^sluts (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^sluts", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!slots [bet amount]";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(30)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel messagen, UserDbModel user,
        GroupCollection arguments, CancellationToken ctx)
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

        decimal winnings;
        using (var board = new KiwiSlotBoard(wager))
        {
            board.LoadAssets();
            board.ExecuteGameLoop();
            using (var finalImageStream = board.ExportAndCleanup())
            {
                if (finalImageStream == null)
                {
                    throw new InvalidOperationException("board.ExportAndCleanup returned null");
                }
                var imageUrl = await Zipline.Upload(finalImageStream, new MediaTypeHeaderValue("image/webp"), "1h", ctx);
                await botInstance.SendChatMessageAsync($"[img]{imageUrl}[/img]", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(150));
            }

            winnings = (decimal)board.RunningTotalDisplay;
        }
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Slots, ct: ctx);
        if (winnings == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()} you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]lost[/color]. Current balance: {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: TimeSpan.FromSeconds(150));
            return;
        }
        
        newBalance = await Money.NewWagerAsync(gambler.Id, wager, winnings, WagerGame.Slots, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]won[/color] {await winnings.FormatKasinoCurrencyAsync()}! Current balance: {await newBalance.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(150));
    }
    public class WinDetail
    {
        public required (int row, int col)[] Path { get; set; }
        public double Amount { get; set; }
    }

    private class KiwiSlotBoard : IDisposable
    {
        private const char WILD = 'K', FEATURE = 'L', EXPANDER = 'M';
        private Image<Rgba32>? _headerImg;
        private Dictionary<char, Image<Rgba32>> _symbolImgs = new();
        private Dictionary<int, Image<Rgba32>> _expanderImgs = new();
        private Font? _font;

        // Optimized Animation Container
        private Image<Rgba32> AnimatedImage { get; set; }

        private readonly char[,] _preboard = new char[5, 5];
        private char[,] _board = new char[5, 5];
        private readonly decimal _userBet;
        public double RunningTotalDisplay = 0;
        private int _activeFeatureTier = 0, _currentFeatureSpin = 0;
        private bool _showGoldCircle = false;

        private readonly RandomShim<StandardRng> _rand = RandomShim.Create(StandardRng.Create());
        private static readonly List<char> ExpanderWild =
            ['N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '1', '2'];
        private readonly Dictionary<char, double> _multiTable = new() { { 'N', 2 }, { 'O', 3 }, { 'P', 4 }, { 'Q', 5 }, { 'R', 6 }, { 'S', 7 }, { 'T', 8 }, { 'U', 9 }, { 'V', 10 }, { 'W', 15 }, { 'X', 20 }, { 'Y', 25 }, { 'Z', 50 }, { '1', 100 }, { '2', 200 } };
        private readonly Dictionary<string, double> _payoutTable = new() { { "A3", 0.2 }, { "A4", 1.0 }, { "A5", 5.0 }, { "B3", 0.2 }, { "B4", 1.0 }, { "B5", 5.0 }, { "C3", 0.3 }, { "C4", 1.5 }, { "C5", 7.5 }, { "D3", 0.3 }, { "D4", 1.5 }, { "D5", 7.5 }, { "E3", 0.4 }, { "E4", 2.0 }, { "E5", 10.0 }, { "F3", 1.0 }, { "F4", 5.0 }, { "F5", 15.0 }, { "G3", 1.0 }, { "G4", 5.0 }, { "G5", 15.0 }, { "H3", 1.5 }, { "H4", 7.5 }, { "H5", 17.5 }, { "I3", 1.5 }, { "I4", 7.5 }, { "I5", 17.5 }, { "J3", 2.0 }, { "J4", 10.0 }, { "J5", 20.0 }, { "K5", 25.0 }, { "L5", 25.0 }, { "M5", 25.0 } };
        private readonly List<(int row, int col)[]> _payoutLines =
        [
            [(0, 0), (0, 1), (0, 2), (0, 3), (0, 4)], [(1, 0), (1, 1), (1, 2), (1, 3), (1, 4)],
            [(2, 0), (2, 1), (2, 2), (2, 3), (2, 4)], [(3, 0), (3, 1), (3, 2), (3, 3), (3, 4)],
            [(4, 0), (4, 1), (4, 2), (4, 3), (4, 4)], [(0, 0), (1, 1), (2, 2), (3, 3), (4, 4)],
            [(4, 0), (3, 1), (2, 2), (1, 3), (0, 4)], [(1, 0), (0, 1), (1, 2), (0, 3), (1, 4)],
            [(2, 0), (1, 1), (2, 2), (1, 3), (2, 4)], [(3, 0), (2, 1), (3, 2), (2, 3), (3, 4)],
            [(4, 0), (3, 1), (4, 2), (3, 3), (4, 4)], [(0, 0), (1, 1), (0, 2), (1, 3), (0, 4)],
            [(1, 0), (2, 1), (1, 2), (2, 3), (1, 4)], [(2, 0), (3, 1), (2, 2), (3, 3), (2, 4)],
            [(3, 0), (4, 1), (3, 2), (4, 3), (3, 4)], [(2, 0), (1, 1), (0, 2), (1, 3), (2, 4)],
            [(3, 0), (2, 1), (1, 2), (2, 3), (3, 4)], [(2, 0), (3, 1), (4, 2), (3, 3), (2, 4)],
            [(1, 0), (2, 1), (3, 2), (2, 3), (1, 4)]
        ];

        public KiwiSlotBoard(decimal bet)
        {
            _userBet = bet;
            AnimatedImage = new Image<Rgba32>(600, 800);
        }

        public void LoadAssets()
        {
            var assetPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Default");

            if (!Directory.Exists(assetPath)) throw new DirectoryNotFoundException($"Assets folder missing at {assetPath}");

            _headerImg = Image.Load<Rgba32>(System.IO.Path.Combine(assetPath, "header.png"));
            foreach (var c in "ABCDEFGHIJKL") _symbolImgs[c] = Image.Load<Rgba32>(System.IO.Path.Combine(assetPath, $"{c}.png"));
            for (var i = 1; i <= 5; i++) _expanderImgs[i] = Image.Load<Rgba32>(System.IO.Path.Combine(assetPath, $"exp{i}.png"));
            _font = SystemFonts.CreateFont("Arial", 20, FontStyle.Bold);
        }

        private void RenderFrame(int dropOffset = 500, List<WinDetail>? activeWins = null)
        {
            if (_font == null || _headerImg == null)
            {
                throw new InvalidOperationException("_font or _headerImg was null");
            }
            using var frame = new Image<Rgba32>(600, 800);
            frame.Mutate(ctx => {
                ctx.Fill(Color.Black);

                // --- SIDEBAR SECTION ---
                var sidebarX = 0;
                int[] tiers = [3, 4, 5];
                int[] yCoords = [150, 300, 450];

                for (var i = 0; i < 3; i++) {
                    var t = tiers[i];
                    var y = yCoords[i];
                    if (_showGoldCircle && _activeFeatureTier == t)
                        ctx.Fill(Color.Gold, new EllipsePolygon(sidebarX + 50, y + 50, 48));

                    if (_symbolImgs.TryGetValue(FEATURE, out var feat))
                        ctx.DrawImage(feat, new Point(sidebarX, y), 1f);

                    var lb = $"x{t}";
                    var sz = TextMeasurer.MeasureSize(lb, new TextOptions(_font));
                    ctx.DrawText(lb, _font, Color.White, new PointF(sidebarX + 50 - (sz.Width / 2), y + 105));
                }

                // --- MAIN REEL SECTION ---
                var mainX = 100;
                ctx.DrawImage(_headerImg, new Point(mainX, 0), 1f);

                var boardRect = new Rectangle(mainX, 200, 500, 500);
                ctx.Clip(new RectangularPolygon(boardRect), clipCtx => {
                    var occupied = new bool[5, 5];
                    float animationY = (500 - dropOffset);

                    for (var j = 0; j < 5; j++) {
                        for (var i = 0; i < 5; i++) {
                            if (occupied[i, j]) continue;
                            var sym = _board[i, j];
                            var x = mainX + (j * 100);
                            var y = (200 + (i * 100)) - (int)animationY;
                                
                            if (sym == EXPANDER || _multiTable.ContainsKey(sym)) {
                                var h = 0;
                                for (var k = i; k < 5; k++) if (_board[k, j] == sym) h++; else break;
                                if (_expanderImgs.TryGetValue(h, out var tex)) {
                                    clipCtx.DrawImage(tex, new Point(x, y), 1f);
                                    if (_multiTable.TryGetValue(sym, out var mVal))
                                        clipCtx.DrawText($"x{mVal}", _font, Color.Yellow, new PointF(x + 50, y + (h * 50)));
                                }
                                for (var k = 0; k < h; k++) occupied[i + k, j] = true;
                            }
                            else if (_symbolImgs.TryGetValue(sym, out var tex)) clipCtx.DrawImage(tex, new Point(x, y), 1f);
                        }
                    }

                    if (activeWins != null) {
                        foreach (var win in activeWins) {
                            var points = win.Path.Select(p => new PointF(mainX + (p.col * 100 + 50), 200 + (p.row * 100) + 50 - animationY)).ToArray();
                            clipCtx.Draw(new SolidPen(Color.White, 8f), new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(points)));

                            var amtText = $"${win.Amount:F2}";
                            var midPoint = points[win.Path.Length / 2];
                            var size = TextMeasurer.MeasureSize(amtText, new TextOptions(_font));
                            var bgRect = new RectangularPolygon(midPoint.X - (size.Width / 2) - 5, midPoint.Y - (size.Height / 2) - 2, size.Width + 10, size.Height + 4);
                            clipCtx.Fill(Color.FromRgba(0, 0, 0, 200), bgRect);
                            clipCtx.DrawText(amtText, _font, Color.LimeGreen, new PointF(midPoint.X - (size.Width / 2), midPoint.Y - (size.Height / 2)));
                        }
                    }
                });

                // --- FOOTER SECTION ---
                ctx.Fill(Color.FromRgb(15, 15, 15), new Rectangle(0, 700, 600, 100));
                ctx.DrawLine(Color.Gold, 3f, new PointF(0, 700), new PointF(600, 700));

                var largeFont = SystemFonts.CreateFont("Arial", 35, FontStyle.Bold);

                void DrawAutoScaledText(string text, Font font, Color color, RectangleF targetArea) {
                    var textOptions = new TextOptions(font);
                    var size = TextMeasurer.MeasureSize(text, textOptions);
                    var scale = 1.0f;
                    if (size.Width > targetArea.Width) scale = targetArea.Width / size.Width;

                    // FIX: Added 'using' to prevent Font object leaks
                    var finalFont = new Font(font, font.Size * scale);
                    var finalSize = TextMeasurer.MeasureSize(text, new TextOptions(finalFont));
                    var yPos = targetArea.Y + (targetArea.Height - finalSize.Height) / 2;
                    var xPos = targetArea.X + (targetArea.Width - finalSize.Width) / 2;
                    ctx.DrawText(text, finalFont, color, new PointF(xPos, yPos));
                        
                }

                DrawAutoScaledText($"BET: ${_userBet:F2}", largeFont, Color.White, new RectangleF(20, 700, 180, 100));
                DrawAutoScaledText($"WIN: ${RunningTotalDisplay:F2}", largeFont, Color.Gold, new RectangleF(380, 700, 200, 100));

                if (_currentFeatureSpin > 0) {
                    var total = _activeFeatureTier switch { 3 => 3, 4 => 5, 5 => 10, _ => 0 };
                    DrawAutoScaledText($"SPIN {_currentFeatureSpin}/{total}", largeFont, Color.SkyBlue, new RectangleF(210, 700, 160, 100));
                }
            });

            // Set delay and push to master animation
            frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = 2;
            AnimatedImage.Frames.AddFrame(frame.Frames.RootFrame);
        }

        public MemoryStream? ExportAndCleanup()
        {
            if (AnimatedImage.Frames.Count <= 1) return null;

            var ms = new MemoryStream();
            // Remove the blank placeholder frame
            AnimatedImage.Frames.RemoveFrame(0);
            
            AnimatedImage.Save(ms, new WebpEncoder { Quality = 80 });
            ms.Position = 0;

            // Free the animation memory now that it's encoded
            ResetAnimation();
            return ms;
        }

        private void ResetAnimation()
        {
            while (AnimatedImage.Frames.Count > 1)
                AnimatedImage.Frames.RemoveFrame(0);
        }

        public void ExecuteGameLoop(int featureSpins = 0)
        {
            GeneratePreBoard(featureSpins);
            var fCount = 0;
            for (var i = 0; i < 5; i++) for (var j = 0; j < 5; j++) if (_preboard[i, j] == FEATURE) fCount++;

            if (featureSpins == 0) {
                _activeFeatureTier = fCount >= 5 ? 5 : (fCount >= 3 ? fCount : 0);
                _showGoldCircle = _activeFeatureTier >= 3; _currentFeatureSpin = 0;
            } else {
                _showGoldCircle = true; _currentFeatureSpin = featureSpins;
            }

            ProcessReelsAndWins();
            var total = _activeFeatureTier switch { 3 => 3, 4 => 5, 5 => 10, _ => 0 };
            if (featureSpins == 0) for (var s = 1; s <= total; s++) ExecuteGameLoop(s);
        }

        private void ProcessReelsAndWins()
        {
            _board = (char[,])_preboard.Clone();
            for (var o = 0; o <= 500; o += 50) RenderFrame(o);
            List<char> multis = new(_multiTable.Keys);
            for (var j = 0; j < 5; j++) {
                for (var i = 0; i < 5; i++) {
                    if (_preboard[i, j] == EXPANDER) {
                        var hitWild = false;
                        for (var c = i; c < 5; c++) if (_preboard[c, j] == WILD) hitWild = true;
                        var mSym = hitWild ? multis[_rand.Next(multis.Count)] : EXPANDER;
                        for (var r = i; r < 5; r++) _board[r, j] = mSym;
                        RenderFrame(); break;
                    }
                }
            }
            var winners = GetWinningLinesCoordsWithPayouts();
            var target = RunningTotalDisplay + winners.Sum(w => w.Amount);
            foreach (var win in winners) {
                var inc = win.Amount / 10.0;
                for (var f = 0; f < 10; f++) { RunningTotalDisplay += inc; RenderFrame(500, [win]); }
            }
            RunningTotalDisplay = target; RenderFrame();
        }

        private List<WinDetail> GetWinningLinesCoordsWithPayouts()
        {
            List<WinDetail> res = [];
            foreach (var line in _payoutLines) {
                var ch = '0'; var count = 0; double m = 0; var spec = true;
                foreach (var (r, c) in line) {
                    var cell = _board[r, c];
                    if (cell != WILD && cell != FEATURE && cell != EXPANDER && !ExpanderWild.Contains(cell)) { ch = cell; spec = false; break; }
                }
                if (!spec) {
                    foreach (var (r, c) in line) {
                        var cell = _board[r, c];
                        if (cell == ch || cell == WILD || cell == FEATURE || ExpanderWild.Contains(cell) || cell == EXPANDER) {
                            count++; if (ExpanderWild.Contains(cell)) m += _multiTable[cell];
                        } else if (count < 3) { count = 0; break; } else break;
                    }
                } else { ch = _board[line[0].row, line[0].col]; count = 5; foreach (var (r, c) in line) if (ExpanderWild.Contains(_board[r, c])) m += _multiTable[_board[r, c]]; }
                if (count >= 3) {
                    if (m == 0) m = 1;
                    if (_payoutTable.TryGetValue($"{ch}{count}", out var baseW)) {
                        var path = new (int, int)[count]; Array.Copy(line, path, count);
                        res.Add(new WinDetail { Path = path, Amount = (double)_userBet * baseW * m });
                    }
                }
            }
            return res;
        }

        private void GeneratePreBoard(int f = 0, char rigged = '0')
        {
            var fc = 0; HashSet<int> ex = [];
            for (var i = 0; i < 5; i++) {
                for (var j = 0; j < 5; j++) {
                    var r = _rand.NextDouble() * 100.6;
                    if (f != 0 && j > 2) r *= 1.05;
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
                    else if (r < (j <= 2 ? 99 : 99.5)) { if (!ex.Contains(j)) { _preboard[i, j] = EXPANDER; ex.Add(j); } else _preboard[i, j] = WILD; }
                    else { if (fc < 5) { _preboard[i, j] = FEATURE; fc++; } else _preboard[i, j] = WILD; }
                }
            }
        }

        public void Dispose()
        {
            _headerImg?.Dispose();
            foreach (var img in _symbolImgs.Values) img.Dispose();
            foreach (var img in _expanderImgs.Values) img.Dispose();
            AnimatedImage?.Dispose();
        }
    }
}