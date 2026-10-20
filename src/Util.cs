using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Miscellaneous helpers and marker components.
    /// Three empty MonoBehaviours act as "already processed" markers. Using components
    /// instead of a HashSet ties the marker to the object's lifetime: once a vehicle is
    /// destroyed and rebuilt the marker disappears with it, so nothing is misclassified.
    /// </summary>
    public static class Util
    {
        /// <summary>Marker: this vehicle / this armor plate has already been fitted with ERA, do not process it again.</summary>
        public class AlreadyConvertedNERA : MonoBehaviour { }

        /// <summary>Marker: this vehicle carries ARAT (M1/M1IP).</summary>
        public class HasARAT : MonoBehaviour { }

        /// <summary>Marker: this vehicle carries BRAT (M2 Bradley).</summary>
        public class HasBRAT : MonoBehaviour { }

        /// <summary>Writes the path information to the log to make hierarchy problems easier to diagnose.</summary>
        public static string PathOf(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 64)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }
    }
}
