using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Grammar;

namespace CM_Semi_Random_Research
{
    public class ResearchTracker : WorldComponent
    {
        private List<ResearchProjectDef> currentAvailableProjects = new List<ResearchProjectDef>();
        private Dictionary<ResearchProjectDef, int> notChosenProjects = new Dictionary<ResearchProjectDef, int>();
        private Dictionary<string, int> currentRerollState = new Dictionary<string, int>();
        private List<ResearchProjectDef> currentProjects = new List<ResearchProjectDef>();
        private HashSet<ResearchProjectDef> additionalAvailableProjects = new HashSet<ResearchProjectDef>();
        private HashSet<KnowledgeCategoryDef> pendingResearchRerolls = new HashSet<KnowledgeCategoryDef>();

        public List<ResearchProjectDef> CurrentProject => currentProjects;

        public bool autoResearch = false;

        private Dictionary<string, bool> rerolled = new Dictionary<string, bool>();
        private Dictionary<string, List<ResearchProjectDef>> projectDefsCacheByType = new Dictionary<string, List<ResearchProjectDef>>();
        private Dictionary<string, List<ResearchProjectDef>> currentProjectDefsCacheByType = new Dictionary<string, List<ResearchProjectDef>>();
        private HashSet<string> completedTypes = new HashSet<string>();

        private int tickCounter = 0;
        private int tickShortOffset = 10;
        private int tickOffset = 360;
        private int previousDefCount = 0;
        private bool additionalProjectsRefresh = true;

        private Dictionary<string, bool> lastPicked = new Dictionary<string, bool>();
        private Dictionary<string, string> loggedMessages = new Dictionary<string, string>();

        private List<string> all_typeKeys;

        public ResearchTracker(World world) : base(world)
        {
            previousDefCount = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count;
            RefreshTypeKeys();
        }

        private void RefreshTypeKeys()
        {
            all_typeKeys = DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading.Select(x => x.defName).ToList();
            all_typeKeys.Add("Standard");
            all_typeKeys.Add("Gravship");
            all_typeKeys.Add("Divinitech"); // Future-proofed for your other mod!
            all_typeKeys = all_typeKeys.Distinct().ToList();
        }

        // ==============================================================================
        // PSEUDO-CATEGORY GENERATOR
        // ==============================================================================
        public static string GetCategoryKey(ResearchProjectDef def)
        {
            if (def == null) return "Standard";
            if (def.tab?.defName == "VGE_Gravtech" || def.tab?.defName == "VGE_GravShip") return "Gravship";
            if (def.knowledgeCategory?.defName == "Information") return "Divinitech";
            if (def.knowledgeCategory != null) return def.knowledgeCategory.defName;
            return "Standard";
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            SettingsChanged();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref currentAvailableProjects, "currentAvailableProjects", LookMode.Def);
            Scribe_Collections.Look(ref notChosenProjects, "notChosenProjects", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref additionalAvailableProjects, "additionalAvailableProjectsByGain", LookMode.Def);
            Scribe_Collections.Look(ref currentProjects, "currentProject", LookMode.Def);

            if (notChosenProjects == null) notChosenProjects = new Dictionary<ResearchProjectDef, int>();
            if (currentProjects == null) currentProjects = new List<ResearchProjectDef>();
            if (currentAvailableProjects == null) currentAvailableProjects = new List<ResearchProjectDef>();
            if (additionalAvailableProjects == null) additionalAvailableProjects = new HashSet<ResearchProjectDef>();

            if (SemiRandomResearchMod.settings.verboseLogging)
            {
                string allCurrentProjects = "";
                foreach (ResearchProjectDef def in currentProjects)
                    allCurrentProjects += def != null ? def.LabelCap.RawText : "Null" + " ";
                LogIfNewMessage("Loaded Current Projects", allCurrentProjects);

                string allAvailableProjects = "";
                foreach (ResearchProjectDef def in currentAvailableProjects)
                    allAvailableProjects += def != null ? def.LabelCap.RawText : "Null" + " ";
                LogIfNewMessage("Loaded Available Projects", allAvailableProjects);
            }

            Scribe_Collections.Look(ref rerolled, "rerolled");
            if (rerolled == null) rerolled = new Dictionary<string, bool>();

