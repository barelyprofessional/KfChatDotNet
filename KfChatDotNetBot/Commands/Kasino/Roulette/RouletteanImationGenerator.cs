using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KfChatDotNetBot.Commands.Kasino.Roulette;

public static class RouletteAnimationGenerator
{
    // European Wheel Sequence
    private static readonly int[] WheelNumbers = {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
    };

    /// <summary>
    /// Generates an animated roulette wheel that lands on the specified winning number
    /// </summary>
    /// <param name="winningNumber">The number (0-36) that the ball should land on</param>
    /// <returns>A tuple containing the animation duration in seconds and the WebP animation bytes</returns>
    public static (int durationSeconds, byte[] animationBytes) GenerateAnimation(int winningNumber)
    {
        if (winningNumber < 0 || winningNumber > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(winningNumber), "Winning number must be between 0 and 36");
        }

        using var board = DrawWheelBase();
        int fps = 20;
        int duration = Random.Shared.Next(6, 9);
        int totalFrames = fps * duration;
        using var animation = new Image<Rgba32>(500, 500);

        // Find the index of the winning number in the wheel sequence
        int winningIndex = Array.IndexOf(WheelNumbers, winningNumber);
        if (winningIndex == -1)
        {
            throw new InvalidOperationException($"Winning number {winningNumber} not found in wheel sequence");
        }

        // Set the "Journey"
        float endWheelRotation = 720f + Random.Shared.Next(0, 360);
        float sliceAngle = 360f / 37f;
        
        // The pocket index 'i' is located at (i * sliceAngle) degrees relative to wheel zero.
        // Our '0' pocket was drawn at -90 degrees.
        float pocketOffsetOnWheel = (winningIndex * sliceAngle) - 90;
        
        // This is where the ball MUST be at the end of the video
        float finalBallAngle = endWheelRotation + pocketOffsetOnWheel;

        // Render frames
        for (int i = 0; i < totalFrames; i++)
        {
            float progress = (float)i / totalFrames;
            float ease = 1f - MathF.Pow(1f - progress, 3); // Smooth stop

            // Wheel rotates Clockwise (Adding degrees)
            float currentWheelAngle = endWheelRotation * ease;
            
            // Ball rotates Counter-Clockwise (Starting high and subtracting)
            // We start with 5 extra laps (1800 degrees) and "go back" to the final angle
            float startBallAngle = finalBallAngle + 1800f;
            float currentBallAngle = startBallAngle - ((startBallAngle - finalBallAngle) * ease);

            var frame = new Image<Rgba32>(500, 500);
            frame.Mutate(ctx => {
                // Draw Wheel
                using var rotatedBoard = board.Clone(b => b.Rotate(currentWheelAngle));
                int ox = 250 - (rotatedBoard.Width / 2);
                int oy = 250 - (rotatedBoard.Height / 2);
                ctx.DrawImage(rotatedBoard, new Point(ox, oy), 1f);

                // Ball Radius (Physics)
                float dropT = MathF.Max(0, (progress - 0.7f) / 0.3f);
                float radius = 230 - (45 * MathF.Pow(dropT, 2));
                
                float rads = currentBallAngle * MathF.PI / 180;
                float bx = 250 + (radius * MathF.Cos(rads));
                float by = 250 + (radius * MathF.Sin(rads));
                
                ctx.Fill(Color.White, new EllipsePolygon(bx, by, 14));
            });

            frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = (uint)(1000 / fps);
            animation.Frames.AddFrame(frame.Frames.RootFrame);
            frame.Dispose();
        }

        animation.Frames.RemoveFrame(0);
        using var ms = new MemoryStream();
        animation.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 50 });
        
        return (duration, ms.ToArray());
    }

    private static Image<Rgba32> DrawWheelBase()
    {
        var img = new Image<Rgba32>(500, 500);
        float centerX = 250, centerY = 250, outerRadius = 245, innerRadius = 170, step = 360f / 37f;
        
        img.Mutate(ctx => {
            for (int i = 0; i < 37; i++) {
                float startAngle = i * step - (step / 2) - 90;
                var color = WheelNumbers[i] == 0 ? Color.Green : (i % 2 == 0 ? Color.DarkRed : Color.Black);
                var path = new PathBuilder().AddArc(centerX, centerY, outerRadius, outerRadius, 0, startAngle, step)
                    .AddArc(centerX, centerY, innerRadius, innerRadius, 0, startAngle + step, -step).Build();
                ctx.Fill(color, path);
                ctx.Draw(Color.Gold, 1, path);
                
                string text = WheelNumbers[i].ToString();
                float textAngle = (startAngle + (step / 2)) * MathF.PI / 180;
                float tx = centerX + ((outerRadius + innerRadius) / 2) * MathF.Cos(textAngle);
                float ty = centerY + ((outerRadius + innerRadius) / 2) * MathF.Sin(textAngle);
                
                try {
                    var font = SystemFonts.CreateFont("Arial", 14, FontStyle.Bold);
                    ctx.DrawText(
                        new DrawingOptions { 
                            Transform = Matrix3x2Extensions.CreateRotationDegrees(startAngle + (step / 2) + 90, new PointF(tx, ty)) 
                        }, 
                        text, 
                        font, 
                        Color.White, 
                        new PointF(tx - 6, ty - 9));
                } catch { 
                    // Font loading failed, skip text rendering
                }
            }
            ctx.Fill(Color.DarkSlateGray, new EllipsePolygon(centerX, centerY, innerRadius - 5));
        });
        
        return img;
    }
}