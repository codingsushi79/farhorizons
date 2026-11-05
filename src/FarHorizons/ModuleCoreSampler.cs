using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Bolts onto a surface drill (via ModuleManager). With the drill deployed and the vessel landed
    /// and stationary, drills a core for <see cref="drillDuration"/> seconds, consuming EC, and
    /// stores a physical sample. Moving, lifting off or retracting the drill ruins the core.
    /// </summary>
    public class ModuleCoreSampler : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsRegolithCore";
        [KSPField] public float drillDuration = 90f;
        [KSPField] public float ecPerSecond = 1f;
        [KSPField] public bool requireDeployedDrill = true;
        [KSPField] public float maxSurfaceSpeed = 0.25f;

        [KSPField(isPersistant = true)] public bool drilling;
        [KSPField(isPersistant = true)] public float drillProgress;

        [KSPField(guiActive = true, guiName = "Core Sampler")]
        public string status = "Idle";

        private ScienceExperiment experiment;
        private ModuleAnimationGroup drillAnimation;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            experiment = ResearchAndDevelopment.GetExperiment(experimentID);
            drillAnimation = part.FindModuleImplementing<ModuleAnimationGroup>();

            string title = experiment != null ? experiment.experimentTitle : experimentID;
            Fields["status"].guiName = title;
            Events["StartDrilling"].guiName = "Drill: " + title;
            Events["StopDrilling"].guiName = "Abort: " + title;
            Actions["StartDrillingAction"].guiName = "Drill: " + title;
            UpdateEvents();
        }

        public override string GetModuleDisplayName()
        {
            return "Core Sampler";
        }

        public override string GetInfo()
        {
            ScienceExperiment exp = ResearchAndDevelopment.GetExperiment(experimentID);
            var sb = new StringBuilder();
            sb.AppendLine("<b>" + (exp != null ? exp.experimentTitle : experimentID) + "</b>");
            sb.AppendLine("Drill time: " + FHSUtil.FormatDuration(drillDuration));
            sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s");
            sb.AppendLine("Requires: landed, stationary" + (requireDeployedDrill ? ", drill deployed" : ""));
            sb.AppendLine("Transmit value: " + (xmitDataScalar * 100f).ToString("0") + "%");
            sb.AppendLine("<color=#99ff00>Analyze in a sample lab for full transmittable results.</color>");
            return sb.ToString();
        }

        [KSPEvent(guiActive = true, guiName = "Drill Core Sample")]
        public void StartDrilling()
        {
            if (experiment == null)
            {
                FHSUtil.Message("Unknown experiment " + experimentID);
                return;
            }
            if (StorageFull)
            {
                FHSUtil.Message("Sample bay full. Transmit, transfer or discard some cores first.");
                return;
            }
            string why = CheckConditions();
            if (why != null)
            {
                FHSUtil.Message("Can't drill: " + why);
                return;
            }
            drilling = true;
            drillProgress = 0f;
            FHSUtil.Message(string.Format("Drilling {0}. Hold still for {1}.",
                experiment.experimentTitle, FHSUtil.FormatDuration(drillDuration)));
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Abort Core Drilling", active = false)]
        public void StopDrilling()
        {
            drilling = false;
            drillProgress = 0f;
            UpdateEvents();
        }

        [KSPAction("Drill Core Sample")]
        public void StartDrillingAction(KSPActionParam p)
        {
            if (!drilling) StartDrilling();
        }

        private void UpdateEvents()
        {
            Events["StartDrilling"].active = !drilling;
            Events["StopDrilling"].active = drilling;
        }

        private string CheckConditions()
        {
            if (vessel == null) return "no vessel";
            if (vessel.situation != Vessel.Situations.LANDED) return "must be landed on solid ground";
            if (vessel.srfSpeed > maxSurfaceSpeed) return "vessel is moving";
            if (requireDeployedDrill && drillAnimation != null && !drillAnimation.isDeployed) return "deploy the drill first";
            if (!experiment.IsAvailableWhile(ExperimentSituations.SrfLanded, vessel.mainBody)) return "nothing to sample here";
            return null;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || experiment == null) return;

            if (!drilling)
            {
                status = StorageFull ? "Sample bay full" : (CheckConditions() ?? "Ready");
                return;
            }

            string why = CheckConditions();
            if (why != null)
            {
                FHSUtil.Message("Core sample ruined: " + why + ".");
                StopDrilling();
                return;
            }

            float dt = TimeWarp.fixedDeltaTime;
            double need = ecPerSecond * dt;
            if (need > 0)
            {
                double got = part.RequestResource(FHSUtil.ElectricCharge, need);
                if (got < need * 0.95)
                {
                    status = "Stalled: no power";
                    return;
                }
            }

            drillProgress += dt;
            status = string.Format("Drilling {0:P0}", drillProgress / drillDuration);
            if (drillProgress >= drillDuration) CompleteCore();
        }

        private void CompleteCore()
        {
            drilling = false;
            drillProgress = 0f;
            UpdateEvents();

            ScienceData data = FHSUtil.CreateData(experiment, vessel, xmitDataScalar, part.flightID);
            if (HasSubject(data.subjectID))
            {
                FHSUtil.Message("This bay already holds a core from this exact site.");
                return;
            }
            StoreAndReview(data);
        }
    }
}
