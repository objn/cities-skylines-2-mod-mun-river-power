using System.Collections.Generic;

namespace MunRiverPower
{
    internal static class LayoutCompatibility<T>
    {
        internal static bool CanAdoptNative(IEnumerable<T> nativeDeclared, IEnumerable<T> modDeclared,
            IEnumerable<T> nativeBaked, IEnumerable<T> modBaked)
        {
            var declared = new HashSet<T>(nativeDeclared);
            var baked = new HashSet<T>(nativeBaked);
            // Numerical tuning must not change the authored component contract. Only allow
            // native-generated additions, never silently remove mod-only baked components.
            return declared.SetEquals(modDeclared) && baked.IsSupersetOf(declared) && baked.IsSupersetOf(modBaked);
        }
    }
}
