using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// On-board laboratory. Picks up physical samples anywhere on the vessel (cores, solar wind
    /// canisters, dust collectors...) and, over time, produces a separate fully-transmittable
    /// analysis result for the same location. The physical sample is NOT consumed: return it home
    /// for its own science on top of the analysis.
    ///
    /// Recipes are configured as: input>output@hours, separated by ';'
    ///   analyses = fhsRegolithCore>fhsCoreAnalysis@2; fhsDeepCore>fhsDeepCoreAnalysis@6
    /// </summary>
    public class ModuleSampleAnalyzer : FHSScienceModule
    {
        [KSPField] public string analyses = "";
        [KSPField] public float ecPerSecond = 2f;
        [KSPField] public int minCrew = 0;
        [KSPField] public float speedMultiplier = 1f;
        /// <summary>Speed bonus per scientist star (level 0 scientist = +1 step).</summary>
        [KSPField] public float scientistBonus = 0.25f;
        [KSPField] public float analyzedTransmitScalar = 1f;
        /// <summary>Keep analyzing while the vessel is unloaded (power is not simulated in the background).</summary>
        [KSPField] public bool backgroundProcessing = true;

        [KSPField(isPersistant = true)] public string currentInput = "";
        [KSPField(isPersistant = true)] public string currentOutput = "";
        [KSPField(isPersistant = true)] public double analysisProgress;
        [KSPField(isPersistant = true)] public double analysisDuration;
        [KSPField(isPersistant = true)] public double lastUT = -1;
        [KSPField(isPersistant = true)] public string analyzedSubjects = "";

        [KSPField(guiActive = true, guiName = "Analyzer")]
        public string status = "Idle";

        [KSPField(guiActive = true, guiName = "Lab speed")]
        public string speedDisplay = "";

        private struct Recipe
        {
            public string input;
            public string output;
            public double seconds;
        }

        private List<Recipe> recipes = new List<Recipe>();
        private HashSet<string> analyzed = new HashSet<string>();

        private bool Analyzing
        {
            get { return !string.IsNullOrEmpty(currentInput); }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            recipes = ParseRecipes(analyses);
            analyzed = new HashSet<string>(FHSUtil.SplitList(analyzedSubjects, '|', s => s));

            if (HighLogic.LoadedSceneIsFlight && Analyzing && backgroundProcessing && lastUT > 0)
            {
                double elapsed = FHSUtil.Now - lastUT;
                if (elapsed > 1 && CrewOK())
                {
                    analysisProgress += elapsed * Speed();
                    if (analysisProgress >= analysisDuration) CompleteAnalysis(false);
                }
            }
            lastUT = FHSUtil.Now;
            UpdateEvents();
        }

        private static List<Recipe> ParseRecipes(string text)
        {
            var list = new List<Recipe>();
            foreach (string entry in FHSUtil.SplitList(text, ';', s => s))
            {
                // input>output@hours
                int gt = entry.IndexOf('>');
                int at = entry.IndexOf('@');
                if (gt <= 0 || at <= gt) continue;
                double hours;
                if (!double.TryParse(entry.Substring(at + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out hours)) continue;
                list.Add(new Recipe
                {
                    input = entry.Substring(0, gt).Trim(),
                    output = entry.Substring(gt + 1, at - gt - 1).Trim(),
                    seconds = hours * 3600.0
                });
            }
            return list;
        }

        public override string GetModuleDisplayName()
        {
            return "Sample Analyzer";
        }

        public override string GetInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyzes physical samples carried anywhere on the vessel.");
            if (minCrew > 0) sb.AppendLine("Crew required: " + minCrew);
            sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s");
            if (speedMultiplier != 1f) sb.AppendLine("Speed: x" + speedMultiplier.ToString("0.##"));
            sb.AppendLine("Scientists: +" + (scientistBonus * 100f).ToString("0") + "% speed per level");
            sb.AppendLine();
            foreach (Recipe r in ParseRecipes(analyses))
            {
                ScienceExperiment i = ResearchAndDevelopment.GetExperiment(r.input);
                sb.AppendLine(string.Format("<color=#99ff00>{0}</color> ({1})",
                    i != null ? i.experimentTitle : r.input, FHSUtil.FormatDuration(r.seconds / speedMultiplier)));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        [KSPEvent(guiActive = true, guiName = "Analyze Next Sample")]
        public void StartAnalysis()
        {
            if (Analyzing) return;
            if (StorageFull)
            {
                FHSUtil.Message("Analyzer storage full. Transmit or discard reports first.");
                return;
            }
            if (!CrewOK())
            {
                FHSUtil.Message(string.Format("This lab needs at least {0} crew aboard.", minCrew));
                return;
            }

            int pending;
            ScienceData sample;
            Recipe recipe;
            if (!FindNextSample(out sample, out recipe, out pending))
            {
                FHSUtil.Message("No unanalyzed samples on board. Drill a core or seal a collector first.");
                return;
            }

            currentInput = sample.subjectID;
            currentOutput = recipe.output;
            analysisDuration = recipe.seconds;
            analysisProgress = 0;
            lastUT = FHSUtil.Now;
            FHSUtil.Message(string.Format("Analyzing {0} (~{1}). {2} more sample(s) queued.",
                sample.title, FHSUtil.FormatDuration(analysisDuration / Speed()), pending - 1));
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Cancel Analysis", active = false)]
        public void CancelAnalysis()
        {
            currentInput = currentOutput = "";
            analysisProgress = analysisDuration = 0;
            UpdateEvents();
        }

        [KSPAction("Analyze Next Sample")]
        public void StartAnalysisAction(KSPActionParam p)
        {
            StartAnalysis();
        }

        private void UpdateEvents()
        {
            Events["StartAnalysis"].active = !Analyzing;
            Events["CancelAnalysis"].active = Analyzing;
        }

        // ------------------------------------------------------------------
        // Logic
        // ------------------------------------------------------------------

        private bool FindNextSample(out ScienceData sample, out Recipe recipe, out int pending)
        {
            sample = null;
            recipe = default(Recipe);
            pending = 0;
            var seen = new HashSet<string>();
            foreach (IScienceDataContainer c in vessel.FindPartModulesImplementing<IScienceDataContainer>())
            {
                foreach (ScienceData d in c.GetData())
                {
                    if (d == null || analyzed.Contains(d.subjectID) || !seen.Add(d.subjectID)) continue;
                    string expId = FHSUtil.ExperimentIdOf(d.subjectID);
                    foreach (Recipe r in recipes)
                    {
                        if (r.input != expId) continue;
                        if (sample == null)
                        {
                            sample = d;
                            recipe = r;
                        }
                        pending++;
                        break;
                    }
                }
            }
            return sample != null;
        }

        private bool CrewOK()
        {
            return minCrew <= 0 || part.protoModuleCrew.Count >= minCrew;
        }

        private double Speed()
        {
            double levels = 0;
            foreach (ProtoCrewMember c in part.protoModuleCrew)
                if (c.trait == "Scientist") levels += c.experienceLevel + 1;
            return speedMultiplier * (1.0 + scientistBonus * levels);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            lastUT = FHSUtil.Now;
            speedDisplay = "x" + Speed().ToString("0.00");

            if (!Analyzing)
            {
                status = StorageFull ? "Storage full" : "Idle";
                return;
            }
            if (!CrewOK())
            {
                status = "Paused: needs crew";
                return;
            }

            float dt = TimeWarp.fixedDeltaTime;
            double need = ecPerSecond * dt;
            if (need > 0 && part.RequestResource(FHSUtil.ElectricCharge, need) < need * 0.95)
            {
                status = "Paused: no power";
                return;
            }

            analysisProgress += dt * Speed();
            double remaining = (analysisDuration - analysisProgress) / Speed();
            status = string.Format("{0:P0}, {1} left", analysisProgress / analysisDuration, FHSUtil.FormatDuration(remaining));
            if (analysisProgress >= analysisDuration) CompleteAnalysis(vessel == FlightGlobals.ActiveVessel);
        }

        private void CompleteAnalysis(bool showDialog)
        {
            string input = currentInput;
            ScienceExperiment outExp = ResearchAndDevelopment.GetExperiment(currentOutput);
            CancelAnalysis();

            CelestialBody body;
            ExperimentSituations sit;
            string biome;
            if (outExp == null || !FHSUtil.TryParseSubject(input, out body, out sit, out biome))
            {
                FHSUtil.Log("Could not complete analysis of " + input);
                return;
            }
            if (!outExp.BiomeIsRelevantWhile(sit)) biome = string.Empty;

            analyzed.Add(input);
            analyzedSubjects = string.Join("|", new List<string>(analyzed).ToArray());

            ScienceData result = FHSUtil.CreateData(outExp, body, sit, biome, analyzedTransmitScalar, part.flightID);
            FHSUtil.Message("Analysis complete: " + result.title + ". The original sample is preserved for return.", 8f);
            if (showDialog)
                StoreAndReview(result);
            else
            {
                storedData.Add(result);
                UpdateDataEvents();
            }
        }
    }
}
