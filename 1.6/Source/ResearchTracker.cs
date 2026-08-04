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
        private Dictionary<ResearchProjectDef,int> notChosenProjects = new Dictionary<ResearchProjectDef, int>();
        private Dictionary<string, int> currentRerollState = new Dictionary<string, int>();
        private List<ResearchProjectDef> currentProjects = new List<ResearchProjectDef>();
        private HashSet<ResearchProjectDef> additionalAvailableProjects = new HashSet<ResearchProjectDef>();
        private HashSet<KnowledgeCategoryDef> pendingResearchRerolls = new HashSet<KnowledgeCategoryDef>();

        public List<ResearchProjectDef> CurrentProject => currentProjects;

        public bool autoResearch = false;

        private Dictionary<string,bool> rerolled = new Dictionary<string, bool>();
        private Dictionary<string, List<ResearchProjectDef>> projectDefsCacheByType = new Dictionary<string, List<ResearchProjectDef>>();
        private Dictionary<string, List<ResearchProjectDef>> currentProjectDefsCacheByType = new Dictionary<string, List<ResearchProjectDef>>();
        private HashSet<string> completedTypes = new HashSet<string>();

        private int tickCounter = 0;
        private int tickShortOffset = 10;
        private int tickOffset = 360;
        private int previousDefCount = 0;
        private bool additionalProjectsRefresh = true;

        //Whether the previous offered research was picked based on equalize cost calculations (true) or chosen randomly (false)
        private Dictionary<string, bool> lastPicked = new Dictionary<string, bool>();

        private Dictionary<string, string> loggedMessages = new Dictionary<string, string>();

        private List<KnowledgeCategoryDef> all_types;

        public ResearchTracker(World world) : base(world)
        {
            previousDefCount = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count;

            all_types = DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading.ListFullCopy();
            all_types.Add(null);
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

            if(notChosenProjects == null) 
            {
                notChosenProjects = new Dictionary<ResearchProjectDef, int>();
            }

            if (currentProjects == null)
            {
                currentProjects = new List<ResearchProjectDef>();
            }

            if (currentAvailableProjects == null)
            {
                currentAvailableProjects = new List<ResearchProjectDef>();
            }

            if(additionalAvailableProjects == null)
            {
                additionalAvailableProjects = new HashSet<ResearchProjectDef>();
            }

            if (SemiRandomResearchMod.settings.verboseLogging)
            {
                string allCurrentProjects = "";
                foreach(ResearchProjectDef def in currentProjects)
                {
                    allCurrentProjects += def != null ? def.LabelCap.RawText : "Null";
                    allCurrentProjects += " ";
                }
                LogIfNewMessage("Loaded Current Projects", allCurrentProjects);

                string allAvailableProjects = "";
                foreach (ResearchProjectDef def in currentAvailableProjects)
                {
                    allAvailableProjects += def != null ? def.LabelCap.RawText : "Null";
                    allAvailableProjects += " ";
                }
                LogIfNewMessage("Loaded Available Projects", allAvailableProjects);

            }

            Scribe_Collections.Look(ref rerolled, "rerolled");

            if (rerolled == null)
            {
                rerolled = new Dictionary<string,bool>();
            }

            Scribe_Collections.Look(ref currentRerollState, "currentRerollState");

            if (currentRerollState == null)
            {
                currentRerollState = new Dictionary<string, int>();
            }

            Scribe_Values.Look(ref autoResearch, "autoResearch", false);

            Scribe_Collections.Look(ref lastPicked, "lastPicked");

            if (lastPicked == null)
            {
                lastPicked = new Dictionary<string, bool>();
            }

            Scribe_Collections.Look(ref pendingResearchRerolls, "pendingResearchRerolls", LookMode.Def);

            if (pendingResearchRerolls == null)
            {
                pendingResearchRerolls = new HashSet<KnowledgeCategoryDef>();
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            // Check if research is done
            // We don't need this EVERY tick, but it should be somewhat frequent
            if ((tickCounter % tickShortOffset) == 0)
            {
                foreach (KnowledgeCategoryDef type in all_types)
                {
                    string typeKey = type == null ? "null" : type.defName;
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
                            List<ResearchProjectDef> possibleProjectsOfType = GetCurrentlyAvailableProjects().Where(x => x.knowledgeCategory == type).ToList();

                            if (!possibleProjectsOfType.Empty())
                            {
                                SetCurrentProject(possibleProjectsOfType.First(), type);
                                currentProjectOfType = currentProjects.Where(x => x.knowledgeCategory == type).ToList();
                            }
                        }
                    }

                    if ((tickCounter % tickOffset) == 0)
                    {
                        // This is a fairly expensive call
                        ResearchProjectDef activeProject = Find.ResearchManager.GetProject(type);

                        // This should not be required, but there are some mods that stop the current research. This restores them.  
                        if (activeProject == null && !currentProjectOfType.Empty() && currentProjectOfType.First().CanStartNow)
                        {
                            SetCurrentProject(currentProjectOfType.First(), type);
                        }
                        else if (activeProject != null && (currentProjectOfType.Empty() || !currentProjectOfType.Contains(activeProject)) && activeProject.CanStartNow)
                        {
                            if (!SemiRandomResearchMod.settings.featureEnabled)
                            {
                                SetCurrentProject(activeProject, type);
                            }
                            else if (currentProjectOfType.Empty() && currentAvailableProjects.Contains(activeProject))
                            {
                                SetCurrentProject(activeProject, type);
                            }
                            else if (!currentProjectOfType.Empty())
                            {
                                SetCurrentProject(currentProjectOfType.First(), type);
                            }
                            else
                            {
                                LogIfNewMessage("WorldTickUnexpectedState" + type, $"Error? Set as activeProject: {activeProject.LabelCap} currentAvailableProjects: {currentAvailableProjects.Count} and of type {type}: {currentAvailableProjects.Where(x => x.knowledgeCategory == type).Count()}");
                                SetCurrentProject(activeProject, type);
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
            List<KnowledgeCategoryDef> all_types = DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading.ListFullCopy();
            all_types = all_types.Prepend(null).ToList();
            List<ResearchProjectDef> result = new List<ResearchProjectDef>();
            SemiRandomResearchMod.settings.DumpSettingToLog(); //Only dumps the setting if verbose logging is enabled and then only once.
            foreach (KnowledgeCategoryDef type in all_types)
            {
                if(!SemiRandomResearchMod.settings.experimentalAnomalySupport && type != null)
                {
                    continue;
                }

                currentAvailableProjects = currentAvailableProjects.Where(projectDef => projectDef != null && 
                !projectDef.IsFinished &&
                !projectDef.IsHidden &&
                Compatibility.SatisfiesAlienRaceRestriction(projectDef)).ToList();
                List<ResearchProjectDef> currentAvailableValidProjectsOfType = currentAvailableProjects.Where(x => x.knowledgeCategory == type && x.CanStartNow).ToList();
                List<ResearchProjectDef> currentProjectOfType = currentProjects.Where(x => x.knowledgeCategory == type).ToList();

                if (!SemiRandomResearchMod.settings.rerollAllEveryTime ||
                    SemiRandomResearchMod.settings.allowSwitchingResearch ||
                    currentProjectOfType.Empty() ||
                    currentProjectOfType.Any(x=>x.IsFinished || !Compatibility.SatisfiesAlienRaceRestriction(x)))
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
                        List<ResearchProjectDef> nextProjects = GetResearchableProjects(numberOfMissingProjects, type);

                        if (!nextProjects.NullOrEmpty())
                        {
                            currentAvailableProjects.AddRange(nextProjects);
                            currentAvailableProjects = currentAvailableProjects.Distinct().ToList(); // Do a distinct check. Should not be required, but better be sure.
                            currentAvailableValidProjectsOfType.AddRange(nextProjects);
                            currentAvailableValidProjectsOfType = currentAvailableValidProjectsOfType.Distinct().ToList();
                            handledProjects = true;
                            result.AddRange(currentAvailableValidProjectsOfType);
                        }
                        // There may be added more projects than requested in some cased, e.g. when progressAddsChoice != None, so update that value
                        numberOfMissingProjects = Math.Min((SemiRandomResearchMod.settings.availableProjectCount + additionalProjects), SemiRandomResearchMod.settings.maxProjectCount) - currentAvailableValidProjectsOfType.Count;
                    }
                    int projectsAddedAdditional = currentAvailableValidProjectsOfType.Count(x => additionalAvailableProjects.Contains(x));
                    int progressAddedProgressed = currentAvailableValidProjectsOfType.Count(x => x.ProgressReal > 0 && !currentProjectOfType.Contains(x) && !additionalAvailableProjects.Contains(x));
                    int extraAddedProgress = SemiRandomResearchMod.settings.progressAddsChoice == ProgressAddsChoice.AddChoice ? progressAddedProgressed: 0;
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

                        if (!currentProjectOfType.Empty() && currentProjectOfType.Any(x=>!x.IsFinished && Compatibility.SatisfiesAlienRaceRestriction(x)))
                        {
                            currentAvailableProjectsWithoutCurrentProject.AddRange(currentProjectOfType);
                        }
                        //return the modified list instead of updating the original list to prevent players from cheesing the mechanic using cryptosleep capsules
                        handledProjects = true;
                        result.AddRange(currentAvailableProjectsWithoutCurrentProject);
                        if (SemiRandomResearchMod.settings.verboseLogging)
                            LogIfNewMessage("numberOfMissingProjects < 0" + type, $"More projects available than expected. numberOfMissingProjects: {numberOfMissingProjects} Values: additionalProjects {additionalProjects} amountToRemove: {amountToRemove} keepable.Count: {keepable.Count()} extraAddedProgress: {extraAddedProgress} projectsAddedAdditional:{projectsAddedAdditional}" );

                    }
                    if (! handledProjects)
                    {
                        if (SemiRandomResearchMod.settings.verboseLogging && currentAvailableValidProjectsOfType.Count == 0)
                            LogIfNewMessage("numberOfMissingProjects = 0" + type, $"No projects are to be added even though non are available?Values: additionalProjects {additionalProjects} extraAddedProgress: {extraAddedProgress} projectsAddedAdditional:{projectsAddedAdditional}");

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

        private List<ResearchProjectDef> GetResearchableProjects(int count, KnowledgeCategoryDef type)
        {
            string typeKey = type == null ? "null" : type.defName;

            if (completedTypes.Contains(typeKey) &&
                previousDefCount == DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count)
            {
                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("Skipping" + type, "Type Completed");
                }

                return new List<ResearchProjectDef>();
            }

            TechLevel maxCurrentProjectTechlevel = TechLevel.Archotech;
            // Get the max tech level of projects already in the offered list
            if (currentAvailableProjects.Count > 0)
                maxCurrentProjectTechlevel = currentAvailableProjects.Select(projectDef => projectDef.techLevel).Max();
            TechLevel minCurrentProjectTechlevel = TechLevel.Archotech;
            // Get the min tech level of projects already in the offered list
            if (currentAvailableProjects.Count > 0)
                minCurrentProjectTechlevel = currentAvailableProjects.Select(projectDef => projectDef.techLevel).Min();

            if(!projectDefsCacheByType.ContainsKey(typeKey) ||
                previousDefCount == DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count)
            {
                projectDefsCacheByType[typeKey] = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where((ResearchProjectDef projectDef) => !projectDef.IsFinished &&
                projectDef.knowledgeCategory == type).ToList();

                if(!projectDefsCacheByType[typeKey].Any())
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

                    LogIfNewMessage("NoAvailableProjects1" + type, $"[CM_Semi_Random_Research] Total projects in game: {allAvailableProjectsDebug.Count}");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => projectDef.CanStartNow).ToList();
                    LogIfNewMessage("NoAvailableProjects2" + type, $"[CM_Semi_Random_Research] Of which { allAvailableProjectsDebug.Count} Could be started now");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => Compatibility.SatisfiesAlienRaceRestriction(projectDef)).ToList();
                    LogIfNewMessage("NoAvailableProjects3" + type, $"[CM_Semi_Random_Research] Of which { allAvailableProjectsDebug.Count} you have the required races for (Humanoid Alien Races Compability Check)");
                    allAvailableProjectsDebug = allAvailableProjectsDebug.Where((ResearchProjectDef projectDef) => ! projectDef.IsDummyResearch()).ToList();
                    LogIfNewMessage("NoAvailableProjects4" + type, $"[CM_Semi_Random_Research] Of which { allAvailableProjectsDebug.Count} are not Dummy researches");
                }
            }

            // Get a random project if we are allowed to have one ignoring the restrictions.
            ResearchProjectDef randomProject=null;
            if (allAvailableProjects.Any() && SemiRandomResearchMod.settings.allowOneHigherTechProject && 
                (!SemiRandomResearchMod.settings.restrictToFactionTechLevel || maxCurrentProjectTechlevel<= Faction.OfPlayer.def.techLevel) && 
                (!SemiRandomResearchMod.settings.forceLowestTechLevel || maxCurrentProjectTechlevel == minCurrentProjectTechlevel))
            {
                randomProject = allAvailableProjects.RandomElement();
            }

            // If setting is enabled, block techs beyond player faction's tech level
            if (SemiRandomResearchMod.settings.restrictToFactionTechLevel)
            {
                TechLevel maxTechLevel = Faction.OfPlayer.def.techLevel;
                allAvailableProjects = allAvailableProjects.Where(projectDef => projectDef.techLevel <= maxTechLevel).ToList();

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("AfterRestrictToFactionTechLevel"+type, "Currently possible projects after restrictToFactionTechLevel: " + allAvailableProjects.Count());
                }

            }

            // Force completing lowest level if setting is enabled
            if (allAvailableProjects.Any() && SemiRandomResearchMod.settings.forceLowestTechLevel)
            {
                // Go through each tech level and select from lowest available
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
                    LogIfNewMessage("AfterForceLowestTechLevel" + type, "Currently possible projects after forceLowestTechLevel: " + allAvailableProjects.Count());
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
                    LogIfNewMessage("ReofferAfterAmountOfRerollsCount" + type, "This many researches were not offered recently: " + possibleNotShownRecently.Count + " while this many were shown recently: " + notChosenProjects.Keys.Count(x => x.knowledgeCategory == type && !x.IsFinished));
                }
                int remainingCount = count;

                if (possibleNotShownRecently.Count < count)
                {
                    if (SemiRandomResearchMod.settings.verboseLogging)
                    {
                        LogIfNewMessage("PossibleNotShownRecently" + type, "Picking from recently shown researches this many projects: " + (count - possibleNotShownRecently.Count));
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
                        / Math.Max(currentAvailableProjects.Count + selectedProjects.Count + selectedProjectsFirstHalf.Count,1);
                    float targetAddedAverageCost = ((averageAvailableCost * (currentAvailableProjects.Count+count)) 
                        - (currentAvailableProjects.Count + selectedProjectsFirstHalf.Count) * averageCurrentCost)/(amountToPick); 
                    allAvailableProjects = allAvailableProjects.Where(x => !selectedProjectsFirstHalf.Contains(x));

                    if (SemiRandomResearchMod.settings.verboseLogging)
                    {
                       LogIfNewMessage("equalizeCostPick1" + type, $"Picking projects to equalize: Average research cost of all still available projects: {averageAvailableCost} \nAverage cost of the randomly selected projects: {averageCurrentCost}  \nTarget that the other projects added should have on average: {targetAddedAverageCost} \nThere were {amountToRandomlyGenerate} projects selected randomly. \nBefore adding projects there were {currentAvailableProjects.Count} already in the list. \nThere will be picked {amountToPick} projects.");
                    }

                    IEnumerable<ResearchProjectDef> bestSelectedProjects = new List<ResearchProjectDef>();
                    float bestAverage = float.MaxValue;
                    for(int i = 0; i < 25; i++)
                    {
                        allAvailableProjects = allAvailableProjects.InRandomOrder(); // Pretty bad performance wise. Is there a better option
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
                        LogIfNewMessage("equalizeCostPick2" + type, $"Total cost of picked projects: {bestSelectedProjects.Select(x => x.CostApparent).Sum()} ");
                    }
                }
                else if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("equalizeCostNoPick" + type, $"[There were {amountToRandomlyGenerate} projects selected randomly as part of cost equalization");
                }
            }
            else
            {
                selectedProjects.AddRange(allAvailableProjects.Take(Math.Min(count, allAvailableProjects.Count())));

                if (SemiRandomResearchMod.settings.verboseLogging)
                {
                    LogIfNewMessage("selectCount" + type, $"There were {selectedProjects.Count} projects selected randomly");
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
            if(selectedProjects.Count != selectedProjectsCount)
                LogIfNewMessage("Distinct error" + type, $"There were {selectedProjects.Count} projects after distinct but {selectedProjectsCount} before.");
            return selectedProjects;
        }

        public void SetCurrentProject(ResearchProjectDef newCurrentProject, KnowledgeCategoryDef type)
        {
            string typeKey = type == null ? "null" : type.defName;
            loggedMessages.Clear();
            currentProjects = currentProjects.Where(x => x.knowledgeCategory != type).ToList();
            projectDefsCacheByType.Remove(typeKey);
            if (newCurrentProject!=null)
            {
                currentProjects.Add(newCurrentProject);
                Find.ResearchManager.SetCurrentProject(newCurrentProject);

                if (!SemiRandomResearchMod.settings.featureEnabled && !currentAvailableProjects.Contains(newCurrentProject))
                    currentAvailableProjects.Add(newCurrentProject);

                if (SemiRandomResearchMod.settings.rerollAllEveryTime && !SemiRandomResearchMod.settings.allowSwitchingResearch)
                    currentAvailableProjects = currentAvailableProjects.Where(projectDef => projectDef.knowledgeCategory != type || projectDef == newCurrentProject).ToList();

            }
            else if(Find.ResearchManager.GetProject(type) != null)
            {
                Find.ResearchManager.StopProject(Find.ResearchManager.GetProject(type));
            }
            currentProjectDefsCacheByType[typeKey] = currentProjects.Where(x => x.knowledgeCategory == type).ToList();
        }

        public void ManageNotChosen(KnowledgeCategoryDef type)
        {
            if(SemiRandomResearchMod.settings.reofferAfterAmountOfRerolls == 0)
            {
                notChosenProjects.Clear();
            }
            else 
            {
                string key = type == null ? "null" : type.defName;
                if (!currentRerollState.ContainsKey(key))
                {
                    currentRerollState[key] = 0;
                }
                currentRerollState[key]++;
                foreach (ResearchProjectDef rdef in currentAvailableProjects)
                {
                    if (!notChosenProjects.ContainsKey(rdef))
                    {
                        notChosenProjects.Add(rdef, currentRerollState[key]);
                    }
                    else 
                    {
                        notChosenProjects[rdef] = currentRerollState[key];
                    }
                }
                notChosenProjects = notChosenProjects.Where(x => x.Value > currentRerollState[key] - SemiRandomResearchMod.settings.reofferAfterAmountOfRerolls).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }

        public void SetRerolled(KnowledgeCategoryDef type, bool newValue)
        {
            string key = type == null ? "null" : type.defName;
            if (!rerolled.ContainsKey(key))
            {
                rerolled.Add(key, newValue);
            }
            else 
            {
                rerolled[key] = newValue;
            }
        }

        public bool CanReroll(KnowledgeCategoryDef type)
        {
            string key = type == null ? "null" : type.defName;

            return SemiRandomResearchMod.settings.allowManualReroll == ManualReroll.Always ||
                (SemiRandomResearchMod.settings.allowManualReroll == ManualReroll.Once && (!rerolled.ContainsKey(key) || !rerolled[key]));
        }

        public void Reroll(KnowledgeCategoryDef type)
        {
            // If there are no researches of that type rerolling wont change anything so lets the player should not spent the reroll
            if (GetCurrentlyAvailableProjects().Any(x=>x.knowledgeCategory == type)) 
            {
                SetRerolled(type, true);
                ManageNotChosen(type);
                SetCurrentProject(null, type);
                currentAvailableProjects = currentAvailableProjects.Where(x => x.knowledgeCategory != type).ToList();
                additionalAvailableProjects = additionalAvailableProjects.Where(x => x.knowledgeCategory != type).ToHashSet();
                GetCurrentlyAvailableProjects();
                tickCounter = 0;
            }
        }

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
            if(def.IsDummyResearch())
            {
                return;
            }

            if (SemiRandomResearchMod.settings.verboseLogging)
            {
                LogIfNewMessage("Consider Completed", def?.LabelCap);
            }

            SetRerolled(def.knowledgeCategory, false);
            ForceAutoReseachCheckNextTick();
            
            // Clear current project
            if(currentProjects.Contains(def))
            {
                SetCurrentProject(null, def.knowledgeCategory);
            }

            // Immediately handle reroll
            if(SemiRandomResearchMod.settings.rerollAllEveryTime)
            {
                ManageNotChosen(def.knowledgeCategory);
                currentAvailableProjects = currentAvailableProjects.Where(x => 
                    x.knowledgeCategory != def.knowledgeCategory).ToList();
                additionalAvailableProjects = additionalAvailableProjects.Where(x => 
                    x.knowledgeCategory != def.knowledgeCategory).ToHashSet();
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
