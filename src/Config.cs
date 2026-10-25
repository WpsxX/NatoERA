using System;
using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Central management of all user-tunable parameters.
    ///
    /// [Design principle: one-to-one compatible with the original author's v1.2.4 MelonPreferences.cfg]
    /// Entry names, default values, ordering and category assignment are all copied from the original:
    ///   - Category name "NATOERAConfig"
    ///   - Entry names identical to the original, character for character (including spaces, brackets and casing)
    ///   - Description text identical to the original (MelonLoader writes it into the cfg as an inline comment)
    ///
    /// That way, when an existing user upgrades, the [NATOERAConfig] section already present in their
    /// MelonPreferences.cfg can be kept as is, their values take effect directly, and they do not have to configure it again.
    ///
    /// Original entry list (33 of the 34; "IP model to M1" was removed on request):
    ///   ARAT.cs       Improve CE Protection (600mm) /
    ///                 Improve KE Protection (150mm) / ARAT R / ARAT G / ARAT B
    ///   M1Abrams.cs   M1 ERA and its 7 sub-arrays / M1IP ERA and its 7 sub-arrays / Feather ARAT
    ///   BRAT.cs       Improve BRAT-M3 CE/KE / Improve BRAT-M5 CE/KE / BRAT R / BRAT G / BRAT B
    ///   M2Bradley.cs  M2 ERA and its 5 sub-arrays / Feather BRAT
    ///
    /// Entries added by this mod on top of the original (M2Extended compatibility, detonation thresholds, etc.)
    /// live in the "Extended options" section; every one of those entry names carries a NatoEra prefix or an
    /// explicit marker, so they never conflict with the original entries.
    /// </summary>
    public static class Config
    {
        /// <summary>Same category name as the original author. Renaming it would silently invalidate existing users' configs.</summary>
        public const string CategoryName = "NATOERAConfig";

        private static MelonPreferences_Category _cat;

        // =====================================================================
        // 1. ARAT global (originally ARAT.cs)
        // =====================================================================

        /// <summary>Original "Improve CE Protection (600mm)": ARAT chemical protection 450 -> 600.</summary>
        public static bool ImproveAratCE = false;

        /// <summary>Original "Improve KE Protection (150mm)": ARAT kinetic protection 30 -> 150.</summary>
        public static bool ImproveAratKE = false;

        public static float AratR = 71f;
        public static float AratG = 80f;
        public static float AratB = 65f;

        // =====================================================================
        // 2. M1 / M1IP sub-array toggles (originally M1Abrams.cs)
        // =====================================================================

        // ---- M1 ----
        public static bool ShowEraM1 = true;
        public static bool ShowEraM1_HullLowerFront = false;
        public static bool ShowEraM1_HullUpperFront = false;
        public static bool ShowEraM1_HullExtended = false;
        public static bool ShowEraM1_Turret = false;
        public static bool ShowEraM1_TurretRoof = false;
        public static bool ShowEraM1_TurretCheek = false;
        public static bool ShowEraM1_TurretExtended = false;

        // ---- M1IP ----
        public static bool ShowEraM1Ip = true;
        public static bool ShowEraM1Ip_HullLowerFront = false;
        public static bool ShowEraM1Ip_HullUpperFront = false;
        public static bool ShowEraM1Ip_HullExtended = false;
        public static bool ShowEraM1Ip_Turret = false;
        public static bool ShowEraM1Ip_TurretRoof = false;
        public static bool ShowEraM1Ip_TurretCheek = false;
        public static bool ShowEraM1Ip_TurretExtended = false;

        /// <summary>Original "Feather ARAT": removes all of the ARAT weight penalty.</summary>
        public static bool FeatherArat = true;

        // =====================================================================
        // 3. BRAT global (originally BRAT.cs)
        // =====================================================================

        public static bool ImproveBratM3CE = false;
        public static bool ImproveBratM3KE = false;
        public static bool ImproveBratM5CE = false;
        public static bool ImproveBratM5KE = false;

        public static float BratR = 71f;
        public static float BratG = 80f;
        public static float BratB = 65f;

        // =====================================================================
        // 4. M2 sub-array toggles (originally M2Bradley.cs)
        // =====================================================================

        public static bool ShowEraM2 = true;
        public static bool ShowEraM2_HullSideExtended = false;
        public static bool ShowEraM2_HullSideLower = true;
        public static bool ShowEraM2_HullSideLowerExtended = false;
        public static bool ShowEraM2_HullLowerFront = false;
        public static bool ShowEraM2_HullUpper = false;

        /// <summary>Original "Feather BRAT": removes all of the BRAT weight penalty.</summary>
        public static bool FeatherBrat = true;

        // =====================================================================
        // 5. Protection values (derived from the 6 Improve toggles above, not exposed separately)
        // =====================================================================

        /// <summary>ARAT chemical (CE) protection (mm RHA).</summary>
        public static float AratHeatRha { get { return ImproveAratCE ? 600f : 450f; } }

        /// <summary>ARAT kinetic (KE) protection (mm RHA).</summary>
        public static float AratSabotRha { get { return ImproveAratKE ? 150f : 30f; } }

        public static float BratM3HeatRha { get { return ImproveBratM3CE ? 600f : 450f; } }
        public static float BratM3SabotRha { get { return ImproveBratM3KE ? 150f : 30f; } }
        public static float BratM5HeatRha { get { return ImproveBratM5CE ? 800f : 600f; } }
        public static float BratM5SabotRha { get { return ImproveBratM5KE ? 200f : 45f; } }

        /// <summary>ARAT color (RGB 0-255 -> Unity Color, matching the legacy `arat_R/255f`).</summary>
        public static Color AratColor
        {
            get { return new Color(AratR / 255f, AratG / 255f, AratB / 255f); }
        }

        public static Color BratColor
        {
            get { return new Color(BratR / 255f, BratG / 255f, BratB / 255f); }
        }

        // =====================================================================
        // 5b. Mod extensions (not present in the original mod; failures below the original 34 entries)
        // =====================================================================

        /// <summary>Shift BRAT outwards so it sits on top of the M2Extended addon armor. false = fit exactly like the original mod.</summary>
        public static bool EnableM2ExtendedOverlay = true;

        /// <summary>
        /// A round at or above this calibre sets a brick off even when it failed to penetrate it,
        /// so a heavy shell that bounces still cooks off the ERA. Below it the round has to carry
        /// enough penetration instead (see <see cref="PenThreshold"/>).
        /// </summary>
        public static float CaliberThreshold = InsensitiveEraPatch.DefaultCaliberThreshold;

        /// <summary>
        /// A round below <see cref="CaliberThreshold"/> still sets a brick off when its penetration
        /// rating exceeds this value.
        /// </summary>
        public static float PenThreshold = InsensitiveEraPatch.DefaultPenThreshold;

        // =====================================================================
        // 6. Mass table (hard-coded inside each Convert method in the original; kept verbatim here)
        // =====================================================================
        // Unit: kg. When the corresponding Feather toggle is true everything is treated as 0.

        public static int MassM1IpHull               { get { return FeatherArat ? 0 : 1887; } }
        public static int MassM1IpHullExtended       { get { return FeatherArat ? 0 : 769; } }
        public static int MassM1IpHullLowerFront     { get { return FeatherArat ? 0 : 295; } }
        public static int MassM1IpHullUpperFront     { get { return FeatherArat ? 0 : 1150; } }
        public static int MassM1IpTurret             { get { return FeatherArat ? 0 : 737; } }
        public static int MassM1IpTurretExtended     { get { return FeatherArat ? 0 : 413; } }
        public static int MassM1IpTurretRoof         { get { return FeatherArat ? 0 : 501; } }
        public static int MassM1IpTurretCheek        { get { return FeatherArat ? 0 : 383; } }

        public static int MassM1Hull                 { get { return FeatherArat ? 0 : 1887; } }
        public static int MassM1HullExtended         { get { return FeatherArat ? 0 : 769; } }
        public static int MassM1HullLowerFront       { get { return FeatherArat ? 0 : 295; } }
        public static int MassM1HullUpperFront       { get { return FeatherArat ? 0 : 1150; } }
        public static int MassM1Turret               { get { return FeatherArat ? 0 : 678; } }
        public static int MassM1TurretExtended       { get { return FeatherArat ? 0 : 413; } }
        public static int MassM1TurretRoof           { get { return FeatherArat ? 0 : 501; } }
        public static int MassM1TurretCheek          { get { return FeatherArat ? 0 : 383; } }

        public static int MassM2Hull                 { get { return FeatherBrat ? 0 : 2211; } }
        public static int MassM2HullSideLower        { get { return FeatherBrat ? 0 : 825; } }
        public static int MassM2HullSideLowerExt     { get { return FeatherBrat ? 0 : 118; } }
        public static int MassM2HullSideExtended     { get { return FeatherBrat ? 0 : 531; } }
        public static int MassM2FrontLower           { get { return FeatherBrat ? 0 : 251; } }
        public static int MassM2HullUpper            { get { return FeatherBrat ? 0 : 671; } }

        // ---- How the original adds mass at M1Abrams.cs:181-217 / 300-340 ----
        // Note: the original adds the base hull mass unconditionally, while every other item is gated behind its own toggle.
        // The logic is carried over as is to avoid any mass difference against the legacy version.

        /// <summary>Total mass added by the original M1IP branch.</summary>
        public static float TotalMassM1Ip
        {
            get
            {
                float kg = MassM1IpHull;
                if (ShowEraM1Ip_HullExtended)   kg += MassM1IpHullExtended;
                if (ShowEraM1Ip_HullLowerFront) kg += MassM1IpHullLowerFront;
                if (ShowEraM1Ip_HullUpperFront) kg += MassM1IpHullUpperFront;
                if (ShowEraM1Ip_Turret)         kg += MassM1IpTurret;
                if (ShowEraM1Ip_TurretExtended) kg += MassM1IpTurretExtended;
                if (ShowEraM1Ip_TurretRoof)     kg += MassM1IpTurretRoof;
                if (ShowEraM1Ip_TurretCheek)    kg += MassM1IpTurretCheek;
                return kg;
            }
        }

        /// <summary>Total mass added by the original M1 branch.</summary>
        public static float TotalMassM1
        {
            get
            {
                float kg = MassM1Hull;
                if (ShowEraM1_HullExtended)   kg += MassM1HullExtended;
                if (ShowEraM1_HullLowerFront) kg += MassM1HullLowerFront;
                if (ShowEraM1_HullUpperFront) kg += MassM1HullUpperFront;
                if (ShowEraM1_Turret)         kg += MassM1Turret;
                if (ShowEraM1_TurretExtended) kg += MassM1TurretExtended;
                if (ShowEraM1_TurretRoof)     kg += MassM1TurretRoof;
                if (ShowEraM1_TurretCheek)    kg += MassM1TurretCheek;
                return kg;
            }
        }

        /// <summary>Total mass added by the original M2Bradley branch.</summary>
        public static float TotalMassM2
        {
            get
            {
                float kg = MassM2Hull;
                if (ShowEraM2_HullSideExtended)    kg += MassM2HullSideExtended;
                if (ShowEraM2_HullSideLower)       kg += MassM2HullSideLower;
                if (ShowEraM2_HullSideLowerExtended) kg += MassM2HullSideLowerExt;
                if (ShowEraM2_HullLowerFront)      kg += MassM2FrontLower;
                if (ShowEraM2_HullUpper)           kg += MassM2HullUpper;
                return kg;
            }
        }

        // =====================================================================
        // 7. Fixed internal constants (not preferences)
        // =====================================================================

        /// <summary>How far BRAT is pushed outwards when it has to sit outside the M2Extended addon armor (metres). Not a preference.</summary>
        public const float M2ExtendedOverlayGap = 0f;

        // =====================================================================
        // Register preferences
        // =====================================================================

        public static void Init()
        {
            _cat = MelonPreferences.CreateCategory(CategoryName, "NATO ERA");

            // ---------------------------------------------------------------
            // 1. ARAT global (order matches the legacy M1Abrams.Config -> ARAT.Config)
            //
            // Note: the registration order in the legacy Main.cs is
            //   M1Abrams.Config(cfg) -> ARAT.Config(cfg) -> M2Bradley.Config(cfg) -> BRAT.Config(cfg)
            // It is reproduced exactly here so the generated cfg sections appear in the same order as in existing users' files.
            // ---------------------------------------------------------------

            // ---- M1Abrams.Config ----
            var eM1 = _cat.CreateEntry<bool>("M1 ERA", ShowEraM1);
            eM1.Description = "Enable ERA for M1";

            var eM1Hlf = _cat.CreateEntry<bool>("M1 Hull Lower Front ERA", ShowEraM1_HullLowerFront);
            eM1Hlf.Description = "M1 Hull ERA Arrangement";
            var eM1Huf = _cat.CreateEntry<bool>("M1 Hull Upper Front ERA", ShowEraM1_HullUpperFront);
            var eM1Hext = _cat.CreateEntry<bool>("M1 Extended Hull ERA", ShowEraM1_HullExtended);

            var eM1T = _cat.CreateEntry<bool>("M1 Turret ERA", ShowEraM1_Turret);
            eM1T.Description = "M1 Turret ERA Arrangement";
            var eM1Troof = _cat.CreateEntry<bool>("M1 Turret Roof ERA", ShowEraM1_TurretRoof);
            var eM1Tcheek = _cat.CreateEntry<bool>("M1 Turret Cheek ERA", ShowEraM1_TurretCheek);
            var eM1Text = _cat.CreateEntry<bool>("M1 Extended Turret ERA", ShowEraM1_TurretExtended);

            var eIp = _cat.CreateEntry<bool>("M1IP ERA", ShowEraM1Ip);
            eIp.Description = "Enable ERA for M1IP";

            var eIpHlf = _cat.CreateEntry<bool>("M1IP Hull Lower Front ERA", ShowEraM1Ip_HullLowerFront);
            eIpHlf.Description = "M1IP Hull ERA Arrangement";
            var eIpHuf = _cat.CreateEntry<bool>("M1IP Hull Upper Front ERA", ShowEraM1Ip_HullUpperFront);
            var eIpHext = _cat.CreateEntry<bool>("M1IP Extended Hull ERA", ShowEraM1Ip_HullExtended);

            var eIpT = _cat.CreateEntry<bool>("M1IP Turret ERA", ShowEraM1Ip_Turret);
            eIpT.Description = "M1IP Turret ERA Arrangement";
            var eIpTroof = _cat.CreateEntry<bool>("M1IP Turret Roof ERA", ShowEraM1Ip_TurretRoof);
            var eIpTcheek = _cat.CreateEntry<bool>("M1IP Turret Cheek ERA", ShowEraM1Ip_TurretCheek);
            var eIpText = _cat.CreateEntry<bool>("M1IP Extended Turret ERA", ShowEraM1Ip_TurretExtended);

            var eFeatherArat = _cat.CreateEntry<bool>("Feather ARAT", FeatherArat);
            eFeatherArat.Description = "Removes ARAT weight penalty";

            // ---- ARAT.Config ----
            var eAratCe = _cat.CreateEntry<bool>("Improve CE Protection (600mm)", ImproveAratCE);
            eAratCe.Description = "Improve ARAT Protection";
            var eAratKe = _cat.CreateEntry<bool>("Improve KE Protection (150mm)", ImproveAratKE);

            var eAratR = _cat.CreateEntry<float>("ARAT R", AratR);
            eAratR.Description = "Adjust ARAT colors with RGB values (float)";
            var eAratG = _cat.CreateEntry<float>("ARAT G", AratG);
            var eAratB = _cat.CreateEntry<float>("ARAT B", AratB);

            // ---- M2Bradley.Config ----
            var eM2 = _cat.CreateEntry<bool>("M2 ERA", ShowEraM2);
            eM2.Description = "Enable ERA for M2";

            var eM2Hse = _cat.CreateEntry<bool>("M2 Extended Hull Side ERA", ShowEraM2_HullSideExtended);
            eM2Hse.Description = "M2 Hull ERA Arrangement";
            var eM2Hsl = _cat.CreateEntry<bool>("M2 Hull Side Lower ERA", ShowEraM2_HullSideLower);
            var eM2Hsle = _cat.CreateEntry<bool>("M2 Extended Hull Side Lower ERA", ShowEraM2_HullSideLowerExtended);
            var eM2Hlf = _cat.CreateEntry<bool>("M2 Hull Lower Front ERA", ShowEraM2_HullLowerFront);
            var eM2Hu = _cat.CreateEntry<bool>("M2 Hull Upper ERA", ShowEraM2_HullUpper);

            var eFeatherBrat = _cat.CreateEntry<bool>("Feather BRAT", FeatherBrat);
            eFeatherBrat.Description = "Removes BRAT weight penalty";

            // ---- BRAT.Config ----
            var eB3Ce = _cat.CreateEntry<bool>("Improve BRAT-M3 CE Protection (600mm)", ImproveBratM3CE);
            eB3Ce.Description = "Improve BRAT Protection";
            var eB3Ke = _cat.CreateEntry<bool>("Improve BRAT-M3 KE Protection (150mm)", ImproveBratM3KE);
            var eB5Ce = _cat.CreateEntry<bool>("Improve BRAT-M5 CE Protection (800mm)", ImproveBratM5CE);
            var eB5Ke = _cat.CreateEntry<bool>("Improve BRAT-M5 KE Protection (200mm)", ImproveBratM5KE);

            var eBratR = _cat.CreateEntry<float>("BRAT R", BratR);
            eBratR.Description = "Adjust BRAT colors with RGB values (float)";
            var eBratG = _cat.CreateEntry<float>("BRAT G", BratG);
            var eBratB = _cat.CreateEntry<float>("BRAT B", BratB);

            // ---------------------------------------------------------------
            // Mod extensions. These come after the 34 original entries, so they never shift the order of
            // the original section and an existing cfg keeps working untouched (defaults = original behaviour).
            // ---------------------------------------------------------------

            var eM2Extended = _cat.CreateEntry<bool>("NatoEra M2Extended Overlay", EnableM2ExtendedOverlay);
            eM2Extended.Description = "Set to true to shift BRAT outwards when the M2Extended addon armor is fitted, so it sits outside it";

            var eCaliber = _cat.CreateEntry<float>("NatoEra Caliber Threshold (mm)", CaliberThreshold);
            eCaliber.Description = "Rounds at or above this calibre set a brick off even when they fail to penetrate it";

            var ePenetration = _cat.CreateEntry<float>("NatoEra Penetration Threshold (mm)", PenThreshold);
            ePenetration.Description = "Rounds below the calibre threshold still set a brick off when their penetration exceeds this";

            // ---------------------------------------------------------------
            // 2. Push the values back into the static fields (the user may have edited the cfg)
            // ---------------------------------------------------------------

            ShowEraM1 = eM1.Value;
            ShowEraM1_HullLowerFront = eM1Hlf.Value;
            ShowEraM1_HullUpperFront = eM1Huf.Value;
            ShowEraM1_HullExtended = eM1Hext.Value;
            ShowEraM1_Turret = eM1T.Value;
            ShowEraM1_TurretRoof = eM1Troof.Value;
            ShowEraM1_TurretCheek = eM1Tcheek.Value;
            ShowEraM1_TurretExtended = eM1Text.Value;

            ShowEraM1Ip = eIp.Value;
            ShowEraM1Ip_HullLowerFront = eIpHlf.Value;
            ShowEraM1Ip_HullUpperFront = eIpHuf.Value;
            ShowEraM1Ip_HullExtended = eIpHext.Value;
            ShowEraM1Ip_Turret = eIpT.Value;
            ShowEraM1Ip_TurretRoof = eIpTroof.Value;
            ShowEraM1Ip_TurretCheek = eIpTcheek.Value;
            ShowEraM1Ip_TurretExtended = eIpText.Value;

            FeatherArat = eFeatherArat.Value;

            ImproveAratCE = eAratCe.Value;
            ImproveAratKE = eAratKe.Value;
            AratR = eAratR.Value;
            AratG = eAratG.Value;
            AratB = eAratB.Value;

            ShowEraM2 = eM2.Value;
            ShowEraM2_HullSideExtended = eM2Hse.Value;
            ShowEraM2_HullSideLower = eM2Hsl.Value;
            ShowEraM2_HullSideLowerExtended = eM2Hsle.Value;
            ShowEraM2_HullLowerFront = eM2Hlf.Value;
            ShowEraM2_HullUpper = eM2Hu.Value;

            FeatherBrat = eFeatherBrat.Value;

            ImproveBratM3CE = eB3Ce.Value;
            ImproveBratM3KE = eB3Ke.Value;
            ImproveBratM5CE = eB5Ce.Value;
            ImproveBratM5KE = eB5Ke.Value;
            BratR = eBratR.Value;
            BratG = eBratG.Value;
            BratB = eBratB.Value;

            // ---- Mod extensions (not present in the original mod) ----
            EnableM2ExtendedOverlay = eM2Extended.Value;

            CaliberThreshold = Mathf.Max(0f, eCaliber.Value);
            PenThreshold = Mathf.Max(0f, ePenetration.Value);

            // Out-of-range RGB produces an invalid color, so clamp it
            AratR = Mathf.Clamp(AratR, 0f, 255f);
            AratG = Mathf.Clamp(AratG, 0f, 255f);
            AratB = Mathf.Clamp(AratB, 0f, 255f);
            BratR = Mathf.Clamp(BratR, 0f, 255f);
            BratG = Mathf.Clamp(BratG, 0f, 255f);
            BratB = Mathf.Clamp(BratB, 0f, 255f);

            MelonLogger.Msg("[NatoERA/Config] Registered 34 original preference entries"
                + " + 3 mod extensions (category " + CategoryName + ").");
            try
            {
                string baseDir = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
                MelonLogger.Msg("[NatoERA/Config] Config file: " + System.IO.Path.Combine(baseDir, CategoryName + ".cfg"));
            }
            catch (Exception)
            {
                // A failed log line does not affect functionality
            }
            MelonLogger.Msg("[NatoERA/Config] ARAT KE=" + AratSabotRha + " CE=" + AratHeatRha
                + " | BRAT-M3 KE=" + BratM3SabotRha + " CE=" + BratM3HeatRha
                + " | BRAT-M5 KE=" + BratM5SabotRha + " CE=" + BratM5HeatRha);
            MelonLogger.Msg("[NatoERA/Config] Show toggles M1=" + ShowEraM1 + " M1IP=" + ShowEraM1Ip
                + " M2=" + ShowEraM2
                + " | Feather ARAT=" + FeatherArat + " BRAT=" + FeatherBrat);
            MelonLogger.Msg("[NatoERA/Config] Detonation thresholds: calibre >= " + CaliberThreshold.ToString("0.#")
                + " mm sets a brick off without penetrating; below that, penetration > " + PenThreshold.ToString("0.#") + " mm.");
        }

        /// <summary>
        /// Single hook where the mod hands its settings to the patching layer.
        ///
        /// The transpiler bakes the thresholds in as literals when it runs, so the values only
        /// have to be pushed once, before the first patch is applied. They are read here rather
        /// than at patch time because Harmony may build the replacement IL lazily.
        /// </summary>
        public static void ApplyToPatches()
        {
            InsensitiveEraPatch.CaliberThreshold = CaliberThreshold;
            InsensitiveEraPatch.PenThreshold = PenThreshold;
        }
    }
}
