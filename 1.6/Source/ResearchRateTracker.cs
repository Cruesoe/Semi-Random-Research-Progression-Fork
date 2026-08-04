using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;       // This contains the WorldComponent class
using RimWorld;    // This contains various RimWorld specific types
using RimWorld.Planet; // Add this for WorldComponent

namespace CM_Semi_Random_Research
{
    /// <summary>
    /// Tracks research rates and provides estimates for research completion
    /// </summary>
    public class ResearchRateTracker : WorldComponent
    {
        // Constants
        private const int SAMPLES_PER_DAY = 24; // Take a sample every hour (1/24th of a day)
        private const int DAYS_TO_TRACK = 10;   // Track 10 days of history
        private const int MAX_SAMPLES = SAMPLES_PER_DAY * DAYS_TO_TRACK; // Maximum samples to store
        private const float SAMPLE_INTERVAL_TICKS = GenDate.TicksPerDay / SAMPLES_PER_DAY; // Sample interval in ticks
        
        // Data structures
        // Dictionary that maps research projects to their data
        private Dictionary<string, ProjectRateData> projectDataCache = new Dictionary<string, ProjectRateData>();
        
        // Global research rate tracking across all projects
        private List<float> globalRateSamples = new List<float>();
        
        // Timing variables
        private float nextSampleTick = 0f;
        
        // Keep track of previously sampled projects to maintain history
        private HashSet<string> previousProjectDefNames = new HashSet<string>();
        
        // Constructor for loading
        public ResearchRateTracker(World world) : base(world)
        {
        }

        // Called when the game is loaded
        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            // Initialize the next sample time
            if (nextSampleTick <= 0f)
            {
                nextSampleTick = Find.TickManager.TicksGame + SAMPLE_INTERVAL_TICKS;
            }

            // Init the global rate samples if needed
            if (globalRateSamples == null)
            {
                globalRateSamples = new List<float>();
            }

        }
        
