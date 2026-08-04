using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CM_Semi_Random_Research
{
    // =========================================================================
    // DEF OF
    // =========================================================================
    [DefOf]
    public static class SemiRandomResearchDefOf
    {
        public static MainButtonDef Semi_Random_Research;
    }

    // =========================================================================
    // UTILITIES
    // =========================================================================
    public static class SemiRandomResearchUtility
    {
        // This little gumdrop is to make my life easy with a transpiler patch for hiding the normal research button
        public static bool CanSelectNormalResearchNow(ResearchProjectDef rpd)
        {
            bool anomaly_enabled = Compatibility.IsAnomalyContent(rpd) && !SemiRandomResearchMod.settings.experimentalAnomalySupport;
            bool enabled = !SemiRandomResearchMod.settings.featureEnabled || anomaly_enabled;
            return enabled && rpd.CanStartNow;
        }

        public static bool IsCurrentProject(ResearchProjectDef rpd)
        {
            bool anomaly_enabled = Compatibility.IsAnomalyContent(rpd) && !SemiRandomResearchMod.settings.experimentalAnomalySupport;
            bool enabled = !SemiRandomResearchMod.settings.featureEnabled || anomaly_enabled;
            return enabled && Find.ResearchManager.IsCurrentProject(rpd);
        }
    }

    // =========================================================================
    // COMPATIBILITY
    // =========================================================================
    [StaticConstructorOnStartup]
    static class Compatibility
    {
        public static bool enabled_AlienRaces = ModsConfig.ActiveModsInLoadOrder.Any((ModMetaData m) => m.PackageIdPlayerFacing == "erdelf.HumanoidAlienRaces");
        public static bool enabled_SoS2 = ModsConfig.ActiveModsInLoadOrder.Any((ModMetaData m) => m.PackageIdPlayerFacing == "kentington.saveourship2");
        public static bool enabled_CE = ModsConfig.ActiveModsInLoadOrder.Any((ModMetaData m) => m.PackageIdPlayerFacing == "CETeam.CombatExtended");
        public static bool enabled_BRT = ModsConfig.ActiveModsInLoadOrder.Any((ModMetaData m) => m.PackageIdPlayerFacing == "andery233xj.mod.BetterResearchTabs");

        static Compatibility()
        {
            var harmony = new Harmony("CM_Semi_Random_Research");
        }

        public static bool DoCompatibilityChecks(ResearchProjectDef rpd)
        {
            return SatisfiesAlienRaceRestriction(rpd) &&
                !rpd.IsDummyResearch() &&
                (SemiRandomResearchMod.settings.experimentalAnomalySupport || !IsAnomalyContent(rpd));
        }

        public static bool IsAnomalyContent(ResearchProjectDef rpd)
        {
            if (rpd == null)
            {
                return false;
            }
            return rpd.knowledgeCategory == KnowledgeCategoryDefOf.Basic || rpd.knowledgeCategory == KnowledgeCategoryDefOf.Advanced;
        }

        public static bool IsHiddenResearch(ResearchProjectDef rpd)
        {
            if (rpd == null)
            {
                return false;
            }

            if (rpd.IsHidden)
            {
                return true;
            }

            if (enabled_SoS2 && rpd.tab.defName == "ResearchTabArchotech")
            {
                return !SaveOurShip2ArchotechUplinkUnlocked(rpd);
            }

            return false;
        }

        public static bool SatisfiesAlienRaceRestriction(ResearchProjectDef rpd)
        {
            if (rpd != null && enabled_AlienRaces)
            {
                return true;
            }
            else
            {
                return true;
            }
        }

        public static bool IsDummyResearch(this ResearchProjectDef rpd)
        {
            if (rpd == null)
            {
                return false;
            }
            if (enabled_CE && rpd.defName == "VFES_Artillery_Debug")
            {
                return true;
            }
            if (rpd.Cost == 0)
            {
                return true;
            }
            if (rpd.prerequisites != null && rpd.prerequisites.Contains(rpd))
            {
                return true;
            }

            return false;
        }

        private static bool SaveOurShip2ArchotechUplinkUnlocked(ResearchProjectDef rpd)
        {
            try
            {
                // Use Harmony reflection so we don't need a hard compile-time reference to the SOS2 DLL
                Type modType = AccessTools.TypeByName("SaveOurShip2.ShipInteriorMod2");
                if (modType != null)
                {
                    object worldComp = AccessTools.Field(modType, "WorldComp")?.GetValue(null);
                    if (worldComp != null)
                    {
                        object unlocks = AccessTools.Field(worldComp.GetType(), "Unlocks")?.GetValue(worldComp);

                        // Check if it's a HashSet or List and contains our string
                        if (unlocks is HashSet<string> hashSet) return hashSet.Contains("ArchotechUplink");
                        if (unlocks is List<string> list) return list.Contains("ArchotechUplink");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[CM_Semi_Random_Research] Error checking SOS2 compatibility: " + ex);
            }

            return false;
        }
    }

    // =========================================================================
    // MANIFEST VERSION READER
    // =========================================================================

    public class VersionFromManifest
    {
        private const string ManifestFileName = "Manifest.xml";
        public string version;

        private static string AboutDir(ModMetaData mod)
        {
            return Path.Combine(mod.RootDir.FullName, "About");
        }

        public static string GetVersionFromModMetaData(ModMetaData modMetaData)
        {
            var manifestPath = Path.Combine(AboutDir(modMetaData), ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                var manifest = DirectXmlLoader.ItemFromXmlFile<VersionFromManifest>(manifestPath, false);
                return manifest.version;
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"Error loading manifest for '{modMetaData.Name}':\n{e.Message}\n\n{e.StackTrace}",
                    modMetaData.Name.GetHashCode());
            }

            return null;
        }
    }
}