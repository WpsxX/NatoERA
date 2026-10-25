using System;
using System.Linq;
using GHPC;
using GHPC.Audio;
using GHPC.Effects;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NatoEra
{
    /// <summary>
    /// Plays the detonation feedback for a brick the moment it goes off: swap the mesh for a
    /// burnt-out material, spawn a HEAT impact burst and play a main-gun audio cue.
    ///
    /// [Why this exists]
    /// Vanilla UniformArmor.Detonate() only does two things visually: instantiate DetonateEffect
    /// and toggle Renderer.enabled on the objects in Undetonated/Detonated. That is enough to
    /// make a brick vanish, but it gives no report - a brick popping off the side of a tank
    /// produces no flash and no sound. This postfix adds both, the same way the Fulda 1989
    /// expansion does, so the two mods look identical when a brick is hit.
    ///
    /// [Where it hooks]
    /// UniformArmor.Detonate() is the only entry point that turns a brick into a spent one, so a
    /// postfix there catches every detonation - the vanilla paths in penCheck as well as any
    /// other caller.
    ///
    /// [What it does not do]
    /// It never touches damage, armour values or the detonation decision. Those live in
    /// <see cref="InsensitiveEraPatch"/> and in EraBuilder; this class is presentation only.
    /// </summary>
    [HarmonyPatch(typeof(UniformArmor), "Detonate")]
    public static class EraDetonateVisuals
    {
        /// <summary>
        /// A throwaway ammo type used only to look up the HEAT impact burst. DetonateEffect and
        /// ImpactEffectDescriptor are what ParticleEffectsManager reads, so the rest stays at
        /// its defaults.
        /// </summary>
        private static AmmoType _dummyHe;

        private static bool _effectWarned;

        private static void Postfix(UniformArmor __instance)
        {
            try
            {
                if (__instance == null) return;

                var visuals = __instance.GetComponent<EraVisuals>();
                if (visuals == null) return;

                // ---- 1. The mesh ----
                var visual = visuals.Visual;
                if (visual != null)
                {
                    if (visuals.HideOnDetonate)
                    {
                        visual.enabled = false;
                    }
                    else
                    {
                        visual.enabled = true;

                        var destroyed = visuals.DestroyedMaterial;
                        if (destroyed != null)
                        {
                            // Replace the slot whose material name matches, or the first one when
                            // no target was given. sharedMaterials is copied first so the
                            // material array shared by other bricks is not modified in place.
                            var materials = visual.sharedMaterials;
                            if (materials != null && materials.Length > 0)
                            {
                                int index = 0;
                                if (!string.IsNullOrEmpty(visuals.DestroyedTarget))
                                {
                                    for (int i = 0; i < materials.Length; i++)
                                    {
                                        if (materials[i] != null && materials[i].name.Contains(visuals.DestroyedTarget))
                                        {
                                            index = i;
                                            break;
                                        }
                                    }
                                }

                                var copy = (Material[])materials.Clone();
                                copy[index] = destroyed;
                                visual.sharedMaterials = copy;
                            }
                        }
                    }
                }

                // ---- 2. The burst and the sound ----
                var manager = ParticleEffectsManager.Instance;
                if (manager != null)
                {
                    var ammo = DummyHe();
                    if (ammo != null)
                    {
                        manager.CreateImpactEffectOfType(
                            ammo,
                            ParticleEffectsManager.FusedStatus.Fuzed,
                            ParticleEffectsManager.SurfaceMaterial.Steel,
                            false,
                            __instance.transform.position,
                            null);
                    }
                }

                var sfx = ImpactSFXManager.Instance;
                if (sfx != null)
                {
                    sfx.PlaySimpleImpactAudio(ImpactAudioType.MainGunHeat, __instance.transform.position, true);
                }

                // ---- 3. Keep the brick's physics out of the way ----
                // The brick has already been flagged as detonated by the time a postfix runs, so
                // penCheck will not detonate it again. Disabling the container also takes its
                // mesh child with it, which is what makes the brick read as "gone" rather than
                // "dark".
                if (visuals.HideOnDetonate && __instance.gameObject != null)
                {
                    __instance.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA] Detonation visuals failed (the brick itself is unaffected): " + e.Message);
            }
        }

        /// <summary>
        /// Builds (once) the throwaway AmmoType that points at the game's HEAT impact burst.
        /// Returns null when the burst cannot be found, in which case only the audio plays.
        /// </summary>
        private static AmmoType DummyHe()
        {
            if (_dummyHe != null) return _dummyHe;

            try
            {
                var effect = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(o => o != null && o.name == "HEAT Impact");

                if (effect == null)
                {
                    if (!_effectWarned)
                    {
                        _effectWarned = true;
                        MelonLogger.Warning("[NatoERA] 'HEAT Impact' prefab not found; ERA bricks pop without a burst.");
                    }
                    return null;
                }

                _dummyHe = new AmmoType();
                _dummyHe.DetonateEffect = effect;
                _dummyHe.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor
                {
                    HasImpactEffect = true,
                    ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
                    EffectSize = ParticleEffectsManager.EffectSize.Autocannon,
                    RicochetType = ParticleEffectsManager.RicochetType.None,
                    Flags = ParticleEffectsManager.ImpactModifierFlags.Medium,
                    MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low,
                };

                return _dummyHe;
            }
            catch (Exception e)
            {
                if (!_effectWarned)
                {
                    _effectWarned = true;
                    MelonLogger.Warning("[NatoERA] Could not build the impact burst description: " + e.Message);
                }
                return null;
            }
        }

        /// <summary>Called on scene change: the AmmoType holds a reference into the old scene.</summary>
        public static void Reset()
        {
            _dummyHe = null;
            _effectWarned = false;
        }
    }
}