        // Called every tick by the World component
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            
            // Check if it's time to sample the research rate
            if (Find.TickManager.TicksGame >= nextSampleTick)
            {
                SampleCurrentResearchRate();
                nextSampleTick = Find.TickManager.TicksGame + SAMPLE_INTERVAL_TICKS;
            }
        }
        
        // Take a sample of the current research rate for all active projects
        private void SampleCurrentResearchRate()
        {
            ResearchManager researchManager = Find.ResearchManager;
            if (researchManager == null) return;
            
            // Find all active research projects
            List<ResearchProjectDef> activeProjects = new List<ResearchProjectDef>();
            
            // Get the standard active project using the GetProject method
            ResearchProjectDef currentProject = researchManager.GetProject();
            if (currentProject != null)
            {
                activeProjects.Add(currentProject);
            }
            
            // Get any projects from Semi-Random Research mod (using knowledge categories)
            ResearchTracker researchTracker = Current.Game.World.GetComponent<ResearchTracker>();
            if (researchTracker != null && researchTracker.CurrentProject != null)
            {
                foreach (var proj in researchTracker.CurrentProject)
                {
                    if (proj != null && !activeProjects.Contains(proj))
                    {
                        activeProjects.Add(proj);
                    }
                }
            }
            
            // Total progress change for this sample
            float totalProgressChange = 0f;
            
            // Process each active project
            foreach (var project in activeProjects)
            {
                float progressChange = SampleProjectRate(project);
                totalProgressChange += progressChange;
                
                // Add to previously sampled projects
                previousProjectDefNames.Add(project.defName);
            }
            
            // Compute global rate and add it to the global samples
            float globalRatePerDay = totalProgressChange * SAMPLES_PER_DAY;
            if (globalRatePerDay > 0)
            {
                globalRateSamples.Add(globalRatePerDay);
                // No trimming of global samples
            }
            
            // Also continue tracking previously researched projects
            // even if they're not currently selected
            foreach (string projectDefName in previousProjectDefNames.ToList())
            {
                // Skip if already processed as active
                if (activeProjects.Any(p => p.defName == projectDefName))
                    continue;
                
                // Find the project def
                ResearchProjectDef projectDef = DefDatabase<ResearchProjectDef>.GetNamed(projectDefName, false);
                if (projectDef != null && !projectDef.IsFinished)
                {
                    // Add an empty sample to maintain continuous data
                    // (progress is 0 since it's not being actively researched)
                    if (projectDataCache.TryGetValue(projectDef.defName, out ProjectRateData projectData))
                    {
                        if (projectData.rateSamples.Count > 0)
                        {
                            // Add a zero rate sample
                            projectData.rateSamples.Add(0f);
                            
                            // Trim excess samples
                            if (projectData.rateSamples.Count > MAX_SAMPLES)
                            {
                                projectData.rateSamples.RemoveAt(0);
                            }
                        }
                    }
                }
                else if (projectDef != null && projectDef.IsFinished)
                {
                    // Remove completed projects from tracking
                    previousProjectDefNames.Remove(projectDefName);
                }
            }
        }
        
        // Sample the rate for a specific project and return the progress change
        private float SampleProjectRate(ResearchProjectDef project)
        {
            if (project == null) return 0f;
            
            // Get or create the rate data for this project
            if (!projectDataCache.TryGetValue(project.defName, out ProjectRateData projectData))
            {
                projectData = new ProjectRateData();
                projectDataCache[project.defName] = projectData;
            }
            
            // Get the current progress
            float currentProgress = project.ProgressApparent;
            float progressChange = 0f;
            
            // Calculate the rate (progress per day) if we have a previous sample
            if (projectData.lastSampleProgress >= 0)
            {
                progressChange = currentProgress - projectData.lastSampleProgress;
                
                // Convert to rate per day (normalize by how many samples per day we take)
                float ratePerDay = progressChange * SAMPLES_PER_DAY;
                
                // Only record non-zero rates if progress was made
                if (progressChange > 0)
                {
                    // Add the new rate sample - keep all samples (no trimming)
                    projectData.rateSamples.Add(ratePerDay);
                }
                else if (progressChange == 0)
                {
                    // If no progress was made but this is the active project,
                    // add a zero sample to show inactivity in the graph
                    projectData.rateSamples.Add(0f);
                }
                else if (progressChange < 0)
                {
                    // Progress decreased (probably from dev mode or mod changing things)
                    // Reset tracking for this project
                    projectData.rateSamples.Clear();
                    progressChange = 0f; // Don't count negative progress in global rate
                }
            }
            
            // Store the current progress for the next sample
            projectData.lastSampleProgress = currentProgress;
            
            return progressChange;
        }
        
        // Get the current research rate (progress per day) for a project
        public float GetCurrentRate(ResearchProjectDef project)
        {
            if (project == null) return GetGlobalCurrentRate();
            
            if (projectDataCache.TryGetValue(project.defName, out ProjectRateData projectData))
            {
                // Return the most recent rate
                if (projectData.rateSamples.Count > 0)
                {
                    return projectData.rateSamples.Last();
                }
            }
            
            return 0f;
        }
        
        // Get the global current research rate (most recent sample)
        private float GetGlobalCurrentRate()
        {
            if (globalRateSamples.Count > 0)
            {
                return globalRateSamples.Last();
            }
            return 0f;
        }
        
        // Get the average research rate over the specified number of days
        public float GetAverageRate(ResearchProjectDef project, int days = DAYS_TO_TRACK)
        {
            if (project == null) return GetGlobalAverageRate(days);
            
            if (projectDataCache.TryGetValue(project.defName, out ProjectRateData projectData))
            {
                if (projectData.rateSamples.Count == 0) return GetGlobalAverageRate(days);
                
                // Calculate how many samples to average
                int samplesToAverage = Math.Min(days * SAMPLES_PER_DAY, projectData.rateSamples.Count);
                
                // Get the most recent samples
                List<float> recentSamples = projectData.rateSamples
                    .Skip(projectData.rateSamples.Count - samplesToAverage)
                    .ToList();
                
                // Return the true average including zero samples
                if (recentSamples.Count > 0)
                {
                    return recentSamples.Average();
                }
            }
            
            return GetGlobalAverageRate(days);
        }
        
        // Get the global average research rate across all projects
        public float GetGlobalAverageRate(int days = DAYS_TO_TRACK)
        {
            if (globalRateSamples.Count == 0) return 0f;
            
            // Calculate how many samples to average
            int samplesToAverage = Math.Min(days * SAMPLES_PER_DAY, globalRateSamples.Count);
            
            // Get the most recent samples
            List<float> recentSamples = globalRateSamples
                .Skip(globalRateSamples.Count - samplesToAverage)
                .ToList();
            
            // Return the true average including zero samples
            if (recentSamples.Count > 0)
            {
                return recentSamples.Average();
            }
            
            return 0f;
        }
        
        // Get an estimate of days until research completion
        public float GetEstimatedDaysToCompletion(ResearchProjectDef project)
        {
            if (project == null) return -1f;
            
            float averageRate = GetAverageRate(project);
            if (averageRate <= 0f) 
            {
                // Try using global average if project-specific rate is zero
                averageRate = GetGlobalAverageRate();
                if (averageRate <= 0f) return -1f;
            }
            
            float remainingProgress = project.CostApparent - project.ProgressApparent;
            if (remainingProgress <= 0f) return 0f; // Already complete
            
            return remainingProgress / averageRate;
        }
        
        // Get all rate samples for a project (for graphing)
        public List<float> GetRateSamples(ResearchProjectDef project)
        {
            if (project == null) return globalRateSamples.ToList();
            
            if (projectDataCache.TryGetValue(project.defName, out ProjectRateData projectData))
            {
                return new List<float>(projectData.rateSamples);
            }
            
            return new List<float>();
        }
        
        // Get rate samples for specific time period (for graphing)
        public List<float> GetRateSamplesPeriod(ResearchProjectDef project, int days)
        {
            if (project == null) return globalRateSamples.Skip(Math.Max(0, globalRateSamples.Count - (days * SAMPLES_PER_DAY))).ToList();
            
            if (projectDataCache.TryGetValue(project.defName, out ProjectRateData projectData))
            {
                if (projectData.rateSamples.Count == 0) return new List<float>();
                
                // Calculate how many samples to retrieve
                int samplesToGet = Math.Min(days * SAMPLES_PER_DAY, projectData.rateSamples.Count);
                
                // Get the most recent samples
                return projectData.rateSamples
                    .Skip(projectData.rateSamples.Count - samplesToGet)
                    .ToList();
            }
            
            return new List<float>();
        }
        
        // Get the research data for display formatting
        public ResearchRateInfo GetResearchRateInfo(ResearchProjectDef project)
        {
            ResearchRateInfo info = new ResearchRateInfo
            {
                CurrentRate = project != null ? GetCurrentRate(project) : GetGlobalCurrentRate(),
                AverageRate = project != null ? GetAverageRate(project) : GetGlobalAverageRate(),
                EstimatedDaysToCompletion = project != null ? GetEstimatedDaysToCompletion(project) : -1f,
                PercentComplete = project != null ? project.ProgressPercent * 100f : 0f,
                TotalSamples = project != null ? 
                    (projectDataCache.TryGetValue(project.defName, out ProjectRateData data) ? data.rateSamples.Count : 0) :
                    globalRateSamples.Count
            };
            
            return info;
        }
        
        // Format a rate value as a string with proper units
        public static string FormatRate(float ratePerDay)
        {
            if (ratePerDay <= 0) return "0 research/day";
            
            // Format with 1 decimal place
            return $"{ratePerDay:F1} research/day";
        }
        
        // Format ETA as a string
        public static string FormatETA(float daysToCompletion)
        {
            if (daysToCompletion < 0) return "Unknown";
            if (daysToCompletion == 0) return "Complete";
            
            if (daysToCompletion < 1)
            {
                // Convert to hours
                float hours = daysToCompletion * 24f;
                return $"{hours:F1} hours";
            }
            else if (daysToCompletion < 10)
            {
                // Show days with 1 decimal place
                return $"{daysToCompletion:F1} days";
            }
            else
            {
                // Show whole days
                return $"{Math.Round(daysToCompletion)} days";
            }
        }
        
        // Override for saving data
        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref nextSampleTick, "nextSampleTick", 0f);
            Scribe_Collections.Look(ref globalRateSamples, "globalRateSamples", LookMode.Value);
            Scribe_Collections.Look(ref previousProjectDefNames, "previousProjectDefNames", LookMode.Value);
            
            // Handle dictionary serialization
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Convert dictionary to lists for saving
                List<string> projectIds = projectDataCache.Keys.ToList();
                List<ProjectRateData> projectDatas = projectDataCache.Values.ToList();
                
                Scribe_Collections.Look(ref projectIds, "projectIds", LookMode.Value);
                Scribe_Collections.Look(ref projectDatas, "projectDatas", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Load lists
                List<string> projectIds = null;
                List<ProjectRateData> projectDatas = null;
                
                Scribe_Collections.Look(ref projectIds, "projectIds", LookMode.Value);
                Scribe_Collections.Look(ref projectDatas, "projectDatas", LookMode.Deep);
                
                // Initialize collections if they're null
                if (globalRateSamples == null)
                    globalRateSamples = new List<float>();
                    
                if (previousProjectDefNames == null)
                    previousProjectDefNames = new HashSet<string>();
                
                // Reconstruct the dictionary
                projectDataCache.Clear();
                if (projectIds != null && projectDatas != null && projectIds.Count == projectDatas.Count)
                {
                    for (int i = 0; i < projectIds.Count; i++)
                    {
                        projectDataCache[projectIds[i]] = projectDatas[i];
                    }
                }
            }
        }
        
        // Inner class to store rate data for a specific project
        public class ProjectRateData : IExposable
        {
            public float lastSampleProgress = -1f;
            public List<float> rateSamples = new List<float>();
            
            // Required empty constructor for Scribe
            public ProjectRateData()
            {
            }
            
            // Serialize/deserialize data
            public void ExposeData()
            {
                Scribe_Values.Look(ref lastSampleProgress, "lastSampleProgress", -1f);
                Scribe_Collections.Look(ref rateSamples, "rateSamples", LookMode.Value);
                
                // Initialize if needed
                if (rateSamples == null)
                {
                    rateSamples = new List<float>();
                }
            }
        }
        
        // Add this method to expose global rate samples for a specific period
        public List<float> GetGlobalRateSamplesPeriod(int days)
        {
            if (globalRateSamples.Count == 0) return new List<float>();
            
            // Calculate how many samples to retrieve
            int samplesToGet = Math.Min(days * SAMPLES_PER_DAY, globalRateSamples.Count);
            
            // Get the most recent samples
            return globalRateSamples
                .Skip(globalRateSamples.Count - samplesToGet)
                .ToList();
        }
    }
    
    // Helper class for displaying research rate information
    public class ResearchRateInfo
    {
        public float CurrentRate { get; set; }
        public float AverageRate { get; set; }
        public float EstimatedDaysToCompletion { get; set; }
        public float PercentComplete { get; set; }
        public int TotalSamples { get; set; }
        
        public string CurrentRateFormatted => ResearchRateTracker.FormatRate(CurrentRate);
        public string AverageRateFormatted => ResearchRateTracker.FormatRate(AverageRate);
        public string ETAFormatted => ResearchRateTracker.FormatETA(EstimatedDaysToCompletion);
    }
}
