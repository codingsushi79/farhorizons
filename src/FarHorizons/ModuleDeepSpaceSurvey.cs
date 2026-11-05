using System;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Long-running observation that only makes progress while its conditions hold: far from the sun,
    /// far from Kerbin's radio noise, outside magnetospheres, landed and still, and so on. Progress
    /// pauses (it is not lost) when conditions break, and continues while the vessel is unloaded as
    /// long as conditions held when it was last seen.
    /// </summary>
    public class ModuleDeepSpaceSurvey : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsCosmicRay";
        [KSPField] public string surveyName = "Survey";
        [KSPField] public double surveyHours = 6;
        [KSPField] public float ecPerSecond = 0.2f;
        [KSPField] public string animationName = "";

        // Conditions
        [KSPField] public bool requireLanded = false;
        [KSPField] public bool requireSpace = false;
        [KSPField] public bool requireSunSOI = false;
        [KSPField] public bool requireStationary = false;
        [KSPField] public bool requireOutsideMagnetosphere = false;
        [KSPField] public double magnetosphereRadii = 10;
        /// <summary>Minimum distance from the sun, in multiples of the home planet's orbit.</summary>
        [KSPField] public double minSunDistanceAU = 0;
        /// <summary>Minimum distance from the home planet in meters (radio quiet zone).</summary>
        [KSPField] public double minHomeDistance = 0;
        /// <summary>Must not be in the home planet's SOI.</summary>
        [KSPField] public bool forbidHomeSOI = false;

        [KSPField(isPersistant = true)] public bool running;
        [KSPField(isPersistant = true)] public double surveyProgress;
        [KSPField(isPersistant = true)] public double lastUT = -1;
        [KSPField(isPersistant = true)] public bool conditionsHeld;

        [KSPField(guiActive = true, guiName = "Survey")]
        public string status = "Idle";

        private ScienceExperiment experiment;
        private FHSAnimator animator;

        private double RequiredSeconds
        {
            get { return surveyHours * 3600.0; }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            experiment = ResearchAndDevelopment.GetExperiment(experimentID);
            animator = new FHSAnimator(part, animationName);
            animator.Set(running, true);

            string title = experiment != null ? experiment.experimentTitle : surveyName;
            Fields["status"].guiName = title;
            Events["StartSurvey"].guiName = "Start: " + title;
            Events["StopSurvey"].guiName = "Stop: " + title;
            Actions["StartSurveyAction"].guiName = "Start: " + title;

            if (HighLogic.LoadedSceneIsFlight && running && conditionsHeld && lastUT > 0)
            {
                double elapsed = FHSUtil.Now - lastUT;
                if (elapsed > 1) surveyProgress += elapsed;
                if (surveyProgress >= RequiredSeconds) CompleteSurvey(false);
            }
            lastUT = FHSUtil.Now;
            UpdateEvents();
        }

        public override string GetModuleDisplayName()
        {
            return surveyName;
        }

        public override string GetInfo()
        {
            ScienceExperiment exp = ResearchAndDevelopment.GetExperiment(experimentID);
            var sb = new StringBuilder();
            sb.AppendLine("<b>" + (exp != null ? exp.experimentTitle : experimentID) + "</b>");
            sb.AppendLine("Duration: " + FHSUtil.FormatDuration(RequiredSeconds) + " of continuous observation");
            if (ecPerSecond > 0) sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s");
            sb.AppendLine("Requires:");
            if (requireLanded) sb.AppendLine("  - landed");
            if (requireStationary) sb.AppendLine("  - stationary");
            if (requireSpace) sb.AppendLine("  - in space");
            if (requireSunSOI) sb.AppendLine("  - solar orbit");
            if (forbidHomeSOI) sb.AppendLine("  - outside the home planet's SOI");
            if (requireOutsideMagnetosphere) sb.AppendLine("  - outside planetary magnetospheres");
            if (minSunDistanceAU > 0) sb.AppendLine("  - at least " + minSunDistanceAU.ToString("0.##") + "x Kerbin's distance from the sun");
            if (minHomeDistance > 0) sb.AppendLine("  - at least " + FHSUtil.FormatDistance(minHomeDistance) + " from Kerbin");
            sb.AppendLine("Progress pauses if conditions break.");
            return sb.ToString();
        }

        [KSPEvent(guiActive = true, guiName = "Start Survey")]
        public void StartSurvey()
        {
            if (experiment == null)
            {
                FHSUtil.Message("Unknown experiment " + experimentID);
                return;
            }
            if (StorageFull)
            {
                FHSUtil.Message("Data storage full. Transmit or discard first.");
                return;
            }
            running = true;
            lastUT = FHSUtil.Now;
            animator.Set(true, false);
            string why = CheckConditions();
            FHSUtil.Message(why == null
                ? string.Format("{0} started. {1} of observation needed.", experiment.experimentTitle, FHSUtil.FormatDuration(RequiredSeconds - surveyProgress))
                : string.Format("{0} armed, waiting: {1}.", experiment.experimentTitle, why));
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Stop Survey", active = false)]
        public void StopSurvey()
        {
            running = false;
            conditionsHeld = false;
            animator.Set(false, false);
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Discard Survey Progress", active = false)]
        public void ResetSurvey()
        {
            StopSurvey();
            surveyProgress = 0;
            UpdateEvents();
        }

        [KSPAction("Start Survey")]
        public void StartSurveyAction(KSPActionParam p)
        {
            if (!running) StartSurvey();
        }

        private void UpdateEvents()
        {
            Events["StartSurvey"].active = !running;
            Events["StopSurvey"].active = running;
            Events["ResetSurvey"].active = !running && surveyProgress > 0;
        }

        private string CheckConditions()
        {
            if (vessel == null || experiment == null) return "no vessel";
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(vessel);

            if (requireLanded && vessel.situation != Vessel.Situations.LANDED) return "must be landed";
            if (requireStationary && vessel.srfSpeed > 0.1) return "must be stationary";
            if (requireSpace && !FHSUtil.IsInSpace(sit)) return "must be in space";
            if (requireSunSOI && !FHSUtil.InSunSOI(vessel)) return "must be in solar orbit";
            if (forbidHomeSOI && vessel.mainBody == FlightGlobals.GetHomeBody()) return "too close to home";
            if (requireOutsideMagnetosphere && FHSUtil.InsideMagnetosphere(vessel, magnetosphereRadii)) return "inside a magnetosphere";
            if (minSunDistanceAU > 0)
            {
                double au = FHSUtil.SunDistance(vessel) / FHSUtil.ReferenceSunDistance;
                if (au < minSunDistanceAU)
                    return string.Format("{0:0.00} of {1:0.00} Kerbin-distances from the sun", au, minSunDistanceAU);
            }
            if (minHomeDistance > 0)
            {
                double d = FHSUtil.HomeBodyDistance(vessel);
                if (d < minHomeDistance)
                    return string.Format("Kerbin radio noise ({0} of {1} needed)", FHSUtil.FormatDistance(d), FHSUtil.FormatDistance(minHomeDistance));
            }
            if (!experiment.IsAvailableWhile(sit, vessel.mainBody)) return "not useful in this situation";
            return null;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || experiment == null) return;
            double now = FHSUtil.Now;
            double dt = lastUT > 0 ? Math.Max(0, now - lastUT) : 0;
            lastUT = now;

            string why = CheckConditions();
            if (!running)
            {
                conditionsHeld = false;
                if (StorageFull) status = "Storage full";
                else if (surveyProgress > 0) status = string.Format("Stopped at {0:P0}", surveyProgress / RequiredSeconds);
                else status = why == null ? "Ready" : "Unavailable: " + why;
                return;
            }

            conditionsHeld = why == null;
            if (!conditionsHeld)
            {
                status = string.Format("Paused {0:P0}: {1}", surveyProgress / RequiredSeconds, why);
                return;
            }

            double need = ecPerSecond * dt;
            if (need > 0 && part.RequestResource(FHSUtil.ElectricCharge, need) < need * 0.95)
            {
                status = "Paused: no power";
                return;
            }

            surveyProgress += dt;
            status = string.Format("{0:P1}, {1} left", surveyProgress / RequiredSeconds, FHSUtil.FormatDuration(RequiredSeconds - surveyProgress));
            if (surveyProgress >= RequiredSeconds) CompleteSurvey(vessel == FlightGlobals.ActiveVessel);
        }

        private void CompleteSurvey(bool showDialog)
        {
            surveyProgress = 0;
            StopSurvey();
            ScienceData data = FHSUtil.CreateData(experiment, vessel, xmitDataScalar, part.flightID);
            FHSUtil.Message(experiment.experimentTitle + " complete!", 6f);
            if (showDialog)
                StoreAndReview(data);
            else
            {
                storedData.Add(data);
                UpdateDataEvents();
            }
        }
    }
}
