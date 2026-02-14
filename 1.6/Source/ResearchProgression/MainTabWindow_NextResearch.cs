using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;

namespace CM_Semi_Random_Research
{
    [StaticConstructorOnStartup]
    public class MainTabWindow_NextResearch : MainTabWindow
    {
        protected ResearchProjectDef selectedProject;

        protected override float Margin => 6f;

        private float betweenColumnSpace => 24f;

        private Vector2 leftScrollPosition = Vector2.zero;

        private float leftScrollViewHeight;

        private Vector2 rightScrollPosition = Vector2.zero;

        private float rightScrollViewHeight;

        private static readonly Color FulfilledPrerequisiteColor = Color.green;

        private static readonly Texture2D ResearchBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.8f, 0.85f));

        private static readonly Texture2D ResearchBarBGTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.1f, 0.1f, 0.1f));

        private static readonly Color ActiveProjectLabelColor = new ColorInt(219, 201, 126, 255).ToColor;

        private Dictionary<ResearchProjectDef, List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>>> cachedUnlockedDefsGroupedByPrerequisites;

        private static List<Building> tmpAllBuildings = new List<Building>();

        private int currentRandomSeed = 0;

        bool errorDetected = false;

        private KnowledgeCategoryDef rerollButtonType = null;

        private Dictionary<string, float> animationProgress = new Dictionary<string, float>();
        private float lastRerollTime = -1f;
        private const float ANIMATION_DURATION = 0.5f; // Half second per item
        private const float ITEM_DELAY = 0.15f; // Very short delay between items
        private List<string> animationOrder = new List<string>(); // Track animation order by defName
        private List<ResearchProjectDef> previousProjects = new List<ResearchProjectDef>(); // Add this line

        private Dictionary<TechLevel, float> techLevelHeaderProgress = new Dictionary<TechLevel, float>();

        // Check if anomaly exists and if the setting is enabled
        // Lambda to refresh setting based on settings toggle
        private Func<bool> AnomalyContentEnabled = () => (KnowledgeCategoryDefOf.Basic != null) && SemiRandomResearchMod.settings.experimentalAnomalySupport;

        private bool ColonistsHaveResearchBench
        {
            get
            {
                bool result = false;
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i].listerBuildings.ColonistsHaveResearchBench())
                    {
                        result = true;
                        break;
                    }
                }
                return result;
            }
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth * 0.585f, UI.screenHeight * 0.7f);

        public List<ResearchProjectDef> currentAvailableProjects = new List<ResearchProjectDef>();

        public MainTabWindow_NextResearch()
        {

        }

        public override void PreOpen()
        {
            base.PreOpen();
            
            currentRandomSeed = Rand.Int;

            ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();

            if (researchTracker != null)
            {
                // Store current projects for animation reference
                if (currentAvailableProjects.Count > 0)
                    previousProjects = currentAvailableProjects.ListFullCopy();
                else
                    previousProjects.Clear();
                
                researchTracker.WorldComponentTick();
                
                // Get fresh list of available projects
                currentAvailableProjects = researchTracker.GetCurrentlyAvailableProjects();
                
                foreach(ResearchProjectDef def in researchTracker.CurrentProject.ToList())
                {
                    if (!Compatibility.SatisfiesAlienRaceRestriction(def))
                    {
                        researchTracker.SetCurrentProject(null, def.knowledgeCategory);
                    }
                }
                selectedProject = researchTracker.CurrentProject.FirstOrFallback(null);
                
                // Use the same sorting logic as the reroll animation
                animationOrder.Clear();
                var groupedProjects = currentAvailableProjects
                    .GroupBy(proj => proj.techLevel)
                    .OrderBy(group => (int)group.Key);
                    
                foreach (var techGroup in groupedProjects)
                {
                    foreach (ResearchProjectDef projectDef in techGroup.OrderBy(p => p.CostApparent))
                    {
                        animationOrder.Add(projectDef.defName);
                        // Set to fully visible immediately
                        animationProgress[projectDef.defName] = 1f;
                    }
                }
                
                // Set all tech level headers to fully visible
                foreach (TechLevel techLevel in Enum.GetValues(typeof(TechLevel)))
                {
                    techLevelHeaderProgress[techLevel] = 1f;
                }
            }

            cachedUnlockedDefsGroupedByPrerequisites = null;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();

            if (researchTracker != null)
            {
                // Check if we need to update the available projects
                bool shouldUpdateProjects = false;
                List<ResearchProjectDef> newProjects = researchTracker.GetCurrentlyAvailableProjects();
                
                // If counts differ, definitely need to update
                if (newProjects.Count != currentAvailableProjects.Count)
                {
                    shouldUpdateProjects = true;
                }
                else
                {
                    // Check for any difference in content
                    foreach (var project in newProjects)
                    {
                        if (!currentAvailableProjects.Contains(project))
                        {
                            shouldUpdateProjects = true;
                            break;
                        }
                    }
                }
                
                // If projects have changed, start animation
                if (shouldUpdateProjects)
                {
                    // Get the new projects and sort them properly
                    currentAvailableProjects = newProjects;
                    
                    // Determine display order - exactly as they would be displayed
                    animationOrder.Clear();
                    
                    // Group and sort exactly as in DrawLeftColumn
                    var groupedProjects = currentAvailableProjects
                        .GroupBy(proj => proj.techLevel)
                        .OrderBy(group => (int)group.Key);
                        
                    foreach (var techGroup in groupedProjects)
                    {
                        foreach (ResearchProjectDef projectDef in techGroup.OrderBy(p => p.CostApparent))
                        {
                            animationOrder.Add(projectDef.defName);
                            
                            // Reset animation progress for this item
                            animationProgress[projectDef.defName] = 0f;
                        }
                    }
                    
                    // Start animation timer
                    lastRerollTime = Time.realtimeSinceStartup;
                    
                    // Reset tech level header animations
                    foreach (TechLevel techLevel in Enum.GetValues(typeof(TechLevel)))
                    {
                        techLevelHeaderProgress[techLevel] = 0f;
                    }
                }
                
                // Process animation if active
                if (lastRerollTime > 0f)
                {
                    float timeSinceReroll = Time.realtimeSinceStartup - lastRerollTime;
                    bool allComplete = true;
                    
                    // Calculate animation for each item
                    for (int i = 0; i < animationOrder.Count; i++)
                    {
                        string defName = animationOrder[i];
                        
                        // When should this item start and finish appearing
                        float startTime = i * ITEM_DELAY;
                        float endTime = startTime + ANIMATION_DURATION;
                        
                        // Calculate progress for this item
                        if (timeSinceReroll >= startTime && timeSinceReroll <= endTime)
                        {
                            // Item is currently fading in
                            float itemProgress = (timeSinceReroll - startTime) / ANIMATION_DURATION;
                            animationProgress[defName] = itemProgress;
                            allComplete = false;
                        }
                        else if (timeSinceReroll < startTime)
                        {
                            // Item hasn't started appearing yet
                            animationProgress[defName] = 0f;
                            allComplete = false;
                        }
                        else
                        {
                            // Item has finished appearing
                            animationProgress[defName] = 1f;
                        }
                    }
                    
                    // Update tech level headers
                    var groupedByTechLevel = currentAvailableProjects
                        .GroupBy(p => p.techLevel)
                        .ToDictionary(g => g.Key, g => g.ToList());
                        
                    foreach (var techGroup in groupedByTechLevel)
                    {
                        TechLevel techLevel = techGroup.Key;
                        List<ResearchProjectDef> projects = techGroup.Value;
                        
                        // Find the first project of this tech level in the animation order
                        string firstProjectDefName = projects
                            .OrderBy(p => animationOrder.IndexOf(p.defName))
                            .Select(p => p.defName)
                            .FirstOrDefault();
                            
                        if (firstProjectDefName != null)
                        {
                            // Header animation should be slightly ahead of the first project
                            float projectProgress = animationProgress.TryGetValue(firstProjectDefName, out float progress) ? progress : 0f;
                            
                            // Start header animation slightly before the first item
                            // But never go backward (only increase)
                            float currentHeaderProgress = techLevelHeaderProgress.TryGetValue(techLevel, out float hp) ? hp : 0f;
                            float newHeaderProgress = Mathf.Max(currentHeaderProgress, projectProgress * 1.2f);
                            techLevelHeaderProgress[techLevel] = Mathf.Min(newHeaderProgress, 1f);
                        }
                    }
                    
                    // Reset animation when all items are done
                    if (allComplete)
                    {
                        lastRerollTime = -1f;
                    }
                }
                
                // Update selection if needed
                if (!currentAvailableProjects.Contains(selectedProject))
                    selectedProject = researchTracker.CurrentProject.FirstOrFallback(null);
            }
        }

        public override void DoWindowContents(Rect canvas)
        {
            // Check for spacebar to skip animation
            if (lastRerollTime > 0f && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)
            {
                Event.current.Use();
                SoundDefOf.Click.PlayOneShotOnCamera();
                SkipAnimation();
            }

            // Progress bars at top with adjusted margins
            float progressBarHeight = 70f;
            float progressLabelHeight = 55f;  // Increased from 20f to 40f to provide more space for staggered labels
            float totalProgressHeight = progressBarHeight + progressLabelHeight;
            float horizontalMargin = 40f;  // Increased horizontal margin for progress bar
            float topMargin = 6f;  // Increased from 6f to 20f to provide more space at the top
            float arrowBottomPadding = 24f; // Extra space for the arrow and label
            
            // Apply margins to progress bar rect
            Rect progressRect = new Rect(
                horizontalMargin,  // Left margin
                topMargin + progressLabelHeight, 
                canvas.width - (horizontalMargin * 2),  // Account for both margins
                progressBarHeight + arrowBottomPadding  // Add padding for arrow
            );
            DrawTechLevelProgress(progressRect);

            // Main content area - starts after progress bar including arrow padding
            float mainContentY = topMargin + totalProgressHeight + arrowBottomPadding + 6f;
            float availableHeight = canvas.height - mainContentY;

            // Column widths - adjusted for two columns
            float leftWidth = canvas.width * 0.55f;    // 55% for random list
            float rightWidth = canvas.width * 0.45f;   // 45% for details

            float columnMargin = 16f;

            // Create column rectangles with margins
            Rect leftRect = new Rect(columnMargin, mainContentY, leftWidth - columnMargin, availableHeight);
            Rect rightRect = new Rect(leftWidth + columnMargin, mainContentY, rightWidth - (columnMargin * 2), availableHeight);

            DrawLeftColumn(leftRect);
            DrawRightColumn(rightRect);
        }

        private void DrawTechLevelProgress(Rect rect)
        {
            TechLevel[] techLevels = new[] 
            { 
                TechLevel.Neolithic,
                TechLevel.Medieval,
                TechLevel.Industrial,
                TechLevel.Spacer,
                TechLevel.Ultra,
                TechLevel.Archotech
            };

            // Check if ProgressionCore is active
            bool progressionCoreActive = GenTypes.GetTypeInAnyAssembly("ProgressionCore.ProgressionCoreMod") != null;
            float requiredProgress = progressionCoreActive ? GetRequiredProgressionPercent() : 1.0f;

            // Calculate statistics for all tech levels
            Dictionary<TechLevel, (int completed, int total)> techLevelStats = new Dictionary<TechLevel, (int, int)>();
            foreach (TechLevel techLevel in techLevels)
            {
                int completed = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Count(def => def.techLevel == techLevel && def.IsFinished);
                int total = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Count(def => def.techLevel == techLevel);
                
                techLevelStats[techLevel] = (completed, total);
            }

            // Two rows for labels with staggered positioning - more brick wall like
            float topLabelY = rect.y - 49f;   // Further row - moved even further away
            float bottomLabelY = rect.y - 27f; // Closer row - moved slightly further from bar
            float labelHeight = 16f;

            // Calculate the actual bar height (excluding arrow space)
            float actualBarHeight = 50f;
            
            // Progress bar with adjusted height
            Rect barRect = new Rect(rect.x, rect.y, rect.width, actualBarHeight);
            float currentBarX = barRect.x;
            float barWidth = barRect.width;

            // Draw background
            GUI.color = new Color(0.1f, 0.1f, 0.1f);
            Widgets.DrawBoxSolid(barRect, GUI.color);
            GUI.color = Color.white;

            // Calculate total width for non-zero tech levels
            float totalTechs = techLevelStats.Sum(kvp => kvp.Value.total);

            // For storing threshold position
            float advancementThresholdX = 0f;
            bool thresholdFound = false;

            int techLevelIndex = 0; // To track which row to place the label
            
            // Draw segments and labels
            foreach (TechLevel techLevel in techLevels)
            {
                var stats = techLevelStats[techLevel];
                if (stats.total == 0) continue; // Skip if no technologies in this level

                float segmentWidth = (float)stats.total / totalTechs * barWidth;
                float progress = stats.total > 0 ? (float)stats.completed / stats.total : 0f;
                
                // Draw segment
                Rect segmentRect = new Rect(currentBarX, barRect.y, segmentWidth, barRect.height);
                
                // Draw filled portion
                GUI.color = GetTechLevelColor(techLevel);
                Widgets.DrawBoxSolid(new Rect(segmentRect.x, segmentRect.y, segmentWidth * progress, segmentRect.height), GUI.color);
                
                // If this is the player's current tech level and ProgressionCore is active, store threshold position
                if (progressionCoreActive && techLevel == Faction.OfPlayer.def.techLevel)
                {
                    // Calculate threshold position
                    advancementThresholdX = segmentRect.x + (segmentWidth * requiredProgress);
                    thresholdFound = true;
                }
                
                // Draw segment border
                GUI.color = Color.grey;
                Widgets.DrawBox(segmentRect);

                // Draw staggered labels - alternate between top and bottom rows
                bool isTopRow = (techLevelIndex % 2 == 0);
                float labelY = isTopRow ? topLabelY : bottomLabelY;
                
                // Center point of this segment
                float centerX = currentBarX + (segmentWidth / 2);
                
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                
                // Create combined label text
                string techLevelName = techLevel.ToStringHuman().CapitalizeFirst();
                string statsText = $" ({stats.completed}/{stats.total})";
                string fullLabel = techLevelName + statsText;
                Vector2 labelSize = Text.CalcSize(fullLabel);
                
                // Position label centered on the centerX point
                Rect labelRect = new Rect(
                    centerX - (labelSize.x / 2),
                    labelY,
                    labelSize.x,
                    labelHeight
                );
                
                // Draw a connecting line from the label to the bar
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f); // Semi-transparent gray
                
                // Line from label center to bar
                Vector2 lineStart = new Vector2(centerX, labelY + labelHeight);
                Vector2 lineEnd = new Vector2(centerX, barRect.y - 1);
                Widgets.DrawLine(lineStart, lineEnd, GUI.color, 1f);
                
                // Draw label background for better readability
                Widgets.DrawBoxSolid(labelRect.ExpandedBy(3f), new Color(0.1f, 0.1f, 0.1f, 0.7f));
                
                // Draw the full label with two colors (tech name in tech color, stats in white)
                // First the tech level name
                GUI.color = GetTechLevelColor(techLevel);
                Rect techNameRect = new Rect(labelRect);
                techNameRect.width = Text.CalcSize(techLevelName).x;
                Widgets.Label(techNameRect, techLevelName);
                
                // Then the stats
                GUI.color = new Color(0.95f, 0.95f, 0.95f);
                Rect statsRect = new Rect(
                    techNameRect.xMax,
                    labelRect.y,
                    labelRect.xMax - techNameRect.xMax,
                    labelHeight
                );
                Widgets.Label(statsRect, statsText);
                
                // Draw tooltip
                if (Mouse.IsOver(segmentRect))
                {
                    string tooltip = $"{techLevel.ToStringHuman().CapitalizeFirst()}\n{stats.completed}/{stats.total} ({(progress * 100f):F0}%)";
                    
                    // Add ProgressionCore info to tooltip
                    if (progressionCoreActive && techLevel == Faction.OfPlayer.def.techLevel)
                    {
                        tooltip += $"\n\nProgression Core: {(progress * 100f):F0}% of {(requiredProgress * 100f):F0}% required to advance";
                        
                        if (progress >= requiredProgress)
                        {
                            tooltip += "\nReady to advance to next tech level!";
                        }
                        else
                        {
                            int remaining = (int)((requiredProgress * stats.total) - stats.completed + 0.999f);
                            tooltip += $"\nNeed {remaining} more research project(s)";
                        }
                    }
                    
                    TooltipHandler.TipRegion(segmentRect, tooltip);
                }
                
                currentBarX += segmentWidth;
                techLevelIndex++;
            }

            // Draw the threshold indicator with extended height and label
            if (thresholdFound)
            {
                // Draw an extended vertical line for the threshold
                float lineExtension = 20f; // Increased extension for the line
                float arrowSize = 5f;     // Increased arrow size
                Color thresholdColor = Color.white;
                GUI.color = thresholdColor;
                
                // Draw the extended line
                Widgets.DrawLine(
                    new Vector2(advancementThresholdX, barRect.y - 2f), 
                    new Vector2(advancementThresholdX, barRect.yMax + lineExtension), 
                    thresholdColor, 
                    2f);
                    
                // Add a larger arrow at the bottom with more space
                Vector2 arrowBase = new Vector2(advancementThresholdX, barRect.yMax + lineExtension);
                Vector2 arrowLeft = new Vector2(advancementThresholdX - arrowSize, barRect.yMax + lineExtension - arrowSize);
                Vector2 arrowRight = new Vector2(advancementThresholdX + arrowSize, barRect.yMax + lineExtension - arrowSize);
                Widgets.DrawLine(arrowBase, arrowLeft, thresholdColor, 2f);
                Widgets.DrawLine(arrowBase, arrowRight, thresholdColor, 2f);
                
                // Draw the label with more space below the arrow
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Rect labelRect = new Rect(advancementThresholdX - 60f, barRect.yMax + lineExtension + 4f, 120f, 20f);
                GUI.color = Color.white;
                
                // Determine the label text based on progress
                TechLevel currentTechLevel = Faction.OfPlayer.def.techLevel;
                var stats = techLevelStats[currentTechLevel];
                float progress = stats.total > 0 ? (float)stats.completed / stats.total : 0f;
                
                string thresholdLabel = progress >= requiredProgress 
                    ? "Ready to Advance!" 
                    : "Advance Tech Level";
                    
                Widgets.Label(labelRect, thresholdLabel);
            }

            // Reset
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawLeftColumn(Rect leftRect)
        {
            ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();

            Rect position = leftRect;
            GUI.BeginGroup(position);

            float currentY = 0f;
            float mainLabelHeight = 40.0f;
            float gapHeight = 8.0f;
            float researchProjectGapHeight = 12.0f;
            float buttonHeight = 48f;
            float techLevelHeaderHeight = 28f;

            // Footer dimensions
            float footerPaddingTop = 12f;
            float footerHeight = 40f;
            float footerPaddingBottom = 12f;
            float totalFooterHeight = footerPaddingTop + footerHeight + footerPaddingBottom;
            float footerButtonWidth = 120f;
            float buttonSpacing = 20f;

            // Check if we have an active research project
            bool hasActiveNonAnomalyResearch = false;
            bool hasActiveAnomalyResearchBasic = false;
            bool hasActiveAnomalyResearchAdvanced = false;
            ResearchProjectDef activeNonAnomalyProject = null;
            ResearchProjectDef activeAnomalyProjectBasic = null;
            ResearchProjectDef activeAnomalyProjectAdvanced = null;

            if (researchTracker != null && researchTracker.CurrentProject != null && researchTracker.CurrentProject.Count > 0)
            {
                // Get active normal research
                activeNonAnomalyProject = researchTracker.CurrentProject.FirstOrDefault(p => p.knowledgeCategory == null);
                hasActiveNonAnomalyResearch = activeNonAnomalyProject != null;

                // Get active anomaly research
                activeAnomalyProjectBasic = researchTracker.CurrentProject.FirstOrDefault(p => p.knowledgeCategory == KnowledgeCategoryDefOf.Basic);
                hasActiveAnomalyResearchBasic = activeAnomalyProjectBasic != null;

                activeAnomalyProjectAdvanced = researchTracker.CurrentProject.FirstOrDefault(p => p.knowledgeCategory == KnowledgeCategoryDefOf.Advanced);
                hasActiveAnomalyResearchAdvanced = activeAnomalyProjectAdvanced != null;
            }

            // If no active research from the mod, check vanilla research
            if (!hasActiveNonAnomalyResearch && Find.ResearchManager.GetProject() != null &&
                Find.ResearchManager.GetProject().knowledgeCategory == null)
            {
                activeNonAnomalyProject = Find.ResearchManager.GetProject();
                hasActiveNonAnomalyResearch = true;
            }


            // Get anomaly projects (for later use)
            var anomalyProjectsBasic = AnomalyContentEnabled() ?
                currentAvailableProjects.Where(p => p.knowledgeCategory == KnowledgeCategoryDefOf.Basic).ToList() :
                new List<ResearchProjectDef>();

            var anomalyProjectsAdvanced = AnomalyContentEnabled() ?
                currentAvailableProjects.Where(p => p.knowledgeCategory == KnowledgeCategoryDefOf.Advanced).ToList() :
                new List<ResearchProjectDef>();

            // If we have active anomaly research, only show that
            if (hasActiveAnomalyResearchBasic && anomalyProjectsBasic.Any())
            {
                anomalyProjectsBasic = new List<ResearchProjectDef> { activeAnomalyProjectBasic };
            }

            if (hasActiveAnomalyResearchAdvanced && anomalyProjectsAdvanced.Any())
            {
                anomalyProjectsAdvanced = new List<ResearchProjectDef> { activeAnomalyProjectAdvanced };
            }

            // Check if we have anomaly projects to display
            bool hasAnomalyToShowBasic = anomalyProjectsBasic.Any();
            bool hasAnomalyToShowAdvanced = anomalyProjectsAdvanced.Any();

            // Selected project name and tech levels - only show if not actively researching
            if (!hasActiveNonAnomalyResearch)
            {
                Text.Font = GameFont.Medium;
                GenUI.SetLabelAlign(TextAnchor.MiddleLeft);

                // Main label rect (left side)
                float labelWidth = position.width * 0.4f;
                Rect mainLabelRect = new Rect(0f, currentY, labelWidth, mainLabelHeight);
                Widgets.LabelCacheHeight(ref mainLabelRect, "CM_Semi_Random_Research_Available_Projects".Translate());

                // Tech levels info (right side)
                Text.Font = GameFont.Small;
                float techInfoX = labelWidth + 20f;
                float techInfoWidth = position.width - techInfoX;

                // Colony tech level
                TechLevel colonyTech = Faction.OfPlayer.def.techLevel;
                Rect colonyTechRect = new Rect(techInfoX, currentY, techInfoWidth * 0.5f, mainLabelHeight);
                DrawTechLevelText(colonyTechRect, "Faction: ", colonyTech);

                // World tech level
                TechLevel worldTech = Find.World.worldObjects.Settlements
                    .Where(s => s.Faction != null && !s.Faction.IsPlayer)
                    .Select(s => s.Faction.def.techLevel)
                    .DefaultIfEmpty(TechLevel.Undefined)
                    .Max();
                Rect worldTechRect = new Rect(techInfoX + techInfoWidth * 0.5f, currentY, techInfoWidth * 0.5f, mainLabelHeight);
                DrawTechLevelText(worldTechRect, "World: ", worldTech);

                GenUI.ResetLabelAlign();
                currentY += mainLabelHeight + 4f;
            }

            // If we have an active non-anomaly research project, show the research stats at the top
            if (hasActiveNonAnomalyResearch)
            {
                // Calculate research rate stats section dimensions - adjust height when anomaly research is present
                float rateStatsHeight = 300f; // Fixed height regardless of anomaly presence
                float rateStatsPadding = 30f;

                // Create a background for the active research section
                Rect activeResearchRect = new Rect(0f, currentY, position.width, rateStatsHeight);

                // Draw active research header
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Rect activeHeaderRect = new Rect(activeResearchRect.x + 10f, currentY + 6f, activeResearchRect.width - 20f, 30f);
                Widgets.Label(activeHeaderRect, "Currently Researching");
                Text.Font = GameFont.Small;

                // Draw research rate UI
                Rect rateStatsRect = new Rect(
                    activeResearchRect.x + 10f,
                    activeHeaderRect.yMax + 4f,
                    activeResearchRect.width - 20f,
                    activeResearchRect.height - activeHeaderRect.height - 10f
                );
                DrawResearchRateUI(rateStatsRect, activeNonAnomalyProject);

                // Update current Y position
                currentY += rateStatsHeight + rateStatsPadding;
            }

            // Adjust the scroll view to account for the header and footer
            Rect scrollOutRect = new Rect(0f, currentY, position.width, position.height - (totalFooterHeight + currentY));
            Rect scrollViewRect = new Rect(0f, 0f, scrollOutRect.width - 20f, leftScrollViewHeight);

            Widgets.BeginScrollView(scrollOutRect, ref leftScrollPosition, scrollViewRect);

            currentY = 0f;

            // Only show non-anomaly research options when not actively researching a normal project
            if (!hasActiveNonAnomalyResearch)
            {
                // Group non-anomaly projects by tech level
                var groupedProjects = currentAvailableProjects
                    .Where(p => p.knowledgeCategory == null) // Only non-anomaly projects
                    .GroupBy(proj => proj.techLevel)
                    .OrderBy(group => (int)group.Key);

                bool isFirst = true;
                foreach (var techGroup in groupedProjects)
                {
                    if (!techGroup.Any()) continue;

                    // Add gap before tech level header (except for first one)
                    if (!isFirst)
                    {
                        currentY += gapHeight;
                    }
                    isFirst = false;

                    // Get animation progress for this tech level
                    float headerAnimProgress = 1f;
                    if (techLevelHeaderProgress.TryGetValue(techGroup.Key, out float progress))
                        headerAnimProgress = progress;

                    // Skip drawing if not yet visible at all
                    if (headerAnimProgress <= 0.01f)
                        continue;

                    // Remember current color and apply fade
                    Color originalColor = GUI.color;
                    GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, headerAnimProgress);

                    // Draw tech level header
                    Text.Font = GameFont.Small;
                    Color techColor = GetTechLevelColor(techGroup.Key);
                    techColor.a *= headerAnimProgress; // Apply animation alpha
                    GUI.color = techColor;

                    Rect headerRect = new Rect(0f, currentY, scrollViewRect.width, techLevelHeaderHeight);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(headerRect, techGroup.Key.ToStringHuman().CapitalizeFirst());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = originalColor;
                    currentY += techLevelHeaderHeight;

                    // Draw projects for this tech level, sorted by cost
                    foreach (ResearchProjectDef projectDef in techGroup.OrderBy(p => p.CostApparent))
                    {
                        Rect buttonRect = new Rect(0f, currentY, scrollViewRect.width, buttonHeight);
                        DrawResearchButton(ref buttonRect, projectDef);
                        currentY += buttonHeight + researchProjectGapHeight;
                    }
                }
            }

            // Handle Anomaly Research Section - show at the bottom (if not on research graph screen)
            // OR after the graph when on research screen
            string anomaly_name;
            if (AnomalyContentEnabled() && (hasAnomalyToShowBasic || hasAnomalyToShowAdvanced))
            {
                // Get anomaly research name from the research tab description.
                // This allows us to support mods like ambiguous anomaly

                // Only show anomaly section if there are any anomaly projects
                // Add divider before anomaly section if we're not starting from the top
                // Create a smaller header for anomaly research - like tech level headers
                if (currentY > 0)
                {
                    // Add 60px extra space before the anomaly section when actively researching
                    if (hasActiveNonAnomalyResearch)
                    {
                        currentY += 300f; // Increased from 60f to 120f for more space
                    }

                    currentY += gapHeight;
                    GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.4f);
                    Widgets.DrawLineHorizontal(0f, currentY, scrollViewRect.width);
                    GUI.color = Color.white;
                    currentY += gapHeight;
                }

            }

            if (AnomalyContentEnabled() && hasAnomalyToShowBasic)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect anomalyHeaderRect = new Rect(0f, currentY, scrollViewRect.width, techLevelHeaderHeight);
                
                Color anomalyColor = new Color(0.65f, 0.35f, 0.5f); // Less saturated light purple
                GUI.color = anomalyColor;
                Widgets.Label(anomalyHeaderRect, "Dark research");
                GUI.color = Color.white;
                currentY += techLevelHeaderHeight;
                
                // Show all anomaly projects (or just the active one)
                foreach (ResearchProjectDef projectDef in anomalyProjectsBasic.OrderBy(p => p.CostApparent))
                {
                    Rect buttonRect = new Rect(0f, currentY, scrollViewRect.width, buttonHeight);
                    DrawResearchButton(ref buttonRect, projectDef);
                    currentY += buttonHeight + researchProjectGapHeight;
                }
            }

            if (AnomalyContentEnabled() && hasAnomalyToShowAdvanced)
            {
                if (hasActiveAnomalyResearchBasic)
                {
                    currentY += gapHeight;
                }
                
                // Create a smaller header for anomaly research - like tech level headers
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect anomalyHeaderRect = new Rect(0f, currentY, scrollViewRect.width, techLevelHeaderHeight);
                
                // Use less saturated purple color for anomaly header
                Color anomalyColor = new Color(0.45f, 0.35f, 0.5f); // Less saturated dark purple
                GUI.color = anomalyColor;
                Widgets.Label(anomalyHeaderRect, "Advanced dark research");
                GUI.color = Color.white;
                currentY += techLevelHeaderHeight;
                
                // Show all anomaly projects (or just the active one)
                foreach (ResearchProjectDef projectDef in anomalyProjectsAdvanced.OrderBy(p => p.CostApparent))
                {
                    Rect buttonRect = new Rect(0f, currentY, scrollViewRect.width, buttonHeight);
                    DrawResearchButton(ref buttonRect, projectDef);
                    currentY += buttonHeight + researchProjectGapHeight;
                }
            }

            leftScrollViewHeight = currentY;
            Widgets.EndScrollView();
            
            // Animation skip button
            if (lastRerollTime > 0f)
            {
                float skipButtonHeight = 32f;
                float skipButtonWidth = 120f;
                float skipButtonPadding = 6f;
                
                Rect skipButtonRect = new Rect(
                    scrollOutRect.x + scrollOutRect.width - skipButtonWidth - skipButtonPadding,
                    scrollOutRect.y + skipButtonPadding,
                    skipButtonWidth,
                    skipButtonHeight
                );
                
                // Draw semi-transparent background
                Widgets.DrawBoxSolid(skipButtonRect.ExpandedBy(2f), new Color(0.1f, 0.1f, 0.1f, 0.6f));
                
                if (Widgets.ButtonText(skipButtonRect, "Skip Animation"))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    SkipAnimation();
                }
            }

            // Draw footer controls with improved spacing
            if (researchTracker != null)
            {
                // Create the footer area with top padding
                Rect footerContainerRect = new Rect(
                    0f, 
                    position.height - totalFooterHeight, 
                    position.width,
                    totalFooterHeight
                );

                // Draw a subtle separator line above the footer
                GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
                Widgets.DrawLineHorizontal(footerContainerRect.x, footerContainerRect.y, footerContainerRect.width);
                GUI.color = Color.white;

                // In the footer section, change from a fixed 3-button layout to a dynamic layout
                // We need to check if we need to show a cancel button
                bool showCancelButton = SemiRandomResearchMod.settings.allowSwitchingResearch && 
                    (hasActiveNonAnomalyResearch || hasActiveAnomalyResearchBasic || hasActiveAnomalyResearchAdvanced);

                // Calculate total buttons and adjust layout
                int buttonCount = 3; // Default: Research Tree, Reroll, Research
                if (showCancelButton) buttonCount = 4; // Add Cancel button

                // Calculate total width for buttons
                float totalButtonsWidth = (footerButtonWidth * buttonCount) + (buttonSpacing * (buttonCount - 1));
                float startX = (footerContainerRect.width - totalButtonsWidth) / 2;

                // Position buttons
                Rect researchTreeButtonRect = new Rect(
                    footerContainerRect.x + startX,
                    footerContainerRect.y + footerPaddingTop,
                    footerButtonWidth,
                    footerHeight
                );

                // Position for the cancel button (if shown)
                Rect cancelButtonRect = new Rect(
                    researchTreeButtonRect.xMax + buttonSpacing,
                    footerContainerRect.y + footerPaddingTop,
                    footerButtonWidth,
                    footerHeight
                );

                // Reposition other buttons based on whether cancel is shown
                Rect rerollButtonRect = new Rect(
                    showCancelButton ? cancelButtonRect.xMax + buttonSpacing : researchTreeButtonRect.xMax + buttonSpacing,
                    footerContainerRect.y + footerPaddingTop,
                    footerButtonWidth,
                    footerHeight
                );

                Rect researchButtonRect = new Rect(
                    rerollButtonRect.xMax + buttonSpacing,
                    rerollButtonRect.y,
                    footerButtonWidth,
                    footerHeight
                );

                // Draw research tree button (unchanged)
                if (Widgets.ButtonText(researchTreeButtonRect, "Research Tree"))
                {
                    SoundDefOf.ResearchStart.PlayOneShotOnCamera();
                    MainTabWindow currentWindow = Find.WindowStack.WindowOfType<MainTabWindow>();
                    MainTabWindow newWindow = MainButtonDefOf.Research.TabWindow;
                    if (currentWindow != null && newWindow != null)
                    {
                        Find.WindowStack.TryRemove(currentWindow, false);
                        Find.WindowStack.Add(newWindow);
                        SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    }
                }

                // Draw cancel button if needed
                if (showCancelButton)
                {
                    ResearchProjectDef projectToCancel;
                    // Determine which project would be canceled
                    if (hasActiveNonAnomalyResearch)
                    {
                        projectToCancel = activeNonAnomalyProject;
                    }
                    else if (hasActiveAnomalyResearchBasic)
                    {
                        projectToCancel = activeAnomalyProjectBasic;
                    }
                    else
                    {
                        projectToCancel = activeAnomalyProjectAdvanced;
                    }
                    KnowledgeCategoryDef categoryToCancel = projectToCancel?.knowledgeCategory;
                    
                    if (Widgets.ButtonText(cancelButtonRect, "Cancel Research"))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        // This just cancels the research without rerolling
                        researchTracker.SetCurrentProject(null, categoryToCancel);
                        // Force refresh the list
                        researchTracker.ForceAutoReseachCheckNextTick();
                    }
                }

                // Draw reroll button (existing reroll button code)
                bool canReroll = researchTracker.CanReroll(rerollButtonType);
                string rerollText = canReroll ? "Reroll" : "No rerolls";

                if (canReroll)
                {
                    if (Widgets.ButtonText(rerollButtonRect, rerollText))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        SoundDefOf.TabOpen.PlayOneShotOnCamera();
                        researchTracker.Reroll(rerollButtonType);
                        lastRerollTime = Time.realtimeSinceStartup;
                    }
                }
                else
                {
                    GUI.color = Color.grey;
                    Widgets.DrawAtlas(rerollButtonRect, Widgets.ButtonSubtleAtlas);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(rerollButtonRect, rerollText);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                // Draw research button - always visible but with different states
                string researchButtonText = "Research";
                if (selectedProject == null)
                {
                    // No project selected
                    GUI.color = Color.grey;
                    Widgets.DrawAtlas(researchButtonRect, Widgets.ButtonSubtleAtlas);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(researchButtonRect, researchButtonText);
                }
                else if (selectedProject.IsFinished)
                {
                    GUI.color = Color.grey;
                    Widgets.DrawAtlas(researchButtonRect, Widgets.ButtonSubtleAtlas);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(researchButtonRect, "Finished");
                }
                else if (selectedProject == Find.ResearchManager.GetProject(selectedProject?.knowledgeCategory))
                {
                    GUI.color = ActiveProjectLabelColor;
                    Widgets.DrawAtlas(researchButtonRect, Widgets.ButtonSubtleAtlas);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(researchButtonRect, "In Progress");
                }
                else if (selectedProject.CanStartNow && 
                    (Find.ResearchManager.GetProject(selectedProject.knowledgeCategory) == null || 
                    SemiRandomResearchMod.settings.allowSwitchingResearch))
                {
                    // Can start research
                    if (Widgets.ButtonText(researchButtonRect, researchButtonText))
                    {
                        SoundDefOf.ResearchStart.PlayOneShotOnCamera();
                        Find.ResearchManager.SetCurrentProject(selectedProject);
                        Current.Game.World.GetComponent<ResearchTracker>()?.SetCurrentProject(selectedProject, selectedProject.knowledgeCategory);
                        TutorSystem.Notify_Event("StartResearchProject");
                        if (!ColonistsHaveResearchBench)
                        {
                            Messages.Message("MessageResearchMenuWithoutBench".Translate(), MessageTypeDefOf.CautionInput);
                        }
                    }
                }
                else
                {
                    // Can't start research
                    GUI.color = Color.grey;
                    Widgets.DrawAtlas(researchButtonRect, Widgets.ButtonSubtleAtlas);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(researchButtonRect, "Locked");
                }

                // Reset text anchor and color
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            GUI.EndGroup();
        }

        private void DrawTechLevelText(Rect rect, string prefix, TechLevel techLevel)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            
            // Draw prefix in default color
            GUI.color = Color.white;
            float prefixWidth = Text.CalcSize(prefix).x;
            Widgets.Label(rect, prefix);
            
            // Draw tech level name in its color
            Rect techLevelRect = new Rect(rect.x + prefixWidth, rect.y, rect.width - prefixWidth, rect.height);
            GUI.color = GetTechLevelColor(techLevel);
            Widgets.Label(techLevelRect, techLevel.ToStringHuman().CapitalizeFirst());
            
            GUI.color = Color.white;
        }

        private void DrawResearchButton(ref Rect drawRect, ResearchProjectDef projectDef)
        {
            // Get animation progress for this project
            float animProgress = 1f;
            if (animationProgress.TryGetValue(projectDef.defName, out float progress))
                animProgress = progress;
            
            // Skip drawing entirely if not yet visible
            if (animProgress <= 0.01f)
                return;
        
            // Store original rect and colors
            Rect originalRect = new Rect(drawRect);
            Color originalColor = GUI.color;
            
            // Check if mouse is over for hover effect
            bool isMouseOver = Mouse.IsOver(drawRect);
            
            // Apply fade effect to the entire GUI
            GUI.color = new Color(1f, 1f, 1f, animProgress);

            float iconSize = 32.0f;
            float innerMargin = 4f;
            float rightMargin = 8f;
            float costValueWidth = Text.CalcSize(projectDef.CostApparent.ToString()).x + innerMargin * 2;
            float buttonHeight = 48f;
            float nameLeftPadding = 12f;
            float separatorWidth = 1f;
            float borderWidth = isMouseOver ? 1.5f : 1f;
            float costSeparatorPadding = 4f;
            
            // Get the research rate tracker for ETA calculations
            ResearchRateTracker rateTracker = Current.Game.World.GetComponent<ResearchRateTracker>();
            float globalAverageRate = rateTracker != null ? rateTracker.GetGlobalAverageRate() : 0f;
            bool hasGlobalRateData = globalAverageRate > 0f;

            // Remember text settings
            TextAnchor startingTextAnchor = Text.Anchor;
            Text.Font = GameFont.Small;

            // Update total height
            drawRect.height = buttonHeight;
            
            // Adjust width to add right margin
            drawRect.width -= rightMargin;

            // We need more space for ETA, adjust layout
            // Create rects for single line layout with added ETA section
            Rect iconRect = new Rect(drawRect.x + innerMargin, drawRect.y + (buttonHeight - iconSize) / 2, iconSize, iconSize);
            
            // First separator position (after icon)
            Rect firstSeparator = new Rect(
                iconRect.xMax + innerMargin * 2,
                drawRect.y,
                separatorWidth, 
                buttonHeight
            );

            // Calculate positions with a fixed percentage approach for 3 sections
            float nameFieldPortion = 0.65f; // Reduced to make room for ETA
            float etaFieldPortion = 0.20f;  // Add portion for ETA
            float costFieldPortion = 0.15f; // Cost takes the remaining portion
            
            float availableWidthAfterIcon = drawRect.width - (firstSeparator.xMax + nameLeftPadding);
            
            // Second separator position (after name, before ETA)
            Rect secondSeparator = new Rect(
                firstSeparator.xMax + nameLeftPadding + (availableWidthAfterIcon * nameFieldPortion),
                drawRect.y,
                separatorWidth, 
                buttonHeight
            );
            
            // Third separator position (after ETA, before cost)
            Rect thirdSeparator = new Rect(
                secondSeparator.x + (availableWidthAfterIcon * etaFieldPortion),
                drawRect.y,
                separatorWidth,
                buttonHeight
            );

            // Name rect
            Rect nameRect = new Rect(
                firstSeparator.xMax + nameLeftPadding,
                drawRect.y, 
                secondSeparator.x - (firstSeparator.xMax + nameLeftPadding),
                buttonHeight
            );
            
            // ETA rect
            Rect etaRect = new Rect(
                secondSeparator.xMax + innerMargin,
                drawRect.y, 
                thirdSeparator.x - secondSeparator.xMax - innerMargin * 2,
                buttonHeight
            );

            // Cost rect
            Rect costRect = new Rect(
                thirdSeparator.xMax + costSeparatorPadding,
                drawRect.y, 
                drawRect.xMax - thirdSeparator.xMax - costSeparatorPadding - innerMargin,
                buttonHeight
            );

            // Set colors - use the same color for border and separators
            Color techColor = GetTechLevelColor(projectDef.techLevel);
            
            // Modified background color - brighter on hover
            Color backgroundColor = isMouseOver 
                ? Color.Lerp(TexUI.AvailResearchColor, techColor, 0.4f)  
                : Color.Lerp(TexUI.AvailResearchColor, techColor, 0.3f); 
            
            // Border is brighter and more saturated on hover
            Color borderColor = selectedProject == projectDef ?
                TexUI.HighlightBorderResearchColor :
                (isMouseOver ? Color.Lerp(techColor, Color.white, 0.2f) : techColor);
            
            Color textColor = new Color(0.95f, 0.95f, 0.95f);

            // Use the same borderColor for separators to maintain consistency
            Color separatorColor = borderColor;

            // Check if this project is currently active and we're in a state where we can cancel
            bool isActive = projectDef == Find.ResearchManager.GetProject(projectDef.knowledgeCategory);
            bool canCancel = SemiRandomResearchMod.settings.allowSwitchingResearch && isActive;
            
            if (canCancel)
            {
                // Add a small cancel button to the top-right corner of the research card
                float cancelButtonSize = 20f;
                Rect cancelRect = new Rect(
                    drawRect.xMax - cancelButtonSize - 4f,
                    drawRect.y + 4f,
                    cancelButtonSize,
                    cancelButtonSize
                );
                
                // Only show cancel button on hover for cleaner UI
                if (isMouseOver || Mouse.IsOver(cancelRect))
                {
                    // Draw a small X button
                    GUI.color = new Color(0.9f, 0.3f, 0.3f, animProgress * 0.8f);
                    Widgets.DrawBoxSolid(cancelRect, GUI.color);
                    
                    // Draw the X
                    GUI.color = new Color(1f, 1f, 1f, animProgress);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    Widgets.Label(cancelRect, "×");
                    Text.Anchor = TextAnchor.UpperLeft;
                    
                    // Check for click on cancel button
                    if (Widgets.ButtonInvisible(cancelRect))
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();
                        if (researchTracker != null)
                        {
                            // Cancel this specific research project
                            researchTracker.SetCurrentProject(null, projectDef.knowledgeCategory);
                            // Force refresh the list
                            researchTracker.ForceAutoReseachCheckNextTick();
                            
                            // Prevent the main button click from triggering
                            Event.current.Use();
                        }
                    }
                }
            }
            
            // Draw button background with animation alpha
            backgroundColor.a *= animProgress;
            Widgets.DrawBoxSolid(drawRect, backgroundColor);
            
            // Optional subtle glow effect on hover
            if (isMouseOver)
            {
                // Draw a second slightly larger box with subtle transparency for glow effect
                Color glowColor = techColor;
                glowColor.a = 0.1f * animProgress;
                Widgets.DrawBoxSolid(drawRect.ExpandedBy(2f), glowColor);
            }
            
            // Ensure border color has correct alpha
            borderColor.a *= animProgress;
            
            // Draw border manually with tech color
            Widgets.DrawLine(new Vector2(drawRect.x, drawRect.y), new Vector2(drawRect.xMax, drawRect.y), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(drawRect.x, drawRect.yMax), new Vector2(drawRect.xMax, drawRect.yMax), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(drawRect.x, drawRect.y), new Vector2(drawRect.x, drawRect.yMax), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(drawRect.xMax, drawRect.y), new Vector2(drawRect.xMax, drawRect.yMax), borderColor, borderWidth);
            
            // Draw icon
            Def firstUnlockable = GetFirstUnlockable(projectDef);
            try
            {
                if (firstUnlockable != null)
                    Widgets.DefIcon(iconRect, firstUnlockable);
            }
            catch(Exception ex)
            {
                Log.Message("[CM_Semi_Random_Research] Error rendering icon for " + 
                    (firstUnlockable != null ? firstUnlockable.defName : " null"));
                Log.Message(ex);
            }

            // Draw separators with the same color as the border
            separatorColor.a *= animProgress;
            
            // Draw vertical separator lines with proper alpha
            Widgets.DrawLine(
                new Vector2(firstSeparator.x, firstSeparator.y), 
                new Vector2(firstSeparator.x, firstSeparator.yMax),
                separatorColor,
                separatorWidth
            );
            
            Widgets.DrawLine(
                new Vector2(secondSeparator.x, secondSeparator.y), 
                new Vector2(secondSeparator.x, secondSeparator.yMax), 
                separatorColor, 
                separatorWidth
            );
            
            Widgets.DrawLine(
                new Vector2(thirdSeparator.x, thirdSeparator.y), 
                new Vector2(thirdSeparator.x, thirdSeparator.yMax), 
                separatorColor, 
                separatorWidth
            );
            
            // Draw text elements
            Color usedTextColor = isActive ? ActiveProjectLabelColor : textColor;
            
            // Make text brighter on hover
            if (isMouseOver && !isActive)
            {
                usedTextColor = Color.white;  // Pure white for best visibility
                usedTextColor.a *= animProgress;
            }
            GUI.color = usedTextColor;
            
            // Draw project name
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, projectDef.LabelCap);
            
            // Draw estimated time if we have global data
            if (hasGlobalRateData)
            {
                // Calculate ETA
                float remainingWork = projectDef.CostApparent - projectDef.ProgressApparent;
                float estimatedDays = remainingWork / globalAverageRate;
                string etaText = ResearchRateTracker.FormatETA(estimatedDays);
                
                // Set color based on estimated days (similar to the stats section)
                Color etaColor = new Color(0.7f, 0.7f, 0.7f, usedTextColor.a); // Default gray
                
                if (estimatedDays >= 0)
                {
                    if (estimatedDays < 1f)
                        etaColor = new Color(0.0f, 0.7f, 0.0f, usedTextColor.a); // Desaturated green
                    else if (estimatedDays < 3f)
                        etaColor = new Color(0.7f, 0.7f, 0.0f, usedTextColor.a); // Desaturated yellow
                    else if (estimatedDays > 10f)
                        etaColor = new Color(0.75f, 0.5f, 0.3f, usedTextColor.a); // Desaturated orange
                }
                
                // Draw ETA with appropriate color
                GUI.color = etaColor;
                
                // Add explicit centering calculation
                float textWidth = Text.CalcSize(etaText).x;
                float centerX = etaRect.x + (etaRect.width - textWidth) / 2;
                Rect centeredEtaRect = new Rect(centerX, etaRect.y, textWidth, etaRect.height);
                
                Widgets.Label(centeredEtaRect, etaText);
                }
                else
                {
                // No rate data available, show "N/A days" with explicit centering
                GUI.color = new Color(0.5f, 0.5f, 0.5f, usedTextColor.a); // Muted gray
                
                string naText = "N/A days";
                float textWidth = Text.CalcSize(naText).x;
                float centerX = etaRect.x + (etaRect.width - textWidth) / 2;
                Rect centeredNaRect = new Rect(centerX, etaRect.y, textWidth, etaRect.height);
                
                Widgets.Label(centeredNaRect, naText);
            }
            
            // Reset color
            GUI.color = originalColor;
            
            // Draw cost with proper centering
            Text.Anchor = TextAnchor.MiddleCenter; // Changed from MiddleRight to MiddleCenter
            Widgets.Label(costRect, projectDef.CostApparent.ToString());

            // Handle mouse click - only if mostly visible
            if (animProgress >= 0.7f && Widgets.ButtonInvisible(drawRect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                selectedProject = projectDef;
            }

            // Draw highlight boxes if needed
            if(isActive) 
            {
                Color activeColor = TexUI.ActiveResearchColor;
                activeColor.a *= animProgress;
                DrawTransparentBox(drawRect, activeColor, 10, true);
            }
            else if(selectedProject == projectDef)
            {
                Color highlightColor = TexUI.HighlightBorderResearchColor;
                highlightColor.a *= animProgress;
                DrawTransparentBox(drawRect, highlightColor, 10, true);
            }
            
            // Draw tooltip if hovering
            if (isMouseOver)
            {
                // Create tooltip text
                StringBuilder tooltipText = new StringBuilder();
                
                // Basic research info
                tooltipText.AppendLine(projectDef.LabelCap);
                tooltipText.AppendLine("Cost: " + projectDef.CostApparent);
                tooltipText.AppendLine("Tech Level: " + projectDef.techLevel.ToStringHuman());
                
                // Add estimated time to tooltip as well
                if (hasGlobalRateData)
                {
                    float remainingWork = projectDef.CostApparent - projectDef.ProgressApparent;
                    float estimatedDays = remainingWork / globalAverageRate;
                    tooltipText.AppendLine("Estimated time: " + ResearchRateTracker.FormatETA(estimatedDays));
                }
                
                // Show unlocks count in tooltip
                var unlocks = UnlockedDefsGroupedByPrerequisites(projectDef);
                int unlockCount = 0;
                
                if (!unlocks.NullOrEmpty())
                {
                    foreach (var unlockGroup in unlocks)
                    {
                        unlockCount += unlockGroup.Second.Count;
                    }
                    
                    tooltipText.AppendLine("Unlocks: " + unlockCount + " items");
                }
                
                // Show if project is active
                if (isActive)
                {
                    tooltipText.AppendLine();
                    tooltipText.AppendLine("Currently researching");
                }
                
                // Set tooltip
                TooltipHandler.TipRegion(drawRect, tooltipText.ToString());
            }
            
            // Reset
            GUI.color = originalColor;
            Text.Anchor = startingTextAnchor;
            
            // Reset rect for next item
            drawRect = originalRect;
        }

        private Color GetTechLevelColor(TechLevel techLevel)
        {
            switch (techLevel)
            {
                case TechLevel.Animal:
                    return new Color(0.5f, 0.4f, 0.2f); // Warmer brown
                case TechLevel.Neolithic:
                    return new Color(0.6f, 0.35f, 0.35f); // Richer dark red
                case TechLevel.Medieval:
                    return new Color(0.6f, 0.6f, 0.3f); // Warmer yellow
                case TechLevel.Industrial:
                    return new Color(0.4f, 0.6f, 0.3f); // Brighter green-yellow
                case TechLevel.Spacer:
                    return new Color(0.3f, 0.5f, 0.6f); // Richer blue-green
                case TechLevel.Ultra:
                    return new Color(0.45f, 0.35f, 0.6f); // Deeper purple-blue
                case TechLevel.Archotech:
                    return new Color(0.6f, 0.35f, 0.6f); // Richer pink
                default:
                    return TexUI.AvailResearchColor;
            }
        }

        private void DrawTransparentBox(Rect rect, Color borderColor, float borderThickness = 1f, bool cutOutside = false)
        {
            Color saveColor = GUI.color;
            //Horizontal lines
            Widgets.DrawLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), borderColor, borderThickness);
            Widgets.DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.xMax, rect.yMax), borderColor, borderThickness);
            
            //Vertical lines
            Widgets.DrawLine(new Vector2(rect.x, rect.y + borderThickness),
                             new Vector2(rect.x, rect.yMax + 2*borderThickness), borderColor, borderThickness);
            Widgets.DrawLine(new Vector2(rect.xMax, rect.y + borderThickness),
                             new Vector2(rect.xMax, rect.yMax + 2*borderThickness), borderColor, borderThickness);
            
            if(cutOutside)
            {
                //Horizontal lines
                Widgets.DrawBoxSolid(new Rect(rect.x - borderThickness, rect.y - borderThickness, rect.width + borderThickness, borderThickness), Widgets.WindowBGFillColor);
                Widgets.DrawBoxSolid(new Rect(rect.x - borderThickness, rect.yMax, rect.width + borderThickness, borderThickness), Widgets.WindowBGFillColor);
                //Vertical lines
                Widgets.DrawBoxSolid(new Rect(rect.x - borderThickness, rect.y - borderThickness, borderThickness, rect.height + borderThickness), Widgets.WindowBGFillColor);
                Widgets.DrawBoxSolid(new Rect(rect.xMax, rect.y - borderThickness, borderThickness, rect.height + borderThickness), Widgets.WindowBGFillColor);
            }
            GUI.color = saveColor;

        }

        private void DrawBorderedBox(Rect rect, Color backgroundColor, Color borderColor, float borderThickness = 1f)
        {
            Color saveColor = GUI.color;

            Rect innerRect = new Rect(rect);
            innerRect.x += borderThickness;
            innerRect.y += borderThickness;
            innerRect.width -= borderThickness * 2;
            innerRect.height -= borderThickness * 2;

            Widgets.DrawRectFast(rect, borderColor);
            Widgets.DrawRectFast(innerRect, backgroundColor);

            GUI.color = saveColor;
        }

        private void DrawRightColumn(Rect rightRect)
        {
            Rect position = rightRect;
            GUI.BeginGroup(position);
            if (selectedProject != null)
            {
                float projectNameHeight = 50.0f;
                float gapHeight = 10.0f;
                
                // Debug button height if needed
                float debugFinishResearchNowButtonHeight = 30.0f;
                float debugButtonGap = Prefs.DevMode ? debugFinishResearchNowButtonHeight + gapHeight : 0f;

                float currentY = 0f;
                
                // Full height is available for the scroll view now
                Rect outRect = new Rect(0f, 0f, position.width, position.height - debugButtonGap);
                Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rightScrollViewHeight);

                Widgets.BeginScrollView(outRect, ref rightScrollPosition, viewRect);

                // Selected project name
                Text.Font = GameFont.Medium;
                GenUI.SetLabelAlign(TextAnchor.MiddleLeft);
                Rect projectNameRect = new Rect(0f, currentY, viewRect.width, projectNameHeight);
                Widgets.LabelCacheHeight(ref projectNameRect, selectedProject.LabelCap);
                GenUI.ResetLabelAlign();
                currentY += projectNameRect.height;

                // Selected project description
                Text.Font = GameFont.Small;
                Rect projectDescriptionRect = new Rect(0f, currentY, viewRect.width, 0f);
                Widgets.LabelCacheHeight(ref projectDescriptionRect, selectedProject.description);
                currentY += projectDescriptionRect.height;

                // Tech level research cost multiplier description
                if ((int)selectedProject.techLevel > (int)Faction.OfPlayer.def.techLevel)
                {
                    float costMultiplier = selectedProject.CostFactor(Faction.OfPlayer.def.techLevel);
                    Rect techLevelMultilplierDescriptionRect = new Rect(0f, currentY, viewRect.width, 0f);
                    string text = "TechLevelTooLow".Translate(Faction.OfPlayer.def.techLevel.ToStringHuman(), selectedProject.techLevel.ToStringHuman(), (1f / costMultiplier).ToStringPercent());
                    if (costMultiplier != 1f)
                    {
                        text += " " + "ResearchCostComparison".Translate(selectedProject.baseCost.ToString("F0"), selectedProject.CostApparent.ToString("F0"));
                    }
                    Widgets.LabelCacheHeight(ref techLevelMultilplierDescriptionRect, text);
                    currentY += techLevelMultilplierDescriptionRect.height;
                }

                // Prerequisites
                currentY += DrawResearchPrereqs(rect: new Rect(0f, currentY, viewRect.width, outRect.height), project: selectedProject);
                currentY += DrawResearchBenchRequirements(rect: new Rect(0f, currentY, viewRect.width, outRect.height), project: selectedProject);
                currentY += DrawStudyRequirements(rect: new Rect(0f, currentY, viewRect.width, outRect.height), project: selectedProject);
                
                // Unlockables
                Rect projectUnlockablesRect = new Rect(0f, currentY, viewRect.width, outRect.height);
                currentY += DrawUnlockableHyperlinks(projectUnlockablesRect, selectedProject);
                currentY += DrawContentSource(rect: new Rect(0f, currentY, viewRect.width, outRect.height), selectedProject);
                currentY = (rightScrollViewHeight = currentY + 3f);

                Widgets.EndScrollView();
                
                // Debug button - at the bottom of the right column
                if (Prefs.DevMode && !selectedProject.IsFinished)
                {
                    Rect debugButtonRect = new Rect(
                        0f,
                        outRect.yMax + gapHeight,
                        120f,
                        debugFinishResearchNowButtonHeight
                    );
                    
                    if (Widgets.ButtonText(debugButtonRect, "Debug: Finish now"))
                    {
                        Find.ResearchManager.SetCurrentProject(selectedProject);
                        Find.ResearchManager.FinishProject(selectedProject);
                        ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();
                        researchTracker.SetCurrentProject(selectedProject, selectedProject.knowledgeCategory);
                        researchTracker.ConsiderProjectFinished(selectedProject);
                        researchTracker.GetCurrentlyAvailableProjects();
                    }
                }
            }

            GUI.EndGroup();
        }

        private float DrawResearchPrereqs(ResearchProjectDef project, Rect rect)
        {
            if (project.prerequisites.NullOrEmpty() && (project.hiddenPrerequisites == null || project.hiddenPrerequisites.Count == 0))
            {
                return 0f;
            }
            float xMin = rect.xMin;
            float yMin = rect.yMin;
            
            // Use medium font for section header
            Text.Font = GameFont.Medium;
            Widgets.LabelCacheHeight(ref rect, "Prerequisites".Translate() + ":");
            rect.yMin += rect.height + 6f; // Add extra padding after header
            
            // Reset to normal font
            Text.Font = GameFont.Small;
            
            List<ResearchProjectDef> allPrereqs = new List<ResearchProjectDef>();
            
            // Combine visible and hidden prerequisites
            if (project.prerequisites != null)
                allPrereqs.AddRange(project.prerequisites);
            
            if (project.hiddenPrerequisites != null)
                allPrereqs.AddRange(project.hiddenPrerequisites);
            
            // Style parameters
            float itemHeight = 42f;  // Taller rows for prerequisites
            float iconSize = 28f;    // Icon size
            float iconPadding = 8f;  // Padding after icon
            
            foreach (ResearchProjectDef prereq in allPrereqs)
            {
                // Create a box for the entire prerequisite row
                Rect prereqRect = new Rect(rect.xMin + 6f, rect.yMin, rect.width - 6f, itemHeight);
                
                // Draw background color based on tech level
                Color techColor = GetTechLevelColor(prereq.techLevel);
                Color bgColor = Color.Lerp(TexUI.AvailResearchColor, techColor, 0.3f);
                Color borderColor = techColor;
                
                // Draw box and border
                Widgets.DrawBoxSolid(prereqRect, bgColor);
                DrawTransparentBox(prereqRect, borderColor, 1f);
                
                // Icon rect (left side)
                Rect iconRect = new Rect(
                    prereqRect.x + 6f, 
                    prereqRect.y + (itemHeight - iconSize) / 2, 
                    iconSize, 
                    iconSize
                );
                
                // Text rect (after icon)
                Rect labelRect = new Rect(
                    iconRect.xMax + iconPadding,
                    prereqRect.y,
                    prereqRect.width - iconRect.width - (iconPadding * 2) - 6f,
                    itemHeight
                );
                
                // Try to get and draw an icon from the prereq's unlockables
                Def firstUnlockable = null;
                try
                {
                    var unlockables = UnlockedDefsGroupedByPrerequisites(prereq);
                    if (!unlockables.NullOrEmpty() && !unlockables[0].Second.NullOrEmpty())
                    {
                        firstUnlockable = unlockables[0].Second[0];
                        Widgets.DefIcon(iconRect, firstUnlockable);
                    }
                }
                catch (Exception ex)
                {
                    // Fallback - just don't show an icon
                }
                
                // Draw text with the prerequisite name
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white; // Clear white text
                Widgets.Label(labelRect, prereq.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
                
                // Make the whole row clickable to select that research
                if (Widgets.ButtonInvisible(prereqRect))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    selectedProject = prereq;
                }
                
                rect.yMin += itemHeight + 4f; // Add spacing between prerequisites
            }
            
            GUI.color = Color.white;
            rect.xMin = xMin;
            
            // Add some bottom padding
            rect.yMin += 6f;
            
            return rect.yMin - yMin;
        }

        private float DrawResearchBenchRequirements(ResearchProjectDef project, Rect rect)
        {
            float xMin = rect.xMin;
            float yMin = rect.yMin;
            if (project.requiredResearchBuilding != null)
            {
                List<Map> maps = Find.Maps;
                Widgets.LabelCacheHeight(ref rect, "RequiredResearchBench".Translate() + ":");
                rect.xMin += 6f;
                rect.yMin += rect.height;
                GUI.color = FulfilledPrerequisiteColor;
                rect.height = Text.CalcHeight(project.requiredResearchBuilding.LabelCap, rect.width - 24f - 6f);
                Widgets.HyperlinkWithIcon(rect, new Dialog_InfoCard.Hyperlink(project.requiredResearchBuilding));
                rect.yMin += rect.height + 4f;
                GUI.color = Color.white;
                rect.xMin = xMin;
            }
            if (!project.requiredResearchFacilities.NullOrEmpty())
            {
                Widgets.LabelCacheHeight(ref rect, "RequiredResearchBenchFacilities".Translate() + ":");
                rect.yMin += rect.height;
                Building_ResearchBench building_ResearchBench = FindBenchFulfillingMostRequirements(project.requiredResearchBuilding, project.requiredResearchFacilities);
                CompAffectedByFacilities bestMatchingBench = null;
                if (building_ResearchBench != null)
                {
                    bestMatchingBench = building_ResearchBench.TryGetComp<CompAffectedByFacilities>();
                }
                rect.xMin += 6f;
                for (int j = 0; j < project.requiredResearchFacilities.Count; j++)
                {
                    DrawResearchBenchFacilityRequirement(project.requiredResearchFacilities[j], bestMatchingBench, project, ref rect);
                    rect.yMin += rect.height;
                }
                rect.yMin += 4f;
            }
            GUI.color = Color.white;
            rect.xMin = xMin;
            return rect.yMin - yMin;
        }

        private float DrawStudyRequirements(ResearchProjectDef project, Rect rect)
        {
            float yMin = rect.yMin;
            if (project.RequiredAnalyzedThingCount > 0)
            {
                Widgets.LabelCacheHeight(ref rect, "StudyRequirements".Translate() + ":");
                rect.xMin += 6f;
                rect.yMin += rect.height;
                foreach (ThingDef item in project.requiredAnalyzed)
                {
                    Rect rect2 = new Rect(rect.x, rect.yMin, rect.width, 24f);
                    Color? color = null;
                    Dialog_InfoCard.Hyperlink hyperlink = new Dialog_InfoCard.Hyperlink(item);
                    Widgets.HyperlinkWithIcon(rect2, hyperlink, null, 2f, 6f, color, truncateLabel: false);
                    rect.yMin += 24f;
                }
            }
            return rect.yMin - yMin;
        }

        private Building_ResearchBench FindBenchFulfillingMostRequirements(ThingDef requiredResearchBench, List<ThingDef> requiredFacilities)
        {
            tmpAllBuildings.Clear();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                tmpAllBuildings.AddRange(maps[i].listerBuildings.allBuildingsColonist);
            }
            float num = 0f;
            Building_ResearchBench building_ResearchBench = null;
            for (int j = 0; j < tmpAllBuildings.Count; j++)
            {
                Building_ResearchBench building_ResearchBench2 = tmpAllBuildings[j] as Building_ResearchBench;
                if (building_ResearchBench2 != null && (requiredResearchBench == null || building_ResearchBench2.def == requiredResearchBench))
                {
                    float researchBenchRequirementsScore = GetResearchBenchRequirementsScore(building_ResearchBench2, requiredFacilities);
                    if (building_ResearchBench == null || researchBenchRequirementsScore > num)
                    {
                        num = researchBenchRequirementsScore;
                        building_ResearchBench = building_ResearchBench2;
                    }
                }
            }
            tmpAllBuildings.Clear();
            return building_ResearchBench;
        }

        private void DrawResearchBenchFacilityRequirement(ThingDef requiredFacility, CompAffectedByFacilities bestMatchingBench, ResearchProjectDef project, ref Rect rect)
        {
            Thing thing = null;
            Thing thing2 = null;
            if (bestMatchingBench != null)
            {
                thing = bestMatchingBench.LinkedFacilitiesListForReading.Find((Thing x) => x.def == requiredFacility);
                thing2 = bestMatchingBench.LinkedFacilitiesListForReading.Find((Thing x) => x.def == requiredFacility && bestMatchingBench.IsFacilityActive(x));
            }
            GUI.color = FulfilledPrerequisiteColor;
            string text = requiredFacility.LabelCap;
            if (thing != null && thing2 == null)
            {
                text += " (" + "InactiveFacility".Translate() + ")";
            }
            rect.height = Text.CalcHeight(text, rect.width - 24f - 6f);
            Widgets.HyperlinkWithIcon(rect, new Dialog_InfoCard.Hyperlink(requiredFacility), text);
        }

        private float GetResearchBenchRequirementsScore(Building_ResearchBench bench, List<ThingDef> requiredFacilities)
        {
            float num = 0f;
            for (int i = 0; i < requiredFacilities.Count; i++)
            {
                CompAffectedByFacilities benchComp = bench.GetComp<CompAffectedByFacilities>();
                if (benchComp != null)
                {
                    List<Thing> linkedFacilitiesListForReading = benchComp.LinkedFacilitiesListForReading;
                    if (linkedFacilitiesListForReading.Find((Thing x) => x.def == requiredFacilities[i] && benchComp.IsFacilityActive(x)) != null)
                    {
                        num += 1f;
                    }
                    else if (linkedFacilitiesListForReading.Find((Thing x) => x.def == requiredFacilities[i]) != null)
                    {
                        num += 0.6f;
                    }
                }
            }
            return num;
        }

        private Def GetFirstUnlockable(ResearchProjectDef project)
        {
            List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>> list = UnlockedDefsGroupedByPrerequisites(project);

            if (list.NullOrEmpty())
                return null;

            List<Def> defList = list.First().Second;
            if (defList.NullOrEmpty())
                return null;

            int randomIndex = Rand.RangeInclusiveSeeded(0, defList.Count - 1, currentRandomSeed);

            return defList[randomIndex];
        }

        private float DrawUnlockableHyperlinks(Rect rect, ResearchProjectDef project)
        {
            List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>> list = UnlockedDefsGroupedByPrerequisites(project);

            if (list.NullOrEmpty())
            {
                if (errorDetected)
                {
                    GUI.color = Color.red;
                    Widgets.LabelCacheHeight(ref rect, "ERROR DETECTED: Check devlog for more information");
                    GUI.color = Color.white;
                    return rect.height;
                }
                return 0f;
            }
            float yMin = rect.yMin;
            float x = rect.x;
            
            // Increased font size for section headers
            Text.Font = GameFont.Medium;
            
            foreach (Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>> item in list)
            {
                ResearchPrerequisitesUtility.UnlockedHeader first = item.First;
                rect.x = x;
                
                // Draw section header with larger font
                if (!first.unlockedBy.Any())
                {
                    Widgets.LabelCacheHeight(ref rect, "Unlocks".Translate() + ":");
                }
                else
                {
                    Widgets.LabelCacheHeight(ref rect, string.Concat("UnlockedWith".Translate(), " ", HeaderLabel(first), ":"));
                }
                
                rect.x += 6f;
                rect.yMin += rect.height + 8f; // More padding after header
                
                // Reset font for items
                Text.Font = GameFont.Small;
                
                // Check if we need to use two columns (more than 8 items)
                bool useDoubleColumns = item.Second.Count > 8;
                float originalWidth = rect.width - 12f;
                float columnWidth = useDoubleColumns ? (originalWidth / 2) - 6f : originalWidth;
                float columnSpacing = useDoubleColumns ? 12f : 0f;
                float originalX = rect.x;
                float startingY = rect.yMin; // Store the starting Y position after the header
                float maxYColumn = rect.yMin;
                int columnCount = 0;
                
                foreach (Def item2 in item.Second)
                {
                    // Increase item height significantly
                    float itemHeight = 48f; // Much bigger than the original 24f
                    
                    // If using double column and this is the second half of items, move to right column
                    if (useDoubleColumns && columnCount >= (int)Math.Ceiling(item.Second.Count / 2.0f))
                    {
                        // Switch to second column if we just finished the first column
                        if (columnCount == (int)Math.Ceiling(item.Second.Count / 2.0f))
                        {
                            rect.x = originalX + columnWidth + columnSpacing;
                            rect.yMin = startingY; // Reset Y to starting position of the first item
                        }
                    }
                    
                    // Create a properly sized rect for the larger hyperlink
                    Rect itemRect = new Rect(rect.x, rect.yMin, columnWidth, itemHeight);
                    
                    // Check if mouse is over this item for highlight effect
                    bool isMouseOver = Mouse.IsOver(itemRect);
                    
                    // Draw subtle background that highlights on hover
                    Color bgColor = isMouseOver 
                        ? new Color(0.3f, 0.3f, 0.3f, 0.3f) 
                        : new Color(0.1f, 0.1f, 0.1f, 0.1f);
                    Widgets.DrawBoxSolid(itemRect, bgColor);
                    
                    // Add a border that's more visible on hover
                    Color borderColor = isMouseOver 
                        ? new Color(0.8f, 0.8f, 0.8f, 0.5f) 
                        : new Color(0.4f, 0.4f, 0.4f, 0.3f);
                    DrawTransparentBox(itemRect, borderColor, isMouseOver ? 1.5f : 1f);
                    
                    // Create hyperlink object
                    Dialog_InfoCard.Hyperlink hyperlink = new Dialog_InfoCard.Hyperlink(item2);
                    
                    // Create custom drawing to make icon larger
                    Rect iconRect = new Rect(itemRect.x + 6f, itemRect.y + (itemHeight - 32f)/2, 32f, 32f);
                    Rect labelRect = new Rect(iconRect.xMax + 12f, itemRect.y, itemRect.width - iconRect.width - 24f, itemHeight);
                    
                    // Draw the icon manually at a larger size
                    try
                    {
                        // Change icon brightness on hover
                        GUI.color = isMouseOver ? Color.white : new Color(0.9f, 0.9f, 0.9f);
                        Widgets.DefIcon(iconRect, item2);
                        
                        // Draw the text separately with middle alignment
                        Text.Anchor = TextAnchor.MiddleLeft;
                        // Brighter text on hover
                        GUI.color = isMouseOver ? Color.white : new Color(0.85f, 0.85f, 0.85f);
                        string label = item2.LabelCap;
                        Widgets.Label(labelRect, label);
                        Text.Anchor = TextAnchor.UpperLeft;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[CM_Semi_Random_Research] Error rendering icon for " + item2.defName + ": " + ex);
                        
                        // Fallback to standard hyperlink if custom drawing fails
                        Widgets.HyperlinkWithIcon(itemRect, hyperlink);
                    }
                    
                    // Keep the invisible button for the hyperlink functionality
                    if (Widgets.ButtonInvisible(itemRect))
                    {
                        hyperlink.ActivateHyperlink();
                    }
                    
                    // Move to next item with spacing
                    rect.yMin += itemHeight + 8f;
                    
                    // Track maximum Y position for double column layout
                    if (rect.yMin > maxYColumn)
                        maxYColumn = rect.yMin;
                        
                    columnCount++;
                }
                
                // Reset position to the furthest down of both columns
                if (useDoubleColumns)
                {
                    rect.yMin = maxYColumn;
                    rect.x = originalX;
                }
                
                // Extra space between categories
                rect.yMin += 16f;
            }
            
            // Reset text settings
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            
            return rect.yMin - yMin;
        }

        private float DrawContentSource(Rect rect, ResearchProjectDef project)
        {
            if (project.modContentPack == null || project.modContentPack.IsCoreMod)
            {
                return 0f;
            }
            float yMin = rect.yMin;
            TaggedString taggedString = "Stat_Source_Label".Translate() + ":  " + project.modContentPack.Name;
            Widgets.LabelCacheHeight(ref rect, taggedString.Colorize(Color.grey));
            ExpansionDef expansionDef = ModLister.AllExpansions.Find((ExpansionDef e) => e.linkedMod == project.modContentPack.PackageId);
            if (expansionDef != null)
            {
                GUI.DrawTexture(new Rect(Text.CalcSize(taggedString).x + 4f, rect.y, 20f, 20f), expansionDef.IconFromStatus);
            }
            return rect.yMax - yMin;
        }


        private string HeaderLabel(ResearchPrerequisitesUtility.UnlockedHeader headerProject)
        {
            StringBuilder stringBuilder = new StringBuilder();
            string value = "";
            for (int i = 0; i < headerProject.unlockedBy.Count; i++)
            {
                ResearchProjectDef researchProjectDef = headerProject.unlockedBy[i];
                string text = researchProjectDef.LabelCap;
                stringBuilder.Append(text).Append(value);
                value = ", ";
            }
            return stringBuilder.ToString();
        }

        private List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>> UnlockedDefsGroupedByPrerequisites(ResearchProjectDef project)
        {
            if (cachedUnlockedDefsGroupedByPrerequisites == null)
            {
                cachedUnlockedDefsGroupedByPrerequisites = new Dictionary<ResearchProjectDef, List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>>>();
            }
            List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>> value = new List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>>();
            if (project!=null && !cachedUnlockedDefsGroupedByPrerequisites.TryGetValue(project, out value))
            {
                // Seems that this function call can throw a NullReferenceException. This is not the problem of this mod, but the reports by people seeing SemiRandomResearch is in the stack trace is.
                try
                {
                    value = ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites(project);
                }
                catch(NullReferenceException nullex)
                {
                    errorDetected = true;

                    Log.Error("[CM_Semi_Random_Research] Error while gathering information which research unlocks which items. " + (project==null?" Function was called with null as parameter. This is a bug.": "This can indicate issues with your modpack. Do not report to Semi Random Research until you have confirmed that there is no error when opening the research screen without semi random research installed!"));
                    var erroringRecepies = DefDatabase<RecipeDef>.AllDefs.Where(x=>x?.products == null || x.products.Any(y =>  y?.thingDef == null));
                    if (erroringRecepies.Any())
                    {
                        RecipeDef broken = erroringRecepies.RandomElement();
                        string errorRecipeInformation = (broken?.modContentPack?.Name != null ?(" Most likely from mod : " + broken?.modContentPack?.Name):"") + (broken?.modContentPack?.PackageId!=null?" Suspected id of the mod that added the broken recipe: " + broken?.modContentPack?.PackageId: "");
                        Log.Error("[CM_Semi_Random_Research] Detected broken recepies! One of the broken recipes has the lable: " + broken?.label + " with DefName "+broken?.defName + errorRecipeInformation);
                    }
                    if (DefDatabase<ThingDef>.AllDefs.Any(x => x == null))
                    {
                        Log.Error("[CM_Semi_Random_Research] Detected null Thingdefs");
                    }
                    if (DefDatabase<TerrainDef>.AllDefs.Any(x => x == null))
                    {
                        Log.Error("[CM_Semi_Random_Research] Detected null TerrainDef");
                    }
                    value = new List<Pair<ResearchPrerequisitesUtility.UnlockedHeader, List<Def>>>();
                    Log.Error(nullex.StackTrace);
                }
                cachedUnlockedDefsGroupedByPrerequisites.Add(project, value);
            }
            return value;
        }

        // Add this method to calculate tech progression percentage
        private float GetTechProgressionPercent(TechLevel techLevel)
        {
            // Try to access ProgressionCore's method if it exists
            try
            {
                // Use reflection to access the method in the ProgressionCore mod
                System.Type progressionPatchType = GenTypes.GetTypeInAnyAssembly("ProgressionCore.RitualObligationTargetWorker_AnyGatherSpotForAdvancement_CanUseTargetInternal_Patch");
                if (progressionPatchType != null)
                {
                    // Get all research at the current tech level
                    var allResearch = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                                .Where(x => x.techLevel == techLevel && x.techprintCount == 0).ToList();
                    var finished = allResearch.Where(x => x.IsFinished).ToList();
                    
                    if (allResearch.Count > 0)
                    {
                        // Calculate completion percentage
                        return finished.Count / (float)allResearch.Count;
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Silently handle errors to avoid breaking the UI
                Log.Warning("[CM_Semi_Random_Research] Error trying to access ProgressionCore data: " + ex.Message);
            }
            
            // If anything fails, return -1 to indicate we couldn't get the data
            return -1f;
        }

        // Add this to retrieve the target percentage needed from ProgressionCore
        private float GetRequiredProgressionPercent()
        {
            try
            {
                // Use reflection to access the settings
                System.Type settingsType = GenTypes.GetTypeInAnyAssembly("ProgressionCore.ProgressionCoreSettings");
                if (settingsType != null)
                {
                    var field = settingsType.GetField("researchComplectionPercent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (field != null)
                    {
                        return (float)field.GetValue(null);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[CM_Semi_Random_Research] Error accessing ProgressionCore settings: " + ex.Message);
            }
            
            // Default value if we can't access the setting
            return 1.0f;
        }

        // Add this method to skip to the end of animation
        private void SkipAnimation()
        {
            // Only do something if we're actually in animation mode
            if (lastRerollTime <= 0f)
                return;
        
            // Set all animations to completed state
            foreach (string defName in animationOrder)
            {
                animationProgress[defName] = 1f;
            }
        
            // Set all tech level headers to completed state
            foreach (TechLevel techLevel in Enum.GetValues(typeof(TechLevel)))
            {
                techLevelHeaderProgress[techLevel] = 1f;
            }
        
            // Reset animation timer to indicate animation is over
            lastRerollTime = -1f;
        }

        public override void PreClose()
        {
            base.PreClose();
            
            // Skip animation when window closes
            SkipAnimation();
        }

        // Add this method to draw the research rate graph and ETA information
        private void DrawResearchRateUI(Rect rect, ResearchProjectDef project)
        {
            if (project == null) return;
            
            Color originalColor = GUI.color;
            TextAnchor originalAnchor = Text.Anchor;
            
            // Get the research rate tracker
            ResearchRateTracker rateTracker = Current.Game.World.GetComponent<ResearchRateTracker>();
            if (rateTracker == null) return;
            
            // Get rate information for this project
            ResearchRateInfo rateInfo = rateTracker.GetResearchRateInfo(project);
            
            // Check if we have enough data yet
            bool hasRateData = rateInfo.TotalSamples > 0;
            
            // Even if this specific project doesn't have data, we may have global data
            // Get global research rate info to ensure we always have an average
            float globalAverageRate = rateTracker.GetGlobalAverageRate();
            bool hasGlobalData = globalAverageRate > 0;
            
            // Calculate dimensions with increased spacing
            float padding = 4f;
            float lineHeight = 20f;
            float graphPadding = 6f;
            float sectionSpacing = 16f;  // Increased from 8f for more vertical space
            
            // Set up initial position
            float currentY = rect.y;
            
            // Draw the tech entry in the same style as the research buttons
            Text.Font = GameFont.Small;
            float headerHeight = 48f;
            Rect headerRect = new Rect(rect.x, currentY, rect.width, headerHeight);
            
            float iconSize = 32.0f;
            float innerMargin = 4f;
            float nameLeftPadding = 12f;
            float separatorWidth = 1f;
            float costValueWidth = Text.CalcSize(project.CostApparent.ToString()).x + innerMargin * 2;
            
            // Create rects for single line layout
            Rect iconRect = new Rect(headerRect.x + innerMargin, headerRect.y + (headerHeight - iconSize) / 2, iconSize, iconSize);
            
            // First separator position (after icon)
            Rect firstSeparator = new Rect(
                iconRect.xMax + innerMargin * 2,
                headerRect.y,
                separatorWidth, 
                headerHeight
            );
            
            // Second separator position - calculate with fixed percentage approach
            float nameFieldPortion = 0.85f;
            float availableWidthAfterIcon = headerRect.width - (firstSeparator.xMax + nameLeftPadding);
            Rect secondSeparator = new Rect(
                firstSeparator.xMax + nameLeftPadding + (availableWidthAfterIcon * nameFieldPortion),
                headerRect.y,
                separatorWidth, 
                headerHeight
            );
            
            // Name rect
            Rect nameRect = new Rect(
                firstSeparator.xMax + nameLeftPadding, 
                headerRect.y, 
                secondSeparator.x - (firstSeparator.xMax + nameLeftPadding),
                headerHeight
            );
            
            // Cost rect
            Rect costRect = new Rect(
                secondSeparator.xMax + innerMargin,
                headerRect.y, 
                costValueWidth,
                headerHeight
            );
            
            // Background and progress indicator
            Color techColor = GetTechLevelColor(project.techLevel);
            Color backgroundColor = Color.Lerp(TexUI.AvailResearchColor, techColor, 0.3f);
            
            // Draw background
            Widgets.DrawBoxSolid(headerRect, backgroundColor);
            
            // Draw progress overlay using the tech color with transparency
            Rect progressRect = new Rect(headerRect.x, headerRect.y, headerRect.width * project.ProgressPercent, headerRect.height);
            Color progressColor = techColor;
            progressColor.a = 0.4f; // Add transparency to make it visible but not overwhelming
            Widgets.DrawBoxSolid(progressRect, progressColor);
            
            // Draw borders
            Color borderColor = techColor;
            float borderWidth = 1f;
            Widgets.DrawLine(new Vector2(headerRect.x, headerRect.y), new Vector2(headerRect.xMax, headerRect.y), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(headerRect.x, headerRect.yMax), new Vector2(headerRect.xMax, headerRect.yMax), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(headerRect.x, headerRect.y), new Vector2(headerRect.x, headerRect.yMax), borderColor, borderWidth);
            Widgets.DrawLine(new Vector2(headerRect.xMax, headerRect.y), new Vector2(headerRect.xMax, headerRect.yMax), borderColor, borderWidth);
            
            // Draw icon
            Def firstUnlockable = GetFirstUnlockable(project);
            try {
                if (firstUnlockable != null)
                    Widgets.DefIcon(iconRect, firstUnlockable);
            } catch(Exception ex) {
                // Silently catch any icon rendering errors
            }
            
            // Draw separators with tech color
            Widgets.DrawLine(
                new Vector2(firstSeparator.x, firstSeparator.y), 
                new Vector2(firstSeparator.x, firstSeparator.yMax), 
                borderColor, 
                separatorWidth
            );
            
            Widgets.DrawLine(
                new Vector2(secondSeparator.x, secondSeparator.y), 
                new Vector2(secondSeparator.x, secondSeparator.yMax), 
                borderColor, 
                separatorWidth
            );
            
            // Draw name
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(nameRect, project.LabelCap);

            // Progress indicator - simplified format with properly centered text
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            string progressText = $"{project.ProgressApparent:F0}/{project.CostApparent:F0}";
            float progressTextWidth = Text.CalcSize(progressText).x;

            // Center within the entire right portion of the header, not just the cost rect
            float availableRightSideWidth = headerRect.width - secondSeparator.x;
            float progressCenterX = secondSeparator.x + (availableRightSideWidth - progressTextWidth) / 2;
            Rect centeredProgressRect = new Rect(progressCenterX, costRect.y, progressTextWidth, costRect.height);
            Widgets.Label(centeredProgressRect, progressText);

            // Remove the standalone cost display entirely
            // Text.Anchor = TextAnchor.MiddleRight;
            // Widgets.Label(costRect, project.CostApparent.ToString());
            
            // Redesigned stats row - all on a single line with more vertical space
            currentY += headerHeight + sectionSpacing; // More space after progress bar

            // Increase the line height for more vertical space
            float statsLineHeight = 38f; // Increased even more for better spacing

            // Single stats row with all information
            Rect statsRowRect = new Rect(rect.x, currentY, rect.width, statsLineHeight);

            // Draw a subtle background for the stats row
            Widgets.DrawBoxSolid(statsRowRect, new Color(0.1f, 0.1f, 0.1f, 0.2f));

            // Divide the row into three equal sections - use direct calculation, no padding contractions
            float sectionWidth = statsRowRect.width / 3;

            // Current Rate (First third)
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Section 1: Current Rate with explicit centering
            Rect currentRateRect = new Rect(statsRowRect.x, statsRowRect.y, sectionWidth, statsLineHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);

            // Header: "Current"
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(currentRateRect.x, currentRateRect.y, currentRateRect.width, statsLineHeight/2), "Current");

            // Value with explicit centering
            GUI.color = new Color(0.65f, 0.8f, 0.9f); // Desaturated blue
            string currentRateText = hasRateData ? rateInfo.CurrentRateFormatted.Replace(" research/day", "/d") : "Calculating...";
            float currentTextWidth = Text.CalcSize(currentRateText).x;
            float currentCenterX = currentRateRect.x + (currentRateRect.width - currentTextWidth) / 2;
            Rect centeredCurrentRect = new Rect(currentCenterX, currentRateRect.y + statsLineHeight/2, currentTextWidth, statsLineHeight/2);
            Widgets.Label(centeredCurrentRect, currentRateText);

            // Section 2: 10-Day Average with explicit centering
            Rect avgRateRect = new Rect(statsRowRect.x + sectionWidth, statsRowRect.y, sectionWidth, statsLineHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);

            // Header: "10d Avg"
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(avgRateRect.x, avgRateRect.y, avgRateRect.width, statsLineHeight/2), "10d Avg");

            // Value with explicit centering
            string averageRateText;
            if (hasRateData)
            {
                averageRateText = rateInfo.AverageRateFormatted.Replace(" research/day", "/d");
            }
            else if (hasGlobalData)
            {
                averageRateText = ResearchRateTracker.FormatRate(globalAverageRate).Replace(" research/day", "/d");
            }
            else
            {
                averageRateText = "0/d";
            }

            GUI.color = new Color(0.8f, 0.8f, 0.6f); // Desaturated gold
            float avgTextWidth = Text.CalcSize(averageRateText).x;
            float avgCenterX = avgRateRect.x + (avgRateRect.width - avgTextWidth) / 2;
            Rect centeredAvgRect = new Rect(avgCenterX, avgRateRect.y + statsLineHeight/2, avgTextWidth, statsLineHeight/2);
            Widgets.Label(centeredAvgRect, averageRateText);

            // Section 3: ETA with explicit centering
            Rect etaRect = new Rect(statsRowRect.x + sectionWidth * 2, statsRowRect.y, sectionWidth, statsLineHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);

            // Header: "Est. Time"
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(etaRect.x, etaRect.y, etaRect.width, statsLineHeight/2), "Est. Time");

            // Calculate ETA using global average if needed
            string etaText;
            float estimatedDays = -1f;

            if (hasRateData && rateInfo.EstimatedDaysToCompletion >= 0)
            {
                etaText = rateInfo.ETAFormatted;
                estimatedDays = rateInfo.EstimatedDaysToCompletion;
            }
            else if (hasGlobalData)
            {
                // Calculate ETA with global average
                float remainingProgress = project.CostApparent - project.ProgressApparent;
                estimatedDays = remainingProgress / globalAverageRate;
                etaText = ResearchRateTracker.FormatETA(estimatedDays);
            }
            else
            {
                etaText = "Unknown";
            }

            // Set color based on estimated days
            Color etaColor = new Color(0.7f, 0.7f, 0.7f); // Default gray
            if (estimatedDays >= 0)
            {
                if (estimatedDays < 1f)
                    etaColor = new Color(0.0f, 0.7f, 0.0f); // Desaturated green
                else if (estimatedDays < 3f)
                    etaColor = new Color(0.7f, 0.7f, 0.0f); // Desaturated yellow
                else if (estimatedDays > 10f)
                    etaColor = new Color(0.75f, 0.5f, 0.3f); // Desaturated orange
            }

            GUI.color = etaColor;
            float etaTextWidth = Text.CalcSize(etaText).x;
            float etaCenterX = etaRect.x + (etaRect.width - etaTextWidth) / 2;
            Rect centeredEtaRect = new Rect(etaCenterX, etaRect.y + statsLineHeight/2, etaTextWidth, statsLineHeight/2);
            Widgets.Label(centeredEtaRect, etaText);

            // Extra spacing before graph
            currentY += statsLineHeight + sectionSpacing;  // More space before graph
            
            // Draw the research rate graph with slightly reduced height and more desaturated colors
            if (hasRateData || hasGlobalData)
            {
                // Make the graph tall but 20% less than before
                float graphHeight = 140f; // Much shorter graph
                
                // If needed, expand the containing rect to accommodate the taller graph
                if (rect.yMax < currentY + graphHeight + 10f)
                {
                    float additionalHeightNeeded = (currentY + graphHeight + 10f) - rect.yMax;
                    rect.height += additionalHeightNeeded;
                }
                
                Rect graphRect = new Rect(
                    rect.x + graphPadding, 
                    currentY, 
                    rect.width - (graphPadding * 2), 
                    graphHeight
                );
                
                // Draw a very subtle background for the graph
                Widgets.DrawBoxSolid(graphRect, new Color(0.1f, 0.1f, 0.1f, 0.2f));
                DrawTransparentBox(graphRect, new Color(0.4f, 0.4f, 0.4f, 0.3f), 1f);
                
                // Pass actual data or global data
                List<float> samplesForGraph = hasRateData ? 
                    rateTracker.GetRateSamplesPeriod(project, 3) : 
                    rateTracker.GetGlobalRateSamplesPeriod(3);
                    
                if (samplesForGraph.Count > 0)
                {
                    DrawRateGraph(graphRect, samplesForGraph, rateTracker.GetAverageRate(project));
                }
                else
                {
                    // Draw "No Data" text in the middle if we don't have samples
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    Widgets.Label(graphRect, "Collecting Data...");
                }
            }
            
            // Reset text settings
            Text.Anchor = originalAnchor;
            GUI.color = originalColor;
        }

        // Completely redesigned graph drawing for a cleaner, taller look
        private void DrawRateGraph(Rect rect, List<float> samples, float averageRate)
        {
            if (samples.Count == 0) return;
            
            float padding = 10f;
            Rect graphAreaRect = rect.ContractedBy(padding);
            
            // Calculate the max value for the graph scale
            float maxValue = samples.Max() * 1.2f; // Add 20% headroom
            maxValue = Mathf.Max(maxValue, 0.1f); // Ensure we have a non-zero scale
            
            // Calculate metrics for drawing
            float barWidth = graphAreaRect.width / Mathf.Max(samples.Count, 1);
            
            // Draw bottom axis line only - clean and minimal
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            Widgets.DrawLine(
                new Vector2(graphAreaRect.x, graphAreaRect.yMax),
                new Vector2(graphAreaRect.xMax, graphAreaRect.yMax),
                GUI.color,
                1f);
            
            // Draw bars with no spacing between them
            for (int i = 0; i < samples.Count; i++)
            {
                float normalizedValue = samples[i] / maxValue; // Scale to 0-1
                
                // Calculate bar rectangle - no spacing between bars
                float barHeight = normalizedValue * graphAreaRect.height;
                Rect barRect = new Rect(
                    graphAreaRect.x + (i * barWidth),
                    graphAreaRect.yMax - barHeight,
                    barWidth, // No spacing between bars
                    barHeight
                );
                
                // Much more desaturated color gradient
                Color barColor = Color.Lerp(
                    new Color(0.4f, 0.5f, 0.6f), // Desaturated blue-gray for lower values
                    new Color(0.5f, 0.6f, 0.4f), // Desaturated sage green for higher values
                    normalizedValue
                );
                
                // Draw the bar
                GUI.color = barColor;
                Widgets.DrawBoxSolid(barRect, barColor);
            }
            
            // Draw the average line if we have an average
            if (averageRate > 0)
            {
                // Only show average line if it's within the visible range
                if (averageRate <= maxValue)
                {
                    float avgY = graphAreaRect.yMax - (averageRate / maxValue * graphAreaRect.height);
                    
                    // More desaturated gold for average line
                    GUI.color = new Color(0.7f, 0.65f, 0.45f, 0.8f);
                    
                    Widgets.DrawLine(
                        new Vector2(graphAreaRect.x, avgY),
                        new Vector2(graphAreaRect.xMax, avgY),
                        GUI.color,
                        2f);
                    
                    // Draw discreet average label with matching desaturated color
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(
                        new Rect(graphAreaRect.x, avgY - 10f, graphAreaRect.width - 5f, 14f),
                        "Average");
                }
            }
            
            // Reset color
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Also need to modify DrawLeftColumn to account for these changes when showing active research

        // Helper method to draw a mini-graph showing research progress over time
        private void DrawProgressMiniGraph(Rect rect, ResearchProjectDef project)
        {
            // Set up the graph area
            float padding = 4f;
            Rect graphAreaRect = rect.ContractedBy(padding);
            
            // Draw background
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.3f));
            
            // Calculate progress percentage
            float progress = project.ProgressPercent;
            
            // Draw the progress bar
            Rect progressRect = new Rect(
                graphAreaRect.x,
                graphAreaRect.y,
                graphAreaRect.width * progress,
                graphAreaRect.height
            );
            
            // Draw progress fill
            Color progressColor = new Color(0.2f, 0.7f, 0.9f);
            Widgets.DrawBoxSolid(progressRect, progressColor);
            
            // Draw percentage text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(graphAreaRect, $"{progress * 100:F1}% Complete");
            
            // Reset text anchor
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}