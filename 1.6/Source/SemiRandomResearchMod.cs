using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CM_Semi_Random_Research
{
    // =========================================================================
    // ENUMS
    // =========================================================================
    public enum ManualReroll
    {
        None,
        Once,
        Always
    }

    public enum ChoiceAmountSelection
    {
        Static,
        PerColonist
    }

    public enum ProgressAddsChoice
    {
        Never,
        ReplaceChoice,
        AddChoice,
        AddChoiceOnlyOnGain
    }

    // =========================================================================
    // CORE MOD CLASS
    // =========================================================================
    public class SemiRandomResearchMod : Mod
    {
        private static SemiRandomResearchMod _instance;
        public static SemiRandomResearchMod Instance => _instance;

        public static SemiRandomResearchModSettings settings;

        public static string version;

        public SemiRandomResearchMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("CM_Semi_Random_Research");
            harmony.PatchAll();

            _instance = this;
            settings = GetSettings<SemiRandomResearchModSettings>();
            string versionFromManifest = VersionFromManifest.GetVersionFromModMetaData(content.ModMetaData);
            if (!versionFromManifest.NullOrEmpty())
            {
                version = versionFromManifest;
            }
            else
            {
                version = "?.?.?";
            }
        }

        public override string SettingsCategory()
        {
            return "Semi Random Research";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            settings.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            settings.UpdateSettings();
        }
    }

    // =========================================================================
    // MOD SETTINGS CLASS
    // =========================================================================
    public class SemiRandomResearchModSettings : ModSettings
    {
        public bool featureEnabled = true;
        public bool rerollAllEveryTime = true;

        public bool forceLowestTechLevel = false;
        public bool restrictToFactionTechLevel = false;
        public bool allowOneHigherTechProject = false;
        public bool allowSwitchingResearch = false;
        public ProgressAddsChoice progressAddsChoice = ProgressAddsChoice.Never;

        public ManualReroll allowManualReroll = ManualReroll.None;
        public ChoiceAmountSelection amountSelection = ChoiceAmountSelection.Static;

        public int availableProjectCount = 3;

        public int additionalProjectPerXColonists = 3;
        public int maxProjectCount = 6;

        public int reofferAfterAmountOfRerolls = 3;

        public bool equalizeCost = false;
        public bool verboseLogging = false;

        public bool experimentalAnomalySupport = true;

        private bool loggedSettings = false;


        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref featureEnabled, "featureEnabled", true);
            Scribe_Values.Look(ref rerollAllEveryTime, "rerollAllEveryTime", true);
            Scribe_Values.Look(ref allowManualReroll, "allowManualReroll", ManualReroll.None);
            Scribe_Values.Look(ref amountSelection, "amountSelection", ChoiceAmountSelection.Static);
            Scribe_Values.Look(ref reofferAfterAmountOfRerolls, "reofferAfterAmountOfRerolls", 3);
            Scribe_Values.Look(ref availableProjectCount, "availableProjectCount", 3);
            Scribe_Values.Look(ref additionalProjectPerXColonists, "additionalProjectPerXColonists", 3);
            Scribe_Values.Look(ref maxProjectCount, "maxProjectCount", 3 + availableProjectCount);
            Scribe_Values.Look(ref progressAddsChoice, "progressAddsChoice", ProgressAddsChoice.AddChoiceOnlyOnGain);
            Scribe_Values.Look(ref forceLowestTechLevel, "forceLowestTechLevel", false);
            Scribe_Values.Look(ref restrictToFactionTechLevel, "restrictToFactionTechLevel", false);
            Scribe_Values.Look(ref allowOneHigherTechProject, "allowOneHigherTechProject", false);
            Scribe_Values.Look(ref allowSwitchingResearch, "allowSwitchingResearch", false);
            Scribe_Values.Look(ref equalizeCost, "equalizeCost", false);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Values.Look(ref experimentalAnomalySupport, "experimentalAnomalySupport", false);
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            bool showResearchButtonWas = featureEnabled;

            //textanchor prevanchor = text.anchor;
            //gamefont prevfont = text.font;
            //text.font = gamefont.tiny;
            //string versionstring = "cm_semi_random_research_setting_version".translate() + semirandomresearchmod.version;
            //text.anchor = textanchor.lowerleft;
            //widgets.label(new rect(0, 0, inrect.width, inrect.height + window.closebutsize.y + 40f), versionstring);
            //text.anchor = prevanchor;
            //text.font = prevfont;


            Listing_Standard listing_Standard = new Listing_Standard();

            listing_Standard.ColumnWidth = (inRect.width - 34f) / 2f;

            listing_Standard.Begin(inRect);

            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Feature_Enabled_Label".Translate(), ref featureEnabled, "CM_Semi_Random_Research_Setting_Feature_Enabled_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Reroll_All_Every_Time_Label".Translate(), ref rerollAllEveryTime, "CM_Semi_Random_Research_Setting_Reroll_All_Every_Time_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Allow_Switching_Research_Label".Translate(), ref allowSwitchingResearch, "CM_Semi_Random_Research_Setting_Allow_Switching_Research_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Equalize_Cost_Label".Translate(), ref equalizeCost, "CM_Semi_Random_Research_Setting_Equalize_Cost_Description".Translate());

            string progressAddChoiceLableTooltip = "CM_Semi_Random_Research_Setting_Progress_Adds_Choice_Description".Translate() + "\n\n";
            foreach (ProgressAddsChoice option in System.Enum.GetValues(typeof(ProgressAddsChoice)))
            {
                progressAddChoiceLableTooltip += ("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_" + option.ToString() + "_Label").Translate() + ": " + ("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_" + option.ToString() + "_Description").Translate() + "\n\n";
            }
            listing_Standard.Label("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_Label".Translate(), -1, progressAddChoiceLableTooltip);

            {
                Rect button_rect = listing_Standard.GetRect(26);
                List<FloatMenuOption> progressAddsChoiceOptions = new List<FloatMenuOption>();
                foreach (ProgressAddsChoice option in System.Enum.GetValues(typeof(ProgressAddsChoice)))
                {
                    string keyLablelOption = ("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_" + option.ToString() + "_Label").Translate();
                    var floatMenuaOption = new FloatMenuOption(keyLablelOption, () =>
                    {
                        progressAddsChoice = option;
                    });
                    floatMenuaOption.tooltip = new TipSignal(("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_" + option.ToString() + "_Description").Translate());
                    progressAddsChoiceOptions.Add(floatMenuaOption);
                }
                DoButtonOption(button_rect,
                    ("CM_Semi_Random_Research_Setting_Progress_Adds_Choice_" + progressAddsChoice.ToString() + "_Label").Translate(),
                    progressAddChoiceLableTooltip,
                    progressAddsChoiceOptions, button_rect.width / 10, button_rect.width / 10);
            }

            listing_Standard.GapLine();

            string rerollLableTooltip = "CM_Semi_Random_Research_Setting_Manual_Reroll_Label".Translate() + "\n\n";
            foreach (ManualReroll option in System.Enum.GetValues(typeof(ManualReroll)))
            {
                rerollLableTooltip += ("CM_Semi_Random_Research_Setting_Manual_Reroll_" + option.ToString() + "_Label").Translate() + ": " + ("CM_Semi_Random_Research_Setting_Manual_Reroll_" + option.ToString() + "_Description").Translate() + "\n\n";
            }
            listing_Standard.Label("CM_Semi_Random_Research_Setting_Manual_Reroll_Label".Translate(), -1, rerollLableTooltip);

            {
                Rect button_rect = listing_Standard.GetRect(26);
                List<FloatMenuOption> manualRerollOptions = new List<FloatMenuOption>();
                foreach (ManualReroll option in System.Enum.GetValues(typeof(ManualReroll)))
                {
                    string keyLablelOption = ("CM_Semi_Random_Research_Setting_Manual_Reroll_" + option.ToString() + "_Label").Translate();
                    var floatMenuaOption = new FloatMenuOption(keyLablelOption, () =>
                    {
                        allowManualReroll = option;
                    });
                    floatMenuaOption.tooltip = new TipSignal(("CM_Semi_Random_Research_Setting_Manual_Reroll_" + option.ToString() + "_Description").Translate());
                    manualRerollOptions.Add(floatMenuaOption);
                }
                DoButtonOption(button_rect,
                    ("CM_Semi_Random_Research_Setting_Manual_Reroll_" + allowManualReroll.ToString() + "_Label").Translate(),
                    rerollLableTooltip,
                    manualRerollOptions, button_rect.width / 10, button_rect.width / 10);
            }

            listing_Standard.Gap();

            if (allowManualReroll != ManualReroll.None)
            {
                listing_Standard.Label(("CM_Semi_Random_Research_Setting_Prevent_Rerolled_From_Appearing_Label".Translate()) + ": " + reofferAfterAmountOfRerolls.ToString(), -1, "CM_Semi_Random_Research_Setting_Prevent_Rerolled_From_Appearing_Description".Translate());
                listing_Standard.IntAdjuster(ref reofferAfterAmountOfRerolls, 1);
            }

            listing_Standard.GapLine();

            listing_Standard.Label("CM_Semi_Random_Research_Setting_Type_Of_Projects_Count_Label".Translate());
            if (listing_Standard.RadioButton("CM_Semi_Random_Research_Setting_Static_Projects_Count_Label".Translate(), amountSelection == ChoiceAmountSelection.Static, 8f, "CM_Semi_Random_Research_Setting_Static_Projects_Count_Description".Translate()))
            {
                amountSelection = ChoiceAmountSelection.Static;
            }
            if (listing_Standard.RadioButton("CM_Semi_Random_Research_Setting_Dynamic_Projects_Count_Label".Translate(), amountSelection == ChoiceAmountSelection.PerColonist, 8f, "CM_Semi_Random_Research_Setting_Dynamic_Projects_Count_Description".Translate()))
            {
                amountSelection = ChoiceAmountSelection.PerColonist;
            }

            listing_Standard.Label(("CM_Semi_Random_Research_Setting_Available_Projects_Count_Label".Translate()) + ": " + availableProjectCount.ToString(), -1, "CM_Semi_Random_Research_Setting_Available_Projects_Count_Description".Translate());
            listing_Standard.IntAdjuster(ref availableProjectCount, 1, 0);
            if (availableProjectCount > maxProjectCount)
            {
                maxProjectCount = availableProjectCount;
            }

            if (amountSelection == ChoiceAmountSelection.PerColonist)
            {
                listing_Standard.Label(("CM_Semi_Random_Research_Setting_Additional_Project_Per_XColonists_Label".Translate()) + ": " + additionalProjectPerXColonists.ToString(), -1, "CM_Semi_Random_Research_Setting_Additional_Project_Per_XColonists_Description".Translate());
                listing_Standard.IntAdjuster(ref additionalProjectPerXColonists, 1, 1);

                listing_Standard.Label(("CM_Semi_Random_Research_Setting_Max_Projects_Label".Translate()) + ": " + maxProjectCount.ToString(), -1, "CM_Semi_Random_Research_Setting_Max_Projects_Description".Translate());
                listing_Standard.IntAdjuster(ref maxProjectCount, 1, 1);
                if (availableProjectCount > maxProjectCount)
                {
                    availableProjectCount = maxProjectCount;
                }
            }

            listing_Standard.GapLine();
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Force_Lowest_Tech_Level_Label".Translate(), ref forceLowestTechLevel, "CM_Semi_Random_Research_Setting_Force_Lowest_Tech_Level_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Restrict_To_Faction_Tech_Level_Label".Translate(), ref restrictToFactionTechLevel, "CM_Semi_Random_Research_Setting_Restrict_To_Faction_Tech_Level_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Allow_One_Higher_Tech_Project_Label".Translate(), ref allowOneHigherTechProject, "CM_Semi_Random_Research_Setting_Allow_One_Higher_Tech_Project_Description".Translate());

            listing_Standard.GapLine();
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Verbose_Logging_Label".Translate(), ref verboseLogging, "CM_Semi_Random_Research_Setting_Verbose_Logging_Description".Translate());
            listing_Standard.CheckboxLabeled("CM_Semi_Random_Research_Setting_Experimental_Anomaly_Support_Label".Translate(), ref experimentalAnomalySupport, "CM_Semi_Random_Research_Setting_Experimental_Anomaly_Support_Description".Translate());


            listing_Standard.End();


            if (featureEnabled != showResearchButtonWas)
                UpdateShowResearchButton();

            DumpSettingToLog();
        }

        private void DoButtonOption(Rect rect, string text, string tooltip, List<FloatMenuOption> options, float leftPad = 0, float rightPad = 0)
        {
            rect.x += leftPad;
            rect.width -= leftPad + rightPad;
            bool button1 = Widgets.ButtonImage(rect, null, true, tooltip);
            bool button2 = Widgets.ButtonText(rect, text);
            if (button1 || button2)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        public void UpdateSettings()
        {
            loggedSettings = false;
            DumpSettingToLog();
            ResearchTracker researchTracker = Current.Game?.World?.GetComponent<ResearchTracker>();
            if (researchTracker != null)
                researchTracker.SettingsChanged();
        }

        public void DumpSettingToLog()
        {
            if (loggedSettings || !verboseLogging)
                return;

            loggedSettings = true;
            Log.Message($"[CM_Semi_Random_Research] Current settings are: featureEnabled: {featureEnabled} " +
                $"rerollAllEveryTime: {rerollAllEveryTime} " +
                $"forceLowestTechLevel: {forceLowestTechLevel} " +
                $"restrictToFactionTechLevel: {restrictToFactionTechLevel} " +
                $"allowOneHigherTechProject: {allowOneHigherTechProject} " +
                $"allowSwitchingResearch: {allowSwitchingResearch} " +
                $"progressAddsChoice: {progressAddsChoice} " +
                $"allowManualReroll: {allowManualReroll} " +
                $"amountSelection: {amountSelection} " +
                $"availableProjectCount: {availableProjectCount} " +
                $"additionalProjectPerXColonists: {additionalProjectPerXColonists} " +
                $"maxProjectCount: {maxProjectCount} " +
                $"reofferAfterAmountOfRerolls: {reofferAfterAmountOfRerolls} " +
                $"equalizeCost: {equalizeCost} " +
                $"verboseLogging: {verboseLogging} " +
                $"experimentalAnomalySupport: {experimentalAnomalySupport} ");
        }

        public void UpdateShowResearchButton()
        {
            UIRoot_Play uiRootPlay = Find.UIRoot as UIRoot_Play;

            if (uiRootPlay != null)
            {
                MainButtonsRoot mainButtonsRoot = uiRootPlay.mainButtonsRoot;

                if (mainButtonsRoot != null)
                {
                    FieldInfo allButtonsInOrderField = mainButtonsRoot.GetType().GetField("allButtonsInOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    List<MainButtonDef> mainButtons = allButtonsInOrderField.GetValue(mainButtonsRoot) as List<MainButtonDef>;

                    MainButtonDef buttonToUse = SemiRandomResearchDefOf.Semi_Random_Research;
                    if (!featureEnabled)
                        buttonToUse = MainButtonDefOf.Research;

                    // Pull both of the buttons out to be sure, then put the correct one back in
                    mainButtons = mainButtons.Where(button => button != MainButtonDefOf.Research && button != SemiRandomResearchDefOf.Semi_Random_Research).ToList();
                    mainButtons.Add(buttonToUse);
                    mainButtons.Sort((a, b) => a.order - b.order);

                    allButtonsInOrderField.SetValue(mainButtonsRoot, mainButtons);
                }
            }
        }
    }
}