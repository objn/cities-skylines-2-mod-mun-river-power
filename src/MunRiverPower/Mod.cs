using System;
using System.Collections.Generic;
using System.Linq;
using Colossal;
using Colossal.Logging;
using Colossal.Localization;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger(nameof(MunRiverPower)).SetShowsErrorsInUI(false);
        private List<KeyValuePair<string, IDictionarySource>> sources;
        private LocalizationManager localization;
        private static Mod current;
        internal static bool IsLoaded => current != null;
        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("Mun River Power 0.17.6 loading. Spillway equipment clears the traffic deck and road tint is recalibrated.");
            // The game creates IMod through GetUninitializedObject: field initializers do not run.
            sources = new List<KeyValuePair<string, IDictionarySource>>();
            localization = GameManager.instance.localizationManager;
            current = this;
            RegisterSpillwayStatusText();
            // PrefabSystem invokes the entire PrefabUpdate phase. Register relative to the
            // initializer INSIDE that phase, before it collects Created prefab entities.
            updateSystem.UpdateBefore<WeirPrefabSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<WeirEconomicProfilesSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<FishPassageUpgradeSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAfter<FishPassageUpgradeLinkSystem, BuildingInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<SpillwayUpgradeSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAfter<SpillwayUpgradeLinkSystem, BuildingInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<WeirNativePlacementSystem>(SystemUpdatePhase.PreTool);
            updateSystem.UpdateBefore<WeirConstructionRestoreSystem, Game.Net.CostSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAfter<WeirConstructionEconomySystem, Game.Buildings.WaterPoweredInitializeSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateBefore<WeirConstructionEconomySystem, Game.Tools.ToolApplySystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateBefore<WeirRatedPowerSystem, Game.Simulation.PowerPlantAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<SpillwaySimulationSystem, Game.Simulation.PowerPlantAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterGPUSystem<SpillwaySimulationSystem>();
            updateSystem.UpdateBefore<SpillwayInfoSystem, Game.UI.InGame.SelectedInfoUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateBefore<WeirBuiltVisualSystem, Game.UI.InGame.SelectedInfoUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateBefore<WeirPowerPreviewSystem, Game.UI.Tooltip.TempRenewableElectricityProductionTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAfter<WeirPowerPreviewRestoreSystem, Game.UI.Tooltip.TempRenewableElectricityProductionTooltipSystem>(SystemUpdatePhase.UITooltip);
            // In this game build PowerPlantAISystem precedes both upkeep systems.
        }
        internal static void RegisterText(IEnumerable<PrefabBase> prefabs)
        {
            if (current?.localization == null) throw new InvalidOperationException("Localization is not ready.");
            var assets = prefabs.ToArray();
            foreach (var locale in current.localization.GetSupportedLocales())
            {
                var entries = new Dictionary<string, string>();
                var thai = locale.StartsWith("th", StringComparison.OrdinalIgnoreCase);
                foreach (var prefab in assets)
                {
                    var root = prefab.name == WeirFactory.RootName;
                    var fish = prefab.name == FishPassageSpecification.Name;
                    var spillway = prefab.name == SpillwayControl.UpgradeName;
                    var title = spillway ? (thai ? "Automatic Spillway — ทางระบายน้ำล้นอัตโนมัติ" : "Automatic Spillway") : fish ? (thai ? "Fish Passage — ทางผ่านปลา" : "Fish Passage") : root ? "Mun River Low-head Weir" : "Mun River Powerhouse";
                    var description = spillway
                        ? (thai
                            ? "ตรวจระยะผิวน้ำถึงสันเขื่อน เปิดระบายเมื่อเหลือไม่เกิน 0.5 ม. และปิดเมื่อกลับถึง 1 ม. ค่อยลดการระบายใกล้จุดหยุด ย้ายน้ำจากฝั่งกักเก็บไปฝั่งท้ายน้ำ โดยไม่เพิ่มพิกัดกังหัน หนึ่งชุดต่อโรง ค่าติดตั้ง ₡100,000 บำรุง ₡1,000/เดือน อัปเกรดภายในอาคาร ระยะอ่านจากแบบจำลองน้ำและแนวสันในเกม"
                            : "Monitors upstream freeboard. Opens at 0.5 m or less and closes at 1.0 m, tapering release near the target. Transfers water downstream without adding turbine capacity. One per powerhouse. Installation ₡100,000; upkeep ₡1,000/month. Internal upgrade; measurements use the simulated water and structural crest.")
                        : fish
                        ? (thai
                            ? "ทางผ่านปลาที่ออกแบบให้เหมาะกับปลาท้องถิ่น ช่วยการย้ายถิ่นและสนับสนุนชุมชนประมง เพิ่ม Well-being +2 รัศมี 4 กม. ที่ประสิทธิภาพ 100% ผลลดลงตามระยะห่างและปรับตามประสิทธิภาพของโรงไฟฟ้า ติดตั้งได้หนึ่งชุดต่อโรง ค่าติดตั้ง ₡25,000 ค่าบำรุง ₡500/เดือน เป็นอัปเกรดภายในอาคาร"
                            : "A passage designed for local fish migration, supporting river ecology and fishing communities. Well-being +2, 4 km radius at 100% efficiency, with native distance falloff and operating-efficiency scaling. One per powerhouse. Installation: ₡25,000; upkeep: ₡500/month. Internal building upgrade.")
                        : thai
                        ? "เพิ่มกังหัน 34 MW ทุกความยาวครบ 75 ม. เช่น 150 ม.=68 MW, 300 ม.=136 MW (ระยะจัดวางสมมติสำหรับม็อด) ต้องมีส่วนต่างระดับน้ำอย่างน้อย 3 ม. ผลิตจริงขึ้นกับน้ำไหล สมมติประสิทธิภาพ 90% ราคาก่อสร้างและบำรุงคูณด้วยพิกัดม็อด/พิกัดเขื่อนเดิมที่ขนาดเดียวกัน ไม่เกินราคาเดิม ช่วงสั้นกว่า 75 ม. ไม่ผลิตไฟและยังเสียค่างานโยธาเดิม ขุดฐานลึกไม่เพิ่มจำนวนเครื่อง พรีวิวแสดงประมาณการผลิต/พิกัดติดตั้ง น้ำเปลี่ยนหลังสร้างได้"
                        : "One 34 MW turbine per completed 75 m (mod layout assumption): 150 m = 68 MW; 300 m = 136 MW. Requires 3 m wet head; output follows flow, assumed hydraulic efficiency 90%. Construction/upkeep multiply stock prices by installed/stock capacity for the same geometry, capped at 1. Spans below 75 m have zero turbines and retain stock civil costs. Foundation depth does not add turbines. Placement shows estimated output / installed rating; output can change as water settles.";
                    // The UI uses PrefabID.ToString (type:name), not the object's name alone.
                    var tag = prefab.GetPrefabID().ToString();
                    entries["Assets.NAME[" + tag + "]"] = title;
                    entries["Assets.DESCRIPTION[" + tag + "]"] = description;
                    entries["Assets.NAME[" + prefab.name + "]"] = title;
                    entries["Assets.DESCRIPTION[" + prefab.name + "]"] = description;
                    // PrefabUISystem.GetTitleAndDescription takes a separate branch for
                    // ServiceUpgrade: UPGRADE_NAME / UPGRADE_DESCRIPTION with plain name.
                    // Normal asset keys are still needed by selected-object panels.
                    if (prefab.Has<ServiceUpgrade>())
                    {
                        entries["Assets.UPGRADE_NAME[" + prefab.name + "]"] = title;
                        entries["Assets.UPGRADE_DESCRIPTION[" + prefab.name + "]"] = description;
                        entries["Assets.UPGRADE_NAME[" + tag + "]"] = title;
                        entries["Assets.UPGRADE_DESCRIPTION[" + tag + "]"] = description;
                    }
                    Log.Info("Localization registered for " + tag);
                }
                var source = new LocaleSource(entries);
                current.localization.AddSource(locale, source);
                current.sources.Add(new KeyValuePair<string, IDictionarySource>(locale, source));
            }
        }
        private static void RegisterSpillwayStatusText()
        {
            // Bounded pretranslated values avoid rebuilding the entire language dictionary
            // on every water reading. UI precision is 0.1m; controller uses unrounded metres.
            foreach (var locale in current.localization.GetSupportedLocales())
            {
                var entries = new Dictionary<string, string>();
                var thai = locale.StartsWith("th", StringComparison.OrdinalIgnoreCase);
                foreach (SpillwayStatus status in Enum.GetValues(typeof(SpillwayStatus)))
                    for (var bucket = 0; bucket <= 253; bucket++)
                        entries[SpillwayControl.StatusKey(status, bucket)] = SpillwayControl.StatusText(thai, status, bucket);
                var source = new LocaleSource(entries);
                current.localization.AddSource(locale, source);
                current.sources.Add(new KeyValuePair<string, IDictionarySource>(locale, source));
            }
        }
        public void OnDispose()
        {
            DamSceneVisuals.Reset();
            if (sources != null && localization != null)
                foreach (var source in sources)
                    localization.RemoveSource(source.Key, source.Value);
            sources?.Clear();
            if (ReferenceEquals(current, this)) current = null;
            Log.Info("Mun River Power disposed. Restart the game when changing local mod files.");
        }
    }

    internal sealed class LocaleSource : IDictionarySource
    {
        private readonly Dictionary<string, string> entries;
        public LocaleSource(Dictionary<string, string> entries) { this.entries = entries; }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) => entries;
        public void Unload() { }
    }

    public partial class WeirPrefabSystem : GameSystemBase
    {
        private PrefabSystem prefabs;
        private bool registered;
        private EntityQuery prefabQuery;
        private bool failed;
        private int lastPrefabCount;
        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            prefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
        }
        protected override void OnUpdate()
        {
            if (registered || failed) return;
            // Register before prefab serialization/loading; never change the stock dam.
            var count = prefabQuery.CalculateEntityCount();
            if (count == lastPrefabCount) return;
            lastPrefabCount = count;
            var candidates = new List<PrefabBase>();
            using (var entities = prefabQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    if (prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) && WeirFactory.IsHydroMenuPrefab(prefab))
                        candidates.Add(prefab);
            if (candidates.Count == 0) return;
            try
            {
                // Prefer the shipped Hydroelectric asset over similarly named third-party assets.
                var source = candidates.OrderByDescending(WeirFactory.SourceScore).ThenBy(p => p.name, StringComparer.Ordinal).First();
                if (prefabs.TryGetPrefab(new PrefabID(source.GetType().Name, WeirFactory.RootName), out var existing))
                {
                    registered = true;
                    return;
                }
                new WeirFactory(prefabs).Register(source);
                registered = true;
                Mod.Log.Info("REGISTERED MunRiverPower.LowHeadWeir. Look in Electricity next to the original hydroelectric dam.");
            }
            catch (Exception exception)
            {
                failed = true;
                Mod.Log.Error(exception, "Weir registration failed; stock assets were not modified.");
            }
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (!registered && !failed)
            {
                // Reinspect after loading in case the native dependencies arrived after their entities.
                lastPrefabCount = -1;
                // Do not create entities here: this callback is outside prefab initialization.
                Mod.Log.Warn("Hydro registration pending; will retry inside the next prefab initialization phase.");
            }
        }
    }
}
