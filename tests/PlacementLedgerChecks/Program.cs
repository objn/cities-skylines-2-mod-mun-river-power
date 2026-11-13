using System;
using System.Linq;
using MunRiverPower;

static class Program
{
    static void Require(bool condition, string failure) { if (!condition) throw new Exception(failure); }
    static void Main()
    {
        var ledger = new PlacementLedger<int>();
        ledger.Observe(new[] { 1 });
        Require(ledger.Pending.Length == 0, "An ordinary stock placement while inactive must not become a mod placement.");
        ledger.Begin(new[] { 10, 20 });
        ledger.Observe(new[] { 10, 20 });
        Require(ledger.Pending.Length == 0, "Existing stock dams must remain unchanged.");
        ledger.Observe(new[] { 10, 20, 30 });
        ledger.Observe(new[] { 10, 20, 30 });
        Require(ledger.Pending.SequenceEqual(new[] { 30 }), "One new producer must be processed once.");
        ledger.End(new[] { 10, 20, 30, 40 });
        Require(!ledger.Active && ledger.Pending.OrderBy(x => x).SequenceEqual(new[] { 30, 40 }), "Leaving the tool must retain already observed construction awaiting completion.");
        ledger.Observe(new[] { 10, 20, 30, 40, 50 });
        Require(!ledger.Pending.Contains(50), "Later stock-tool construction must not be captured.");
        ledger.Begin(new[] { 10, 20, 30, 40, 50 });
        Require(ledger.Pending.OrderBy(x => x).SequenceEqual(new[] { 30, 40 }), "Starting another placement must not discard unfinished prior construction.");
        ledger.Complete(30);
        ledger.Complete(40);
        ledger.Observe(new[] { 10, 20, 30, 40, 50 });
        Require(ledger.Pending.Length == 0, "Completed conversions must not be repeated or recaptured.");
        ledger.Observe(new[] { 10, 20, 30, 40, 50, 60 });
        ledger.Reset();
        ledger.Observe(new[] { 100, 200 });
        Require(!ledger.Active && ledger.Pending.Length == 0, "Loading another city must discard pending work and never modify newly loaded stock dams.");
        Console.WriteLine("PASS: eight placement-session isolation checks.");
        var declared = new[] { "Object", "Transform", "WaterPowered" };
        var nativeBaked = declared.Concat(new[] { "Created", "Updated", "InstalledUpgrade", "SubNet" }).ToArray();
        var modBaked = declared.Concat(new[] { "Created", "Updated" }).ToArray();
        Require(LayoutCompatibility<string>.CanAdoptNative(declared, declared, nativeBaked, modBaked), "Matching authored layouts may adopt native late-initialization additions.");
        Require(LayoutCompatibility<string>.CanAdoptNative(declared, declared, nativeBaked, nativeBaked), "Already matching baked layouts are compatible regardless of order.");
        Require(LayoutCompatibility<string>.CanAdoptNative(declared.Reverse(), declared, nativeBaked.Reverse(), modBaked), "Layout compatibility must compare sets, not component order.");
        Require(!LayoutCompatibility<string>.CanAdoptNative(declared, declared.Concat(new[] { "SpecialPhysics" }), nativeBaked, modBaked), "Authored contract differences must block placement.");
        Require(!LayoutCompatibility<string>.CanAdoptNative(declared, declared, nativeBaked, modBaked.Concat(new[] { "ModOnlyBuffer" })), "Native adoption must never discard mod-only runtime components.");
        Require(!LayoutCompatibility<string>.CanAdoptNative(declared, declared, new[] { "Object", "Transform" }, modBaked), "An incomplete native archetype must not be adopted.");
        Console.WriteLine("PASS: six producer layout compatibility checks.");
        Require(HydroSpecification.RatedMW == 136 && HydroSpecification.RatedGameUnits == 1360000, "Rated capacity must be 4 x 34 MW in verified game UI units.");
        Require(HydroSpecification.PotentialGameUnits(0, 10) == 0, "No discharge must produce no power.");
        Require(HydroSpecification.PotentialGameUnits(1000, 2.99) == 0, "Below 3m head must produce no power.");
        Require(Math.Abs(HydroSpecification.PotentialGameUnits(100, 3) / 10000 - 2.6487) < 1e-8, "100m3/s at 3m and 90% efficiency must yield 2.6487 MW.");
        Require(HydroSpecification.PotentialGameUnits(100000, 10) == 1360000, "Large flow must be limited to installed capacity.");
        Require(HydroSpecification.CapacityHeight(1, double.NaN) == 0 && HydroSpecification.CapacityHeight(1, 0) == 0, "Invalid head must fail closed.");
        foreach (var efficiency in new[] { 0.0, 0.5, 1.0, 1.37, 2.0, 10.0 })
        {
            var height = HydroSpecification.CapacityHeight(efficiency, 5);
            var nativeCapacity = 300 * height * HydroSpecification.CapacityFactor * efficiency;
            Require(nativeCapacity <= 1360000.00001, "Operational bonuses must not raise the turbine rating.");
            Require(Math.Abs(nativeCapacity - 1360000 * Math.Min(efficiency, 1)) < 1e-6, "Reduced operational efficiency must reduce capacity.");
        }
        var raisedFactor = HydroSpecification.CapacityFactor + 100;
        Require(Math.Abs(300 * HydroSpecification.CapacityHeight(2, 5, raisedFactor) * raisedFactor * 2 - 1360000) < 1e-6, "Added water capacity factors must not raise the fixed rating.");
        var factors = Enumerable.Repeat(1f, 128).ToArray();
        factors[3] = 1.25f; factors[17] = 0.01f;
        Require(HydroSpecification.OperationalEfficiency(factors) == 1.25f, "Power AI's excluded efficiency factors must not affect capacity normalization.");
        factors[3] = -1;
        Require(HydroSpecification.OperationalEfficiency(factors) == 0, "Negative operational efficiency must not generate negative power.");
        Console.WriteLine("PASS: physical power, cut-in, unit conversion, rating cap and efficiency checks.");
        var spans = new[] { (0.0, 0), (74.99, 0), (75.0, 1), (149.99, 1), (150.0, 2), (225.0, 3), (299.99, 3), (300.0, 4), (375.0, 5), (600.0, 8) };
        foreach (var (length, turbines) in spans)
        {
            var mw = turbines * 34.0;
            Require(HydroSpecification.InstalledTurbines(length) == turbines && HydroSpecification.InstalledMW(length) == mw, "Turbines must step at each complete 75m, without a four-turbine limit.");
            foreach (var efficiency in new[] { 0.5, 1.0, 2.0 })
            {
                var height = HydroSpecification.CapacityHeight(efficiency, 5, HydroSpecification.CapacityFactor, mw);
                var outputCap = 300 * height * HydroSpecification.CapacityFactor * efficiency / 10000;
                Require(Math.Abs(outputCap - mw * Math.Min(1, efficiency)) < 1e-6, "Every modular rating must retain its cap under staffing bonuses.");
            }
        }
        Require(HydroSpecification.InstalledTurbines(double.NaN) == 0 && HydroSpecification.InstalledTurbines(double.PositiveInfinity) == 0, "Invalid lengths must fail closed.");
        var ratio = HydroSpecification.PriceRatio(300, 272 * 10000);
        Require(Math.Abs(ratio - 0.5) < 1e-10, "136MW against a 272MW native dam must cost 50%.");
        Require(HydroSpecification.ScalePrice(2000000, ratio) == 1000000 && HydroSpecification.ScalePrice(10000, ratio) == 5000, "Construction and upkeep must use the same ratio.");
        Require(HydroSpecification.PriceRatio(300, 68 * 10000) == 1, "A weak native baseline must not increase the price.");
        Require(HydroSpecification.PriceRatio(74, 2000000) == 1 && HydroSpecification.PriceRatio(300, 0) == 1, "Incomplete spans and unavailable baselines retain civil costs.");
        Require(HydroSpecification.ScalePrice(-10000, ratio) == -5000, "Refunds must scale too, preventing full-price demolition arbitrage.");
        Require(HydroSpecification.ScalePrice(1, 0.5) == 1, "Half-unit invoice rounding must be explicit.");
        Console.WriteLine("PASS: ten turbine-length boundaries, modular efficiency caps, proportional construction/upkeep and refund checks.");
        // Reproduce the reported deep-excavation tooltip: 401m showed 307/568MW
        // although only five 34MW turbines fit in that length.
        foreach (var nativeCapacityMW in new[] { 68.0, 170.0, 568.0, 5000.0 })
        {
            const double nativeCapacityFactor = 1000;
            var originalFoundationHeight = nativeCapacityMW * 10000 / (401 * nativeCapacityFactor);
            var oldCapacity = Math.Min(401 * originalFoundationHeight, 1000000) * nativeCapacityFactor;
            var displayHeight = HydroSpecification.CapacityHeight(1, 3, nativeCapacityFactor, HydroSpecification.InstalledMW(401));
            var displayCapacity = Math.Min(300 * displayHeight, 1000000) * nativeCapacityFactor;
            Require(Math.Abs(oldCapacity / 10000 - nativeCapacityMW) < 1e-8, "Regression fixture must represent native foundation-depth capacity.");
            Require(Math.Abs(displayCapacity / 10000 - 170) < 1e-8, "A 401m preview must show 170MW regardless of foundation depth.");
        }
        Require(HydroSpecification.PreviewProductionGameUnits(401, 5, 3070000 / HydroSpecification.ProductionFactor) == 1700000, "The reported 307MW estimate must be clamped to five turbines (170MW).");
        Require(Math.Abs(HydroSpecification.PreviewProductionGameUnits(401, 3, 300) - 26487) < 1e-8, "Normal-flow preview must use the same 90% hydraulic factor as production.");
        Require(HydroSpecification.PreviewProductionGameUnits(401, 2.99, 1000000) == 0, "Low-head preview cannot promise generation below cut-in.");
        Require(HydroSpecification.PreviewProductionGameUnits(74, 5, 1000000) == 0, "No complete turbine must mean no preview generation.");
        Require(HydroSpecification.PreviewProductionGameUnits(401, 5, double.NaN) == 0 && HydroSpecification.PreviewProductionGameUnits(401, 5, double.PositiveInfinity) == 0, "Invalid estimates must not leak NaN or infinity to the UI.");
        Console.WriteLine("PASS: deep-excavation 307/568MW preview regression, depth-independent 170MW rating, normal flow, low head and invalid inputs.");
        Require(!SpillwayControl.NextOpen(false, 0.5001, true, true), "A closed gate must wait for 0.5m freeboard.");
        Require(SpillwayControl.NextOpen(false, 0.5, true, true), "The gate must open at exactly 0.5m.");
        Require(SpillwayControl.NextOpen(false, -0.2, true, true), "Overtopping must also open the gate.");
        Require(SpillwayControl.NextOpen(true, 0.9, true, true) && !SpillwayControl.NextOpen(false, 0.9, true, true), "The hysteresis band must retain the gate's previous state.");
        Require(!SpillwayControl.NextOpen(true, 1, true, true), "The gate must close at exactly 1m.");
        Require(!SpillwayControl.NextOpen(true, 0.997, true, true), "Within 5mm of target the gate must close instead of asymptotically pulsing forever.");
        Require(!SpillwayControl.NextOpen(true, 0, false, true) && !SpillwayControl.NextOpen(true, 0, true, false), "Disabled upgrades and unavailable water must close the gate.");
        Require(!SpillwayControl.NextOpen(true, double.NaN, true, true) && !SpillwayControl.NextOpen(true, double.PositiveInfinity, true, true), "Invalid freeboard must fail closed.");
        Require(SpillwayControl.RemovalDepth(true, 1, 10) == 0 && SpillwayControl.RemovalDepth(false, 0, 10) == 0, "Closed/target-level gates must transfer no water.");
        Require(SpillwayControl.RemovalDepth(true, 0, 0.24) == 0, "Shallow intake must not be drained.");
        var nearClose = SpillwayControl.RemovalDepth(true, 0.99, 10);
        Require(nearClose > 0 && nearClose < SpillwayControl.RemovalDepth(true, 0.5, 10) && nearClose < 0.01, "Release must taper before the closing level.");
        Require(SpillwayControl.RemovalDepth(true, -100, 10) <= 0.025, "Even an extreme overtop must respect the per-readback pulse limit.");
        var readbacks = new FreshWaterReadback();
        Require(readbacks.TryConsume(10) && !readbacks.TryConsume(10) && readbacks.TryConsume(11) && !readbacks.TryConsume(11), "One water readback may debit the intake at most once, even at high simulation speed.");
        readbacks.Reset(); Require(readbacks.TryConsume(10), "Loading a save must reset readback identity.");
        Require(readbacks.TryConsume(uint.MaxValue) && readbacks.TryConsume(0), "Water frame wraparound must not disable release permanently.");
        foreach (SpillwayStatus status in Enum.GetValues(typeof(SpillwayStatus)))
            foreach (var freeboard in new[] { -10.0, -5.0, 0.0, 0.5, 1.0, 20.0, 100.0, double.NaN })
            {
                var bucket = SpillwayControl.DisplayBucket(freeboard);
                Require(bucket >= 0 && bucket <= 253 && !string.IsNullOrWhiteSpace(SpillwayControl.StatusText(false, status, bucket)) && !string.IsNullOrWhiteSpace(SpillwayControl.StatusText(true, status, bucket)), "Every emitted status key needs both language values.");
            }
        Console.WriteLine("PASS: spillway open/close thresholds, hysteresis, disable/dry/invalid guards, tapered pulses, readback deduplication and localized status coverage.");
    }
}