            Scribe_Collections.Look(ref currentRerollState, "currentRerollState");
            if (currentRerollState == null) currentRerollState = new Dictionary<string, int>();

            Scribe_Values.Look(ref autoResearch, "autoResearch", false);

            Scribe_Collections.Look(ref lastPicked, "lastPicked");
            if (lastPicked == null) lastPicked = new Dictionary<string, bool>();

            Scribe_Collections.Look(ref pendingResearchRerolls, "pendingResearchRerolls", LookMode.Def);
            if (pendingResearchRerolls == null) pendingResearchRerolls = new HashSet<KnowledgeCategoryDef>();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if ((tickCounter % tickShortOffset) == 0)
            {
                if (all_typeKeys == null) RefreshTypeKeys();

                foreach (string typeKey in all_typeKeys)
                {
                    List<ResearchProjectDef> currentProjectOfType = new List<ResearchProjectDef>();
                    if (currentProjectDefsCacheByType.ContainsKey(typeKey))
                    {
                        currentProjectOfType = currentProjectDefsCacheByType[typeKey];
                    }
                    bool finished = false;
                    ResearchProjectDef finishedProject = null;
                    if (!currentProjectOfType.Empty())
                    {
                        finishedProject = currentProjectOfType.FirstOrDefault(x => x.IsFinished);
                    }
                    if (finishedProject != null)
                    {
                        finished = true;
                        ConsiderProjectFinished(finishedProject);
                    }

                    if (currentProjectOfType.Empty() || finished)
                    {
                        if (autoResearch && (finished || (tickCounter % tickOffset) == 0))
                        {
                            List<ResearchProjectDef> possibleProjectsOfType = GetCurrentlyAvailableProjects().Where(x => GetCategoryKey(x) == typeKey).ToList();

                            if (!possibleProjectsOfType.Empty())
                            {
                                SetCurrentProjectByKey(possibleProjectsOfType.First(), typeKey);
                                currentProjectOfType = currentProjects.Where(x => GetCategoryKey(x) == typeKey).ToList();
                            }
                        }
                    }

                    if ((tickCounter % tickOffset) == 0)
                    {
                        // Safely find the active project for this pseudo-category
                        ResearchProjectDef activeProject = null;
                        ResearchProjectDef standardActive = Find.ResearchManager.GetProject(null);
                        if (standardActive != null && GetCategoryKey(standardActive) == typeKey)
                            activeProject = standardActive;

                        foreach (var cat in DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading)
                        {
                            ResearchProjectDef catActive = Find.ResearchManager.GetProject(cat);
                            if (catActive != null && GetCategoryKey(catActive) == typeKey)
                                activeProject = catActive;
                        }

                        if (activeProject == null && !currentProjectOfType.Empty() && currentProjectOfType.First().CanStartNow)
                        {
                            SetCurrentProjectByKey(currentProjectOfType.First(), typeKey);
                        }
                        else if (activeProject != null && (currentProjectOfType.Empty() || !currentProjectOfType.Contains(activeProject)) && activeProject.CanStartNow)
                        {
                            if (!SemiRandomResearchMod.settings.featureEnabled)
                            {
                                SetCurrentProjectByKey(activeProject, typeKey);
                            }
                            else if (currentProjectOfType.Empty() && currentAvailableProjects.Contains(activeProject))
                            {
                                SetCurrentProjectByKey(activeProject, typeKey);
                            }
                            else if (!currentProjectOfType.Empty())
                            {
                                SetCurrentProjectByKey(currentProjectOfType.First(), typeKey);
                            }
                            else
                            {
                                LogIfNewMessage("WorldTickUnexpectedState" + typeKey, $"Error? Set as activeProject: {activeProject.LabelCap} currentAvailableProjects: {currentAvailableProjects.Count} and of type {typeKey}: {currentAvailableProjects.Where(x => GetCategoryKey(x) == typeKey).Count()}");
                                SetCurrentProjectByKey(activeProject, typeKey);
                            }
                        }
                    }
                }
                if (SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.Never && additionalAvailableProjects.Any())
                {
                    additionalAvailableProjects.Clear();
                }
            }
            tickCounter = (tickCounter + 1) % tickOffset;
        }

