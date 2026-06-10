using System.Globalization;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Services;

public static class Cecil
{
    private static readonly RandomShim<StandardRng> Rand = RandomShim.Create(StandardRng.Create());
    public static double Consult(Skew skew, double minThreshold = 0)
    {
        var r = Rand.NextDouble();
        if (r < skew.LossRate)
        {
            return 0;
        }

        var winRate = 1 - skew.LossRate;
        var scaledR = (r - skew.LossRate) / winRate;

        double baseResult;
        switch (skew)
        {
            case BetaSkew betaSkew:
                baseResult = Beta.InvCDF(betaSkew.Alpha, betaSkew.Beta, scaledR) * betaSkew.CalibratedMaxWin;
                break;
            case GammaSkew gammaSkew:
                baseResult = Gamma.InvCDF(gammaSkew.Weight, gammaSkew.Weight, scaledR);
                break;
            default:
                return 0;
        }

        baseResult /= winRate;
        
        if (minThreshold == 0) return baseResult;

        var limit = (skew is BetaSkew b) ? b.MaxWin : double.MaxValue;
        
        return Math.Min(Round(baseResult, minThreshold), limit);

        double Round(double baseR, double minT)
        {
            var lower = Math.Floor(baseR / minT) * minT;
            var upper = lower + minT;
            var roundChance = (baseR - lower) / minT;
            return (Rand.NextDouble() < roundChance) ? lower : upper;
        }
        
    }
}

public abstract class Skew
{
    public double Weight;
    public double LossRate;
    protected double TargetEv = 1;
    public abstract void Calibrate(double winRate);

    public void Rig(double desiredEv, double lr)
    {
        TargetEv = desiredEv;
        Calibrate(1-lr);
    }

    /// <summary>
    /// Parses a serialized configuration token string from the HTML configuration utility
    /// and instantiates a fully calibrated Skew state profile.
    /// </summary>
    /// <param name="token">Example formats: "B:0.5:50:0:0.95" or "G:1.2:0.05:1.0"</param>
    public static Skew FromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Engine initialization token cannot be empty.", nameof(token));

        string[] parts = token.Split(':');
        string typeId = parts[0].ToUpper(CultureInfo.InvariantCulture);

        switch (typeId)
        {
            case "B":
                if (parts.Length != 5)
                    throw new FormatException("BetaSkew token profile requires exactly 5 dynamic segments.");

                double bVol = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double bMw = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double bLr = double.Parse(parts[3], CultureInfo.InvariantCulture);
                double bEv = double.Parse(parts[4], CultureInfo.InvariantCulture);

                // Instantiates structural object components, automatically applying calibrated settings.
                BetaSkew beta = new BetaSkew(bVol, bMw, bLr);
                beta.Rig(bEv, bLr); 
                return beta;

            case "G":
                if (parts.Length != 4)
                    throw new FormatException("GammaSkew token profile requires exactly 4 dynamic segments.");

                double gWght = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double gLr = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double gEv = double.Parse(parts[3], CultureInfo.InvariantCulture);

                GammaSkew gamma = new GammaSkew(gWght, gLr);
                gamma.Rig(gEv, gLr);
                return gamma;

            default:
                throw new NotSupportedException($"Unrecognized skew mathematical distribution identifier classification: '{typeId}'");
        }
    }
}

public class BetaSkew : Skew
{
    public readonly double MaxWin;
    public double CalibratedMaxWin;
    public double Beta;
    public double Alpha;

    public BetaSkew(double vol, double mw, double lr)
    {
        MaxWin = mw;
        Weight = vol;
        LossRate = lr;
        Rig(1, lr);
    }

    public override void Calibrate(double winRate)
    {
        CalibratedMaxWin = MaxWin * winRate;
        var normalizer = TargetEv / CalibratedMaxWin;
        Alpha = normalizer * Weight;
        Beta = (1 - normalizer) * Weight;
    }
}

public class GammaSkew : Skew
{
    public double Alpha;
    public double B;

    public GammaSkew(double wght, double lr)
    {
        Weight = wght;
        LossRate = lr;
        Alpha = Math.Pow(Weight, Weight) / SpecialFunctions.Gamma(wght);
        Rig(1, lr);
    }

    public override void Calibrate(double winRate)
    {
        B = (Weight / TargetEv) * winRate;
    }
}
