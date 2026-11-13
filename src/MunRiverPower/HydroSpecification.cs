using System;

namespace MunRiverPower
{
    internal static class HydroSpecification
    {
        internal const int TurbineCount = 4;
        internal const double TurbineMW = 34;
        internal const double RatedMW = TurbineCount * TurbineMW;
        internal const double ReferenceLengthMetres = 300;
        internal const double StructuralHeightMetres = 17;
        internal const double MinimumHeadMetres = 3;
        // Engineering assumption, not an EGAT-published efficiency for Pak Mun.
        internal const double AssumedHydraulicEfficiency = 0.90;
        // Verified in the installed game's UI formatter: Power / 1e4 -> MW.
        internal const double GameUnitsPerMW = 10000;
        internal const double RatedGameUnits = RatedMW * GameUnitsPerMW;
        // P[kW] = rho*g*Q*H*eta/1000; the game stores 10 units per kW.
        internal const double ProductionFactor = 1000 * 9.81 * AssumedHydraulicEfficiency / 1000 * 10;
        internal const double CapacityFactor = RatedGameUnits / (ReferenceLengthMetres * StructuralHeightMetres);
        internal const double MetresPerTurbine = ReferenceLengthMetres / TurbineCount;
        internal static int InstalledTurbines(double length)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0) return 0;
            return (int)Math.Min(1000000, Math.Floor((length + 0.0001) / MetresPerTurbine));
        }
        internal static double InstalledMW(double length) => InstalledTurbines(length) * TurbineMW;
        internal static double PriceRatio(double length, double nativeCapacityUnits)
        {
            var installed = InstalledMW(length) * GameUnitsPerMW;
            // An unfinished span is still civil infrastructure, not a free dam wall.
            if (installed <= 0 || double.IsNaN(nativeCapacityUnits) || double.IsInfinity(nativeCapacityUnits) || nativeCapacityUnits <= 0) return 1;
            return Math.Min(1, installed / nativeCapacityUnits);
        }
        internal static int ScalePrice(int nativePrice, double ratio) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(nativePrice * ratio, MidpointRounding.AwayFromZero)));

        internal static double CapacityHeight(double operationalEfficiency, double head, double effectiveCapacityFactor = CapacityFactor, double installedMW = RatedMW)
        {
            if (double.IsNaN(head) || double.IsInfinity(head) || head < MinimumHeadMetres) return 0;
            if (double.IsNaN(operationalEfficiency) || double.IsInfinity(operationalEfficiency) || operationalEfficiency < 0) return 0;
            if (double.IsNaN(effectiveCapacityFactor) || double.IsInfinity(effectiveCapacityFactor) || effectiveCapacityFactor <= 0) return 0;
            // Native AI multiplies capacity by its efficiency. Compensate boosts above
            // 100% so staffing/budget bonuses cannot exceed the installed turbine rating.
            return Math.Max(0, installedMW) * GameUnitsPerMW / (ReferenceLengthMetres * effectiveCapacityFactor * Math.Max(1, operationalEfficiency));
        }
        internal static double PotentialGameUnits(double dischargeM3s, double headMetres)
        {
            if (dischargeM3s <= 0 || double.IsNaN(dischargeM3s) || double.IsInfinity(dischargeM3s) || CapacityHeight(1, headMetres) == 0) return 0;
            return Math.Min(RatedGameUnits, dischargeM3s * headMetres * ProductionFactor);
        }
        internal static double PreviewProductionGameUnits(double length, double wetHead, double hydraulicEstimate)
        {
            if (double.IsNaN(hydraulicEstimate) || double.IsInfinity(hydraulicEstimate) || hydraulicEstimate <= 0 || CapacityHeight(1, wetHead) == 0) return 0;
            return Math.Min(InstalledMW(length) * GameUnitsPerMW, hydraulicEstimate * ProductionFactor);
        }
        internal static float OperationalEfficiency(float[] factors)
        {
            var product = 1f;
            for (var i = 0; i < factors.Length; i++)
            {
                if (i >= 17 && i <= 20) continue;
                product *= Math.Max(0, factors[i]);
            }
            if (float.IsNaN(product) || float.IsInfinity(product) || product <= 0) return 0;
            return Math.Max(0.01f, (float)Math.Round(100f * product, MidpointRounding.ToEven) / 100f);
        }
    }
}