        public List<ResearchProjectDef> GetCurrentlyAvailableProjects()
        {
            if (all_typeKeys == null) RefreshTypeKeys();
            List<ResearchProjectDef> result = new List<ResearchProjectDef>();
            SemiRandomResearchMod.settings.DumpSettingToLog();

            foreach (string typeKey in all_typeKeys)
            {
                currentAvailableProjects = currentAvailableProjects.Where(projectDef => projectDef != null &&
                !projectDef.IsFinished &&
                !projectDef.IsHidden &&
                Compatibility.SatisfiesAlienRaceRestriction(projectDef)).ToList();

                List<ResearchProjectDef> currentAvailableValidProjectsOfType = currentAvailableProjects.Where(x => GetCategoryKey(x) == typeKey && x.CanStartNow).ToList();
                List<ResearchProjectDef> currentProjectOfType = currentProjects.Where(x => GetCategoryKey(x) == typeKey).ToList();

                if (!SemiRandomResearchMod.settings.rerollAllEveryTime ||
                    SemiRandomResearchMod.settings.allowSwitchingResearch ||
                    currentProjectOfType.Empty() ||
                    currentProjectOfType.Any(x => x.IsFinished || !Compatibility.SatisfiesAlienRaceRestriction(x)))
                {

                    int additionalProjects = SemiRandomResearchMod.settings.amountSelection == ChoiceAmountSelection.PerColonist ?
                        PawnsFinder.AllMapsCaravansAndTravellingTransporters_AliveSpawned_FreeColonists_NoSuspended.
                        Where(collonist => !collonist.GetDisabledWorkTypes().Any(workType => workType.defName == "Research")).Count()
                        / SemiRandomResearchMod.settings.additionalProjectPerXColonists
                        : 0;

                    bool handledProjects = false;
                    int numberOfMissingProjects = Math.Min((SemiRandomResearchMod.settings.availableProjectCount + additionalProjects), SemiRandomResearchMod.settings.maxProjectCount) - currentAvailableValidProjectsOfType.Count;

                    if (numberOfMissingProjects > 0 || additionalProjectsRefresh)
                    {
                        List<ResearchProjectDef> nextProjects = GetResearchableProjects(numberOfMissingProjects, typeKey);

                        if (!nextProjects.NullOrEmpty())
                        {
                            currentAvailableProjects.AddRange(nextProjects);
                            currentAvailableProjects = currentAvailableProjects.Distinct().ToList();
                            currentAvailableValidProjectsOfType.AddRange(nextProjects);
                            currentAvailableValidProjectsOfType = currentAvailableValidProjectsOfType.Distinct().ToList();
                            handledProjects = true;
                            result.AddRange(currentAvailableValidProjectsOfType);
                        }
                        numberOfMissingProjects = Math.Min((SemiRandomResearchMod.settings.availableProjectCount + additionalProjects), SemiRandomResearchMod.settings.maxProjectCount) - currentAvailableValidProjectsOfType.Count;
                    }
                    int projectsAddedAdditional = currentAvailableValidProjectsOfType.Count(x => additionalAvailableProjects.Contains(x));
                    int progressAddedProgressed = currentAvailableValidProjectsOfType.Count(x => x.ProgressReal > 0 && !currentProjectOfType.Contains(x) && !additionalAvailableProjects.Contains(x));
                    int extraAddedProgress = SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.AddChoice ? progressAddedProgressed : 0;
                    if (numberOfMissingProjects < -extraAddedProgress - projectsAddedAdditional)
                    {
                        int amountToRemove = -1 * numberOfMissingProjects - (extraAddedProgress + projectsAddedAdditional);
                        int amountTarget = currentAvailableValidProjectsOfType.Count - amountToRemove;
                        result.RemoveAll(x => currentAvailableValidProjectsOfType.Contains(x));
                        List<ResearchProjectDef> currentAvailableProjectsWithoutCurrentProject = new List<ResearchProjectDef>();
                        if (SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.ReplaceChoice)
                        {
                            IEnumerable<ResearchProjectDef> partiallyCompleted = currentAvailableValidProjectsOfType.Where(x => x.ProgressReal > 0 && !additionalAvailableProjects.Contains(x));
                            if (partiallyCompleted.Count() > amountTarget)
                            {
                                partiallyCompleted = partiallyCompleted.Skip(partiallyCompleted.Count() - amountTarget);
                            }
                            currentAvailableProjectsWithoutCurrentProject.AddRange(partiallyCompleted);
                        }
                        currentAvailableProjectsWithoutCurrentProject.AddRange(currentAvailableValidProjectsOfType.Where(x => additionalAvailableProjects.Contains(x)));
                        IEnumerable<ResearchProjectDef> keepable = currentAvailableValidProjectsOfType.Where(x => !currentProjects.Contains(x) && !currentAvailableProjectsWithoutCurrentProject.Contains(x));
                        currentAvailableProjectsWithoutCurrentProject.AddRange(keepable.Reverse().Skip(amountToRemove).Reverse());

                        if (!currentProjectOfType.Empty() && currentProjectOfType.Any(x => !x.IsFinished && Compatibility.SatisfiesAlienRaceRestriction(x)))
                        {
                            currentAvailableProjectsWithoutCurrentProject.AddRange(currentProjectOfType);
                        }
                        handledProjects = true;
                        result.AddRange(currentAvailableProjectsWithoutCurrentProject);
                        if (SemiRandomResearchMod.settings.verboseLogging)
                            LogIfNewMessage("numberOfMissingProjects < 0" + typeKey, $"More projects available than expected. numberOfMissingProjects: {numberOfMissingProjects} Values: additionalProjects {additionalProjects} amountToRemove: {amountToRemove} keepable.Count: {keepable.Count()} extraAddedProgress: {extraAddedProgress} projectsAddedAdditional:{projectsAddedAdditional}");

                    }
                    if (!handledProjects)
                    {
                        if (SemiRandomResearchMod.settings.verboseLogging && currentAvailableValidProjectsOfType.Count == 0)
                            LogIfNewMessage("numberOfMissingProjects = 0" + typeKey, $"No projects are to be added even though non are available?Values: additionalProjects {additionalProjects} extraAddedProgress: {extraAddedProgress} projectsAddedAdditional:{projectsAddedAdditional}");

                        result.AddRange(currentAvailableValidProjectsOfType);
                    }
                    additionalProjectsRefresh = false;
                }
                else
                {
                    result.AddRange(currentProjectOfType);
                }
            }
            return result;
        }

