using System.Collections.Generic;
using System.Linq;

namespace MunRiverPower
{
    // Tracks a tool session without treating pre-existing buildings as newly placed ones.
    internal sealed class PlacementLedger<T>
    {
        private readonly HashSet<T> seen = new HashSet<T>();
        private readonly HashSet<T> pending = new HashSet<T>();
        internal bool Active { get; private set; }
        internal T[] Pending => pending.ToArray();
        internal void Begin(IEnumerable<T> existing)
        {
            seen.Clear();
            foreach (var item in existing) seen.Add(item);
            Active = true;
        }
        internal void Observe(IEnumerable<T> current)
        {
            if (!Active) return;
            foreach (var item in current)
                if (seen.Add(item)) pending.Add(item);
        }
        internal void End(IEnumerable<T> current) { Observe(current); Active = false; }
        internal void Complete(T item) { pending.Remove(item); }
        internal void Reset() { Active = false; seen.Clear(); pending.Clear(); }
    }
}
