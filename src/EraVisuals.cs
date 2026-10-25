using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Per-brick visual state, carried on the brick's hit-volume container and read by
    /// <see cref="EraDetonateVisuals"/> when the brick goes off.
    ///
    /// The physics brick and the rendered mesh live on different objects: the brick must sit on
    /// a layer that projectiles can hit but cameras do not draw, and the mesh has to sit on a
    /// layer cameras do draw. So "hide the brick" and "hide the mesh" are two different
    /// objects, and something has to remember the pairing - that is this component.
    ///
    /// The same layout the Fulda 1989 expansion uses, so both mods behave the same way when a
    /// brick is hit: the mesh is swapped for a burnt-out material and a burst is played, instead
    /// of the brick silently vanishing.
    /// </summary>
    public class EraVisuals : MonoBehaviour
    {
        /// <summary>The renderer that draws this brick. Disabled when the brick detonates.</summary>
        public MeshRenderer Visual;

        /// <summary>
        /// Optional material swapped in when the brick detonates, so it looks burned instead of
        /// disappearing. When null the mesh is simply hidden.
        /// </summary>
        public Material DestroyedMaterial;

        /// <summary>
        /// True: hide the brick once it goes off. False: keep drawing it with
        /// <see cref="DestroyedMaterial"/> so the wreck stays visible.
        /// </summary>
        public bool HideOnDetonate = true;

        /// <summary>
        /// Only the material whose name contains this string gets replaced by
        /// <see cref="DestroyedMaterial"/>. Empty replaces the first slot.
        /// </summary>
        public string DestroyedTarget = "";
    }
}