        private List<ResearchProjectDef> GetResearchableProjects(int count, string typeKey)
        {
            if (completedTypes.Contains(typeKey) &&
                previousDefCount == DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count)
            {
                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("Skipping" + typeKey, "Type Completed");
                }

                return new List<ResearchProjectDef>();
            }

            TechLevel maxCurrentProjectTechlevel = TechLevel.Archotech;
            if (currentAvailableProjects.Count > 0)
                maxCurrentProjectTechlevel = currentAvailableProjects.Select(projectDef => projectDef.techLevel).Max();
            TechLevel minCurrentProjectTechlevel = TechLevel.Archotech;
            if (currentAvailableProjects.Count > 0)
                minCurrentProjectTechlevel = currentAvailableProjects.Select(projectDef => projectDef.techLevel).Min();

            if (!projectDefsCacheByType.ContainsKey(typeKey) ||
                previousDefCount == DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count)
            {
                projectDefsCacheByType[typeKey] = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where((ResearchProjectDef projectDef) => !projectDef.IsFinished &&
                GetCategoryKey(projectDef) == typeKey).ToList();

                if (!projectDefsCacheByType[typeKey].Any())
                {
                    completedTypes.Add(typeKey);
                }
            }

            IEnumerable<ResearchProjectDef> allAvailableProjects = projectDefsCacheByType[typeKey]
                .Where((ResearchProjectDef projectDef) => !currentAvailableProjects.Contains(projectDef) &&
                projectDef.CanStartNow &&
                Compatibility.DoCompatibilityChecks(projectDef)).ToList();

