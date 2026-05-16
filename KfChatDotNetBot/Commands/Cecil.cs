using KfChatDotNetBot.Migrations;
using MathNet.Numerics;
using KfChatDotNetBot.Services;
using MathNet.Numerics.Distributions;
using RandN;
using RandN.Compat;
namespace KfChatDotNetBot.Commands.Kasino;

public static class Cecil
{
    private static RandomShim<StandardRng> _rand = RandomShim.Create<StandardRng>(StandardRng.Create());
    public static double Consult(Skew skew, double minThreshold = 0)
    {
        double r = _rand.NextDouble();
        if (r < skew.LossRate)
        {
            return 0;
        }

        double winRate = 1 - skew.LossRate;
        double scaledR = (r - skew.LossRate) / winRate;

        double baseResult;
        if (skew is BetaSkew betaSkew)
        {
            baseResult = Beta.InvCDF(betaSkew.Weight, betaSkew.Beta, scaledR) * betaSkew.CalibratedMaxWin;
        }
        else if (skew is GammaSkew gammaSkew)
        {
            baseResult = Gamma.InvCDF(gammaSkew.Weight, gammaSkew.Weight, scaledR);
        }
        else return 0;

        baseResult /= winRate;
        
        if (minThreshold == 0) return baseResult;

        double limit = (skew is BetaSkew b) ? b.MaxWin : double.MaxValue;
        
        return Math.Min(Round(baseResult, minThreshold), limit);

        double Round(double baseR, double minT)
        {
            double lower = Math.Floor(baseR / minT) * minT;
            double upper = lower + minT;
            double roundChance = (baseR - lower) / minT;
            return (_rand.NextDouble() < roundChance) ? lower : upper;
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
        double normalizer = TargetEv / CalibratedMaxWin;
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
