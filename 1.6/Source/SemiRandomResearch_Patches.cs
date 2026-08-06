using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace CM_Semi_Random_Research
{
    // =========================================================================
    // MAIN BUTTONS ROOT PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class MainButtonsRoot_Patches
    {
        [HarmonyPatch(typeof(MainButtonsRoot))]
        [HarmonyPatch(MethodType.Constructor)]
        public static class MainButtonsRoot_Constructor
        {
            [HarmonyPostfix]
            public static void Postfix(ref List<MainButtonDef> ___allButtonsInOrder)
            {
                if (___allButtonsInOrder != null)
                {
                    if (SemiRandomResearchMod.settings.featureEnabled && !SemiRandomResearchMod.settings.usingNodeResearch)
                        ___allButtonsInOrder = ___allButtonsInOrder.Where(button => button != MainButtonDefOf.Research).ToList();
                    else
                        ___allButtonsInOrder = ___allButtonsInOrder.Where(button => button != SemiRandomResearchDefOf.Semi_Random_Research).ToList();
                }
            }
        }
    }

    // =========================================================================
    // MAIN TABS ROOT PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class MainTabsRoot_Patches
    {
        [HarmonyPatch(typeof(MainTabsRoot))]
        [HarmonyPatch("SetCurrentTab", MethodType.Normal)]
        public static class MainTabsRoot_SetCurrentTab
        {
            [HarmonyPrefix]
            public static void Prefix(ref MainButtonDef tab)
            {
                if (tab == null)
                    return;

                if (tab == MainButtonDefOf.Research && SemiRandomResearchMod.settings.featureEnabled && !SemiRandomResearchMod.settings.usingNodeResearch)
                {
                    tab = SemiRandomResearchDefOf.Semi_Random_Research;
                }
            }
        }
    }

    // =========================================================================
    // MAIN TAB WINDOW RESEARCH PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class MainTabWindow_Research_Patches
    {
        private static readonly Texture2D NextResearchButtonIcon = ContentFinder<Texture2D>.Get("UI/Buttons/MainButtons/CM_Semi_Random_Research_Random");

        [HarmonyPatch(typeof(MainTabWindow_Research))]
        [HarmonyPatch("DrawLeftRect", MethodType.Normal)]
        public static class MainTabWindow_Research_DrawLeftRect
        {
            [HarmonyPostfix]
            public static void Postfix(ResearchProjectDef __instance, Rect leftOutRect)
            {
                float buttonSize = 32.0f;
                Rect buttonRect = new Rect(leftOutRect.xMax - buttonSize, leftOutRect.yMin, buttonSize, buttonSize);

                bool pressedButton1 = Widgets.ButtonTextSubtle(buttonRect, "");
                bool pressedButton2 = Widgets.ButtonImage(buttonRect, NextResearchButtonIcon);

                if (pressedButton1 || pressedButton2)
                {
                    SoundDefOf.ResearchStart.PlayOneShotOnCamera();

                    SemiRandomResearchMod.settings.usingNodeResearch = false;
                    LoadedModManager.GetMod<SemiRandomResearchMod>().WriteSettings();
                    SemiRandomResearchMod.UpdateShowResearchButton();

                    MainTabWindow currentWindow = Find.WindowStack.WindowOfType<MainTabWindow>();
                    MainTabWindow newWindow = SemiRandomResearchDefOf.Semi_Random_Research.TabWindow;

                    if (currentWindow != null && newWindow != null)
                    {
                        Find.WindowStack.TryRemove(currentWindow, false);
                        Find.WindowStack.Add(newWindow);
                        SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MainTabWindow_Research))]
        [HarmonyPatch("DrawStartButton", MethodType.Normal)]
        public static class MainTabWindow_Research_DrawStartButton
        {
            [HarmonyPrefix]
            public static void Prefix(List<string> ___lockedReasons, ResearchTabDef ___curTabInt)
            {
                ___lockedReasons.Clear();
                if (SemiRandomResearchMod.settings.featureEnabled && !SemiRandomResearchMod.settings.usingNodeResearch)
                {
                    ___lockedReasons.Add("Semi Random Research is active.");
                }
            }

            public static bool PrefixSkip(List<string> ___lockedReasons, ResearchTabDef ___curTabInt, ResearchProjectDef ___selectedProject)
            {
                Prefix(___lockedReasons, ___curTabInt);
                return ___selectedProject == null || !SemiRandomResearchMod.settings.featureEnabled || SemiRandomResearchMod.settings.usingNodeResearch || !___selectedProject.CanStartNow;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo selectedProjectFieldInfo = typeof(RimWorld.MainTabWindow_Research).GetField("selectedProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                MethodInfo canStartNowMethodInfo = AccessTools.Method(typeof(Verse.ResearchProjectDef), "get_CanStartNow");
                MethodInfo replacementCanStartCheck = AccessTools.Method(typeof(SemiRandomResearchUtility), nameof(SemiRandomResearchUtility.CanSelectNormalResearchNow));
                MethodInfo isCurrentProjectMethodInfo = AccessTools.Method(typeof(ResearchManager), "IsCurrentProject");
                MethodInfo replacementIsCurrentProject = AccessTools.Method(typeof(SemiRandomResearchUtility), nameof(SemiRandomResearchUtility.IsCurrentProject));

                MethodInfo clearListMethodInfo = AccessTools.Method(new List<string>().GetType(), "Clear");
                FieldInfo lockedReasonsFieldInfo = typeof(RimWorld.MainTabWindow_Research).GetField("lockedReasons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                List<CodeInstruction> instructionList = instructions.ToList();

                for (int i = 2; i < instructionList.Count; ++i)
                {
                    if (instructionList[i - 2].IsLdarg() &&
                        instructionList[i - 1].LoadsField(selectedProjectFieldInfo) &&
                        instructionList[i - 0].Calls(canStartNowMethodInfo))
                    {
                        instructionList[i - 0] = new CodeInstruction(OpCodes.Call, replacementCanStartCheck);
                    }

                    if (i > 5)
                    {
                        if (instructionList[i - 5].IsLdarg() &&
                            instructionList[i - 4].LoadsField(selectedProjectFieldInfo) &&
                            instructionList[i - 3].Calls(isCurrentProjectMethodInfo) &&
                            instructionList[i - 0].LoadsConstant("StopResearch"))
                        {
                            instructionList[i - 6].opcode = OpCodes.Nop;
                            instructionList[i - 3] = new CodeInstruction(OpCodes.Call, replacementIsCurrentProject);
                        }
                    }

                    if (
                        instructionList[i - 1].LoadsField(lockedReasonsFieldInfo) &&
                        instructionList[i - 0].Calls(clearListMethodInfo))
                    {
                        instructionList[i - 1].opcode = OpCodes.Nop;
                        instructionList[i - 0].opcode = OpCodes.Nop;
                    }
                }

                foreach (CodeInstruction instruction in instructionList)
                {
                    yield return instruction;
                }
            }
        }
    }

    // =========================================================================
    // RESEARCH MANAGER PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class ResearchManager_Patches
    {
        [HarmonyPatch(typeof(ResearchManager))]
        [HarmonyPatch("FinishProject", MethodType.Normal)]
        public static class ResearchManager_FinishProject
        {
            [HarmonyPrefix]
            public static void HarmonyPrefix(ResearchProjectDef proj)
            {
                ResearchTracker researchTracker = Current.Game?.World?.GetComponent<ResearchTracker>();
                if (researchTracker != null)
                {
                    researchTracker.ConsiderProjectFinished(proj);
                }
            }
        }

        [HarmonyPatch(typeof(ResearchManager))]
        [HarmonyPatch("AddProgress", MethodType.Normal)]
        public static class ResearchManager_AddProgress
        {
            [HarmonyPrefix]
            public static void Prefix(ResearchProjectDef proj, float amount, Pawn source)
            {
                ResearchTracker researchTracker = Current.Game?.World?.GetComponent<ResearchTracker>();
                if (researchTracker != null &&
                    (proj.ProgressReal == 0 || SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.AddChoiceOnlyOnGain) &&
                    SemiRandomResearchMod.settings.progressAddsChoice != ProgressAddsChoice.Never &&
                    !researchTracker.GetCurrentlyAvailableProjects().Contains(proj) &&
                    proj.CanStartNow)
                {
                    if (!researchTracker.CurrentProject.Any(x => x.knowledgeCategory == proj.knowledgeCategory) ||
                        SemiRandomResearchMod.settings.allowSwitchingResearch)
                    {
                        researchTracker.AddProjectToAvailableProjects(proj);
                    }
                }
            }
        }
    }

    // =========================================================================
    // ALERT PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class Alert_NeedResearchProject_Patches
    {
        [HarmonyPatch(typeof(Alert_NeedResearchProject))]
        [HarmonyPatch("OnClick", MethodType.Normal)]
        public static class Alert_NeedResearchProject_OnClick
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (SemiRandomResearchMod.settings.featureEnabled && !SemiRandomResearchMod.settings.usingNodeResearch)
                {
                    Find.MainTabsRoot.SetCurrentTab(SemiRandomResearchDefOf.Semi_Random_Research);
                    return false;
                }
                return true;
            }
        }
    }

    // =========================================================================
    // DIALOG RESEARCH COMPLETE PATCHES
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class Dialog_ResearchComplete_Patches
    {
        static Dialog_ResearchComplete_Patches()
        {
            try
            {
                var harmony = new Harmony("CM_Semi_Random_Research.Dialog_ResearchComplete_Patches");
                var finishProjectMethod = typeof(ResearchManager).GetMethod("FinishProject",
                    new Type[] { typeof(ResearchProjectDef), typeof(bool), typeof(Pawn), typeof(bool) });

                if (finishProjectMethod != null)
                {
                    harmony.Patch(finishProjectMethod,
                        prefix: new HarmonyMethod(typeof(Dialog_ResearchComplete_Patches), nameof(FinishProject_Prefix)));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Semi Random Research] Error while patching: {ex}");
            }
        }

        public static void FinishProject_Prefix(ResearchProjectDef proj, ref bool doCompletionDialog, Pawn researcher, ref bool doCompletionLetter)
        {
            try
            {
                // If the mod is off OR Node Research currently has the baton, sleep and let Node/Vanilla handle it!
                if (!SemiRandomResearchMod.settings.featureEnabled || SemiRandomResearchMod.settings.usingNodeResearch)
                    return;

                doCompletionDialog = false;
                doCompletionLetter = false;

                if (Verse.GenScene.InEntryScene || Current.Game == null || Current.Game.World == null ||
                    Current.Game.World.worldObjects == null || LongEventHandler.AnyEventNowOrWaiting)
                    return;

                var rateTracker = Current.Game.World.GetComponent<ResearchRateTracker>();
                var rateInfo = rateTracker?.GetResearchRateInfo(proj);

                StringBuilder letterText = new StringBuilder();
                letterText.AppendLine($"Research completed: {proj.LabelCap}");

                if (rateInfo != null && rateInfo.TotalSamples > 0)
                {
                    letterText.AppendLine();
                    letterText.AppendLine($"Average rate: {rateInfo.AverageRateFormatted}");
                }

                if (researcher != null)
                {
                    letterText.AppendLine();
                    letterText.AppendLine($"Completed by: {researcher.LabelShort}");
                }

                if (SemiRandomResearchMod.settings.showCompletionLetter)
                {
                    var letter = LetterMaker.MakeLetter(
                        $"Research Complete: {proj.LabelCap}",
                        letterText.ToString(),
                        LetterDefOf.PositiveEvent,
                        researcher != null ? new LookTargets(researcher) : null);

                    Find.LetterStack.ReceiveLetter(letter);
                }

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        if (Find.UIRoot == null || !(Find.UIRoot is UIRoot_Play)) return;
                        Find.TickManager?.Pause();

                        MainButtonDef researchButton = SemiRandomResearchDefOf.Semi_Random_Research;
                        if (researchButton != null && Find.MainTabsRoot != null)
                        {
                            Find.MainTabsRoot.SetCurrentTab(researchButton);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Semi Random Research] Error in queued UI update: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[Semi Random Research] Error in FinishProject_Prefix: {ex}");
            }
        }
    }

    // =========================================================================
    // NODE RESEARCH INTEGRATION
    // =========================================================================
    [StaticConstructorOnStartup]
    public static class NodeResearch_Integration
    {
        static NodeResearch_Integration()
        {
            if (ModLister.GetActiveModWithIdentifier("ferny.noderesearch") != null)
            {
                var harmony = new Harmony("CM_Semi_Random_Research.NodeIntegration");
                var type = AccessTools.TypeByName("BetterResearchMenu.MainTabWindow_BetterResearch");

                if (type != null)
                {
                    var original = AccessTools.Method(type, "DrawGraphControls");
                    var transpiler = AccessTools.Method(typeof(NodeResearch_Integration), nameof(DrawGraphControls_Transpiler));
                    var postfix = AccessTools.Method(typeof(NodeResearch_Integration), nameof(DrawGraphControls_Postfix));

                    harmony.Patch(original, transpiler: new HarmonyMethod(transpiler), postfix: new HarmonyMethod(postfix));
                    Log.Message("[Semi Random Research] Successfully integrated with Node Research UI.");
                }
            }
        }

        public static IEnumerable<CodeInstruction> DrawGraphControls_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool foundVanillaTip = false;
            bool shiftedSettings = false;

            foreach (var instruction in instructions)
            {
                yield return instruction;

                if (!foundVanillaTip && instruction.opcode == OpCodes.Ldstr && instruction.operand is string str && str == "BRM_OpenVanillaMenu")
                {
                    foundVanillaTip = true;
                }

                if (foundVanillaTip && !shiftedSettings && instruction.opcode == OpCodes.Add)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 32f);
                    yield return new CodeInstruction(OpCodes.Add);
                    shiftedSettings = true;
                    foundVanillaTip = false;
                }
            }
        }

        public static void DrawGraphControls_Postfix(Rect controlAreaRect, Window __instance)
        {
            float btnSize = 24f;
            float btnGap = 8f;
            float xOffset = (btnSize + btnGap) * 2;

            Rect semiBtnRect = new Rect(controlAreaRect.x + xOffset, controlAreaRect.y, btnSize, btnSize);
            Texture2D texSemiRandom = ContentFinder<Texture2D>.Get("UI/semi", true);

            if (Widgets.ButtonImage(semiBtnRect, texSemiRandom))
            { 
                SemiRandomResearchMod.settings.usingNodeResearch = false;
                LoadedModManager.GetMod<SemiRandomResearchMod>().WriteSettings();
                SemiRandomResearchMod.UpdateShowResearchButton();

                ResearchProjectDef activeProj = Find.ResearchManager.GetProject();
                ResearchTracker tracker = Current.Game?.World?.GetComponent<ResearchTracker>();
                if (tracker != null && activeProj != null && !tracker.CurrentProject.Contains(activeProj))
                {
                    tracker.SetCurrentProject(activeProj, activeProj.knowledgeCategory);
                }

                __instance.Close();
                Find.MainTabsRoot.SetCurrentTab(SemiRandomResearchDefOf.Semi_Random_Research);
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(semiBtnRect, "Open Semi-Random Research");
        }
    }
}