            if (SemiRandomResearchMod.settings.verboseLogging)
            {
                if (!allAvailableProjects.Any() && currentAvailableProjects.Count == 0)
                {
                    List<ResearchProjectDef> allAvailableProjectsDebug = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

                    LogIfNewMessage("NoAvailableProjects1" + typeKey, $"[CM_Semi_Random_Research] Total projects in game: {allAvailableProjectsDebug.Count}");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => projectDef.CanStartNow).ToList();
                    LogIfNewMessage("NoAvailableProjects2" + typeKey, $"[CM_Semi_Random_Research] Of which {allAvailableProjectsDebug.Count} Could be started now");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => Compatibility.SatisfiesAlienRaceRestriction(projectDef)).ToList();
                    LogIfNewMessage("NoAvailableProjects3" + typeKey, $"[CM_Semi_Random_Research] Of which {allAvailableProjectsDebug.Count} you have the required races for");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => !projectDef.IsDummyResearch()).ToList();
                    LogIfNewMessage("NoAvailableProjects4" + typeKey, $"[CM_Semi_Random_Research] Of which {allAvailableProjectsDebug.Count} are not Dummy researches");
                }
            }

            ResearchProjectDef randomProject = null;
            if (allAvailableProjects.Any() && SemiRandomResearchMod.settings.allowOneHigherTechProject &&
                (!SemiRandomResearchMod.settings.restrictToFactionTechLevel || maxCurrentProjectTechlevel <= Faction.OfPlayer.def.techLevel) &&
                (!SemiRandomResearchMod.settings.forceLowestTechLevel || maxCurrentProjectTechlevel == minCurrentProjectTechlevel))
            {
                randomProject = allAvailableProjects.RandomElement();
            }

            if (SemiRandomResearchMod.settings.restrictToFactionTechLevel)
            {
                TechLevel maxTechLevel = Faction.OfPlayer.def.techLevel;
                allAvailableProjects = allAvailableProjects.Where(projectDef => projectDef.techLevel <= maxTechLevel).ToList();

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("AfterRestrictToFactionTechLevel" + typeKey, "Currently possible projects after restrictToFactionTechLevel: " + allAvailableProjects.Count());
                }
            }

            if (allAvailableProjects.Any() && SemiRandomResearchMod.settings.forceLowestTechLevel)
            {
                for (TechLevel techLevel = TechLevel.Animal; techLevel <= TechLevel.Archotech; ++techLevel)
                {
                    IEnumerable<ResearchProjectDef> projectsAtTechLevel = allAvailableProjects.Where(projectDef => projectDef.techLevel <= techLevel);
                    if (projectsAtTechLevel.Any() || minCurrentProjectTechlevel == techLevel)
                    {
                        allAvailableProjects = projectsAtTechLevel;
                        break;
                    }
                }

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("AfterForceLowestTechLevel" + typeKey, "Currently possible projects after forceLowestTechLevel: " + allAvailableProjects.Count());
                }
            }
            List<ResearchProjectDef> selectedProjects = new List<ResearchProjectDef>();
            selectedProjects.AddRange(allAvailableProjects.Where(x => additionalAvailableProjects.Contains(x)));
            IEnumerable<ResearchProjectDef> partiallyCompleted = allAvailableProjects.Where(x => x.ProgressReal > 0 && !additionalAvailableProjects.Contains(x));

            if (SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.AddChoice)
            {
                selectedProjects.AddRange(partiallyCompleted);
            }
            else if (SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.ReplaceChoice)
            {
                selectedProjects.AddRange(partiallyCompleted);
                count -= partiallyCompleted.Count();
            }
            allAvailableProjects = allAvailableProjects.Where(x => !selectedProjects.Contains(x));

            allAvailableProjects = allAvailableProjects.InRandomOrder();

            if (SemiRandomResearchMod.settings.reofferAfterAmountOfRerolls > 0)
            {
                List<ResearchProjectDef> possibleNotShownRecently = allAvailableProjects.Where(x => !notChosenProjects.ContainsKey(x)).ToList();

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("ReofferAfterAmountOfRerollsCount" + typeKey, "This many researches were not offered recently: " + possibleNotShownRecently.Count + " while this many were shown recently: " + notChosenProjects.Keys.Count(x => GetCategoryKey(x) == typeKey && !x.IsFinished));
                }
                int remainingCount = count;

                if (possibleNotShownRecently.Count < count)
                {
                    if (SemiRandomResearchMod.settings.verboseLogging)
                    {
                        LogIfNewMessage("PossibleNotShownRecently" + typeKey, "Picking from recently shown researches this many projects: " + (count - possibleNotShownRecently.Count));
                    }
                    possibleNotShownRecently.AddRange(allAvailableProjects.Where(x => notChosenProjects.ContainsKey(x)).Take(count - possibleNotShownRecently.Count));
                }

                allAvailableProjects = possibleNotShownRecently;
            }

            if (SemiRandomResearchMod.settings.equalizeCost && allAvailableProjects.Count() > count && count > 0)
            {

                int amountToRandomlyGenerate = count / 2;
                int amountToPick = count - amountToRandomlyGenerate;

                if (count == 1)
                {
                    if (!lastPicked.ContainsKey(typeKey))
                    {
                        lastPicked[typeKey] = false;
                    }
                    if (lastPicked[typeKey])
                    {
                        amountToPick = 0;
                        amountToRandomlyGenerate = 1;
                    }
                    lastPicked[typeKey] = !lastPicked[typeKey];
                }

                List<ResearchProjectDef> selectedProjectsFirstHalf = allAvailableProjects.Take(amountToRandomlyGenerate).ToList();

                if (SemiRandomResearchMod.settings.allowOneHigherTechProject && randomProject != null && !selectedProjectsFirstHalf.Contains(randomProject) && amountToRandomlyGenerate > 0)
                {
                    selectedProjectsFirstHalf[0] = randomProject;
                }

                selectedProjects.AddRange(selectedProjectsFirstHalf);

                if (amountToPick > 0)
                {
                    float averageAvailableCost = allAvailableProjects.Select(x => x.CostApparent).Sum() / allAvailableProjects.Count();
                    float averageCurrentCost = (currentAvailableProjects.Select(x => x.CostApparent).Sum() + selectedProjectsFirstHalf.Select(x => x.CostApparent).Sum() + selectedProjects.Sum(x => x.CostApparent))
                        / Math.Max(currentAvailableProjects.Count + selectedProjects.Count + selectedProjectsFirstHalf.Count, 1);
                    float targetAddedAverageCost = ((averageAvailableCost * (currentAvailableProjects.Count + count))
                        - (currentAvailableProjects.Count + selectedProjectsFirstHalf.Count) * averageCurrentCost) / (amountToPick);
                    allAvailableProjects = allAvailableProjects.Where(x => !selectedProjectsFirstHalf.Contains(x));

                    if (SemiRandomResearchMod.settings.verboseLogging)
                    {
                        LogIfNewMessage("equalizeCostPick1" + typeKey, $"Picking projects to equalize: Average research cost of all still available projects: {averageAvailableCost} \nAverage cost of the randomly selected projects: {averageCurrentCost}  \nTarget that the other projects added should have on average: {targetAddedAverageCost} \nThere were {amountToRandomlyGenerate} projects selected randomly. \nBefore adding projects there were {currentAvailableProjects.Count} already in the list. \nThere will be picked {amountToPick} projects.");
                    }

                    IEnumerable<ResearchProjectDef> bestSelectedProjects = new List<ResearchProjectDef>();
                    float bestAverage = float.MaxValue;
                    for (int i = 0; i < 25; i++)
                    {
                        allAvailableProjects = allAvailableProjects.InRandomOrder();
                        IEnumerable<ResearchProjectDef> iterSelectedProjects = allAvailableProjects.Take(Math.Min(amountToPick, allAvailableProjects.Count()));
                        float actualAverage = iterSelectedProjects.Select(x => x.CostApparent).Sum() / iterSelectedProjects.Count();
                        if (Math.Abs(bestAverage - targetAddedAverageCost) > Math.Abs(actualAverage - targetAddedAverageCost))
                        {
                            bestAverage = actualAverage;
                            bestSelectedProjects = iterSelectedProjects;
                        }
                    }
                    selectedProjects.AddRange(bestSelectedProjects);

                    if (SemiRandomResearchMod.settings.verboseLogging)
                    {
                        LogIfNewMessage("equalizeCostPick2" + typeKey, $"Total cost of picked projects: {bestSelectedProjects.Select(x => x.CostApparent).Sum()} ");
                    }
                }
                else if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("equalizeCostNoPick" + typeKey, $"[There were {amountToRandomlyGenerate} projects selected randomly as part of cost equalization");
                }
            }
            else
            {
                selectedProjects.AddRange(allAvailableProjects.Take(Math.Min(count, allAvailableProjects.Count())));

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("selectCount" + typeKey, $"There were {selectedProjects.Count} projects selected randomly");
                }

                if (SemiRandomResearchMod.settings.allowOneHigherTechProject && randomProject != null && !selectedProjects.Contains(randomProject))
                {
                    if (selectedProjects.Count < count || selectedProjects.Count < 1)
                    {
                        selectedProjects.Add(randomProject);
                    }
                    else
                    {
                        selectedProjects[0] = randomProject;
                    }
                }
            }
            selectedProjects.Shuffle();
            int selectedProjectsCount = selectedProjects.Count;
            selectedProjects = selectedProjects.OrderByDescending(x => partiallyCompleted.Contains(x)).Distinct().ToList();
            if (selectedProjects.Count != selectedProjectsCount)
                LogIfNewMessage("Distinct error" + typeKey, $"There were {selectedProjects.Count} projects after distinct but {selectedProjectsCount} before.");
            return selectedProjects;
        }

        // ==============================================================================
        // NEW STRING-BASED TRACKING
        // ==============================================================================

        public void SetCurrentProjectByKey(ResearchProjectDef newCurrentProject, string typeKey)
        {
            loggedMessages.Clear();
            currentProjects = currentProjects.Where(x => GetCategoryKey(x) != typeKey).ToList();
            projectDefsCacheByType.Remove(typeKey);
            if (newCurrentProject != null)
            {
                currentProjects.Add(newCurrentProject);
                Find.ResearchManager.SetCurrentProject(newCurrentProject);

                if (!SemiRandomResearchMod.settings.featureEnabled && !currentAvailableProjects.Contains(newCurrentProject))
                    currentAvailableProjects.Add(newCurrentProject);

                if (SemiRandomResearchMod.settings.rerollAllEveryTime && !SemiRandomResearchMod.settings.allowSwitchingResearch)
                    currentAvailableProjects = currentAvailableProjects.Where(projectDef => GetCategoryKey(projectDef) != typeKey || projectDef == newCurrentProject).ToList();
            }
            else
            {
                ResearchProjectDef active = null;
                ResearchProjectDef standardActive = Find.ResearchManager.GetProject(null);
                if (standardActive != null && GetCategoryKey(standardActive) == typeKey) active = standardActive;
                foreach (var cat in DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading)
                {
                    ResearchProjectDef catActive = Find.ResearchManager.GetProject(cat);
                    if (catActive != null && GetCategoryKey(catActive) == typeKey) active = catActive;
                }
                if (active != null) Find.ResearchManager.StopProject(active);
            }
            currentProjectDefsCacheByType[typeKey] = currentProjects.Where(x => GetCategoryKey(x) == typeKey).ToList();
        }

        public void ManageNotChosenByKey(string typeKey)
        {
            if (SemiRandomResearchMod.settings.reofferAfterAmountOfRerolls == 0)
            {
                notChosenProjects.Clear();
            }
            else
            {
                if (!currentRerollState.ContainsKey(typeKey))
                {
                    currentRerollState[typeKey] = 0;
                }
                currentRerollState[typeKey]++;
                foreach (ResearchProjectDef rdef in currentAvailableProjects.Where(x => GetCategoryKey(x) == typeKey))
                {
                    if (!notChosenProjects.ContainsKey(rdef))
                    {
                        notChosenProjects.Add(rdef, currentRerollState[typeKey]);
                    }
                    else
                    {
                        notChosenProjects[rdef] = currentRerollState[typeKey];
                    }
                }
                notChosenProjects = notChosenProjects.Where(x => x.Value > currentRerollState[typeKey] - SemiRandomResearchMod.settings.reofferAfterAmountOfRerolls).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }

        public void SetRerolledByKey(string typeKey, bool newValue)
        {
            if (!rerolled.ContainsKey(typeKey))
            {
                rerolled.Add(typeKey, newValue);
            }
            else
            {
                rerolled[typeKey] = newValue;
            }
        }

        public bool CanRerollByKey(string typeKey)
        {
            return SemiRandomResearchMod.settings.allowManualReroll == ManualReroll.Always ||
                (SemiRandomResearchMod.settings.allowManualReroll == ManualReroll.Once && (!rerolled.ContainsKey(typeKey) || !rerolled[typeKey]));
        }

        public void RerollByKey(string typeKey)
        {
            if (GetCurrentlyAvailableProjects().Any(x => GetCategoryKey(x) == typeKey))
            {
                SetRerolledByKey(typeKey, true);
                ManageNotChosenByKey(typeKey);
                SetCurrentProjectByKey(null, typeKey);
                currentAvailableProjects = currentAvailableProjects.Where(x => GetCategoryKey(x) != typeKey).ToList();
                additionalAvailableProjects = additionalAvailableProjects.Where(x => GetCategoryKey(x) != typeKey).ToHashSet();
                GetCurrentlyAvailableProjects();
                tickCounter = 0;
            }
        }

        // ==============================================================================
        // COMPATIBILITY WRAPPERS FOR UI BUTTONS
        // These intercept calls from your UI and route them to the Pseudo-Categories
        // ==============================================================================

        public void SetCurrentProject(ResearchProjectDef newCurrentProject, KnowledgeCategoryDef type)
        {
            if (newCurrentProject != null)
            {
                SetCurrentProjectByKey(newCurrentProject, GetCategoryKey(newCurrentProject));
            }
            else
            {
                ResearchProjectDef active = Find.ResearchManager.GetProject(type);
                if (active != null) SetCurrentProjectByKey(null, GetCategoryKey(active));
            }
        }

        public void ManageNotChosen(KnowledgeCategoryDef type)
        {
            string key = type == null ? "Standard" : type.defName;
            ManageNotChosenByKey(key);
        }

        public void SetRerolled(KnowledgeCategoryDef type, bool newValue)
        {
            string key = type == null ? "Standard" : type.defName;
            SetRerolledByKey(key, newValue);
        }

        public bool CanReroll(KnowledgeCategoryDef type)
        {
            if (type == null) return CanRerollByKey("Standard") || CanRerollByKey("Gravship");
            return CanRerollByKey(type.defName);
        }

        public void Reroll(KnowledgeCategoryDef type)
        {
            if (type == null)
            {
                RerollByKey("Standard");
                RerollByKey("Gravship");
                RerollByKey("Divinitech");
            }
            else
            {
                RerollByKey(type.defName);
            }
        }

        // ==============================================================================

        public void SettingsChanged()
        {
            ForceAutoReseachCheckNextTick();
            loggedMessages.Clear();
        }

        public void ForceAutoReseachCheckNextTick()
        {
            tickCounter = 0;
            additionalProjectsRefresh = true;
        }

        public void ConsiderProjectFinished(ResearchProjectDef def)
        {
            if (def.IsDummyResearch())
            {
                return;
            }

            if (SemiRandomResearchMod.settings.verboseLogging)
            {
                LogIfNewMessage("Consider Completed", def?.LabelCap);
            }

            string typeKey = GetCategoryKey(def);

            SetRerolledByKey(typeKey, false);
            ForceAutoReseachCheckNextTick();

            // Clear current project
            if (currentProjects.Contains(def))
            {
                SetCurrentProjectByKey(null, typeKey);
            }

            // Immediately handle reroll
            if (SemiRandomResearchMod.settings.rerollAllEveryTime)
            {
                ManageNotChosenByKey(typeKey);
                currentAvailableProjects = currentAvailableProjects.Where(x => GetCategoryKey(x) != typeKey).ToList();
                additionalAvailableProjects = additionalAvailableProjects.Where(x => GetCategoryKey(x) != typeKey).ToHashSet();
                GetCurrentlyAvailableProjects();
            }
        }

        public void AddProjectToAvailableProjects(ResearchProjectDef rdef)
        {
            additionalAvailableProjects.Add(rdef);
            additionalProjectsRefresh = true;
        }

        private void LogIfNewMessage(string key, string message)
        {
            if (!loggedMessages.ContainsKey(key) || loggedMessages[key] != message)
            {
                Log.Message($"[CM_Semi_Random_Research] <{key}>: {message}");
                loggedMessages[key] = message;
            }
        }
    }
}