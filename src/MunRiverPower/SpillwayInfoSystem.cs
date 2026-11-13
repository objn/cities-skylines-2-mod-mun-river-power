using Colossal.UI.Binding;
using Game;
using Game.UI.InGame;

namespace MunRiverPower
{
    public partial class SpillwayInfoSystem : GameSystemBase
    {
        private SelectedInfoUISystem selected;
        private StatusSectionSource section;
        protected override void OnCreate()
        {
            base.OnCreate();
            selected = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            section = new StatusSectionSource(selected, World.GetOrCreateSystemManaged<SpillwaySimulationSystem>());
            selected.AddMiddleSection(section);
        }
        protected override void OnUpdate()
        {
            if (section.Refresh()) selected.SetDirty();
        }
        private sealed class StatusSectionSource : ISectionSource, IJsonWritable
        {
            private readonly SelectedInfoUISystem selected;
            private readonly SpillwaySimulationSystem spillway;
            private string key;
            internal StatusSectionSource(SelectedInfoUISystem selected, SpillwaySimulationSystem spillway) { this.selected = selected; this.spillway = spillway; }
            internal bool Refresh()
            {
                var next = spillway.TryReport(selected.selectedEntity, out var report)
                    ? SpillwayControl.StatusKey(report.Status, SpillwayControl.DisplayBucket(report.Reading.Valid ? (double?)report.Reading.Freeboard : null)) : null;
                if (next == key) return false;
                key = next; return true;
            }
            public void RequestUpdate() { }
            public void PerformUpdate() => Refresh();
            public void Write(IJsonWriter writer)
            {
                if (key == null) { writer.WriteNull(); return; }
                // Reuse the shipped renderer, not an unknown custom React section type.
                writer.TypeBegin("Game.UI.InGame.DescriptionSection");
                writer.PropertyName("group"); writer.Write("MunRiverPower.Spillway");
                writer.PropertyName("tooltipKeys"); writer.ArrayBegin(0); writer.ArrayEnd();
                writer.PropertyName("tooltipTags"); writer.ArrayBegin(0); writer.ArrayEnd();
                writer.PropertyName("localeId"); writer.Write(key);
                writer.PropertyName("effects"); writer.ArrayBegin(0); writer.ArrayEnd();
                writer.TypeEnd();
            }
        }
    }
}
