using System;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Passive collector that must be deployed in space and left exposed until enough material has
    /// accumulated, then sealed into a canister for analysis or return. Inspired by Genesis
    /// (solar wind) and Stardust (aerogel dust capture).
    ///
    /// Modes:
    ///   SolarWind - rate scales with (reference distance / sun distance)^2. Blocked inside magnetospheres.
    ///   Dust      - steady in interplanetary space, slower inside planetary SOIs. Near a comet the
    ///               collector instead fills with cometary material (nearCometExperimentID).
    ///
    /// Leaving a collector deployed inside an atmosphere contaminates it and wipes the sample.
    /// Exposure keeps accumulating while the vessel is unloaded.
    /// </summary>
    public class ModuleExposureCollector : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsSolarWind";
        [KSPField] public string nearCometExperimentID = "";
        [KSPField] public string collectorMode = "SolarWind";
        /// <summary>Exposure needed, in hours at reference flux (1.0 = in Kerbin's orbit around the sun).</summary>
        [KSPField] public double requiredHours = 12;
        [KSPField] public float ecPerSecond = 0f;
        [KSPField] public string animationName = "";
        [KSPField] public double magnetosphereRadii = 10;
        [KSPField] public double magnetosphereShielding = 0.02;
        [KSPField] public double planetarySOIDustFactor = 0.3;
        [KSPField] public double cometRange = 50000;
        [KSPField] public double cometMultiplier = 6;
        /// <summary>Static pressure (kPa) above which a deployed collector is contaminated.</summary>
        [KSPField] public double contaminationPressure = 0.001;

        [KSPField(isPersistant = true)] public bool deployed;
        [KSPField(isPersistant = true)] public double exposure;      // seconds at reference flux
        [KSPField(isPersistant = true)] public double cometExposure;
        [KSPField(isPersistant = true)] public double lastUT = -1;

        [KSPField(guiActive = true, guiName = "Collected")]
        public string collectedDisplay = "";

        [KSPField(guiActive = true, guiName = "Flux")]
        public string fluxDisplay = "";

        private ScienceExperiment experiment;
        private ScienceExperiment cometExperiment;
        private FHSAnimator animator;
        private bool nearComet;
        private double nextCometCheck;

        private bool DustMode
        {
            get { return string.Equals(collectorMode, "Dust", StringComparison.OrdinalIgnoreCase); }
        }

        private double RequiredSeconds
        {
            get { return requiredHours * 3600.0; }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            experiment = ResearchAndDevelopment.GetExperiment(experimentID);
            if (!string.IsNullOrEmpty(nearCometExperimentID))
                cometExperiment = ResearchAndDevelopment.GetExperiment(nearCometExperimentID);
            animator = new FHSAnimator(part, animationName);
            animator.Set(deployed, true);

            string title = experiment != null ? experiment.experimentTitle : experimentID;
            Fields["collectedDisplay"].guiName = title;
            Fields["fluxDisplay"].guiActive = HighLogic.LoadedSceneIsFlight;

            if (HighLogic.LoadedSceneIsFlight && deployed && lastUT > 0)
            {
                double elapsed = FHSUtil.Now - lastUT;
                if (elapsed > 1 && Contaminant() == null)
                {
                    nearComet = DustMode && cometExperiment != null && FHSUtil.NearComet(vessel, cometRange);
                    Accumulate(CurrentRate() * elapsed);
                }
            }
            lastUT = FHSUtil.Now;
            UpdateEvents();
        }

        public override string GetModuleDisplayName()
        {
            return DustMode ? "Dust Collector" : "Solar Wind Collector";
        }

        public override string GetInfo()
        {
            ScienceExperiment exp = ResearchAndDevelopment.GetExperiment(experimentID);
            var sb = new StringBuilder();
            sb.AppendLine("<b>" + (exp != null ? exp.experimentTitle : experimentID) + "</b>");
            sb.AppendLine("Exposure: " + requiredHours.ToString("0.#") + " h at Kerbin-orbit flux");
            if (DustMode)
            {
                sb.AppendLine("Best in interplanetary space; x" + planetarySOIDustFactor.ToString("0.##") + " inside a planet's SOI.");
                if (!string.IsNullOrEmpty(nearCometExperimentID))
                    sb.AppendLine("<color=#99ff00>Within " + FHSUtil.FormatDistance(cometRange) + " of a comet it captures coma particles instead.</color>");
            }
            else
            {
                sb.AppendLine("Flux grows with the inverse square of distance to the sun.");
                sb.AppendLine("Almost useless inside a planetary magnetosphere.");
            }
            if (ecPerSecond > 0) sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s");
            sb.AppendLine("<color=#ff9900>Retract before entering an atmosphere or the sample is contaminated.</color>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Deploy Collector")]
        public void Deploy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                string c = Contaminant();
                if (c != null)
                {
                    FHSUtil.Message("Deploying now would contaminate the collector: " + c + ".");
                    return;
                }
                if (StorageFull)
                {
                    FHSUtil.Message("Canister storage full. Transmit, transfer or discard data first.");
                    return;
                }
            }
            deployed = true;
            lastUT = FHSUtil.Now;
            animator.Set(true, !HighLogic.LoadedSceneIsFlight);
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Retract Collector", active = false)]
        public void Retract()
        {
            deployed = false;
            animator.Set(false, !HighLogic.LoadedSceneIsFlight);
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Seal Sample Canister", active = false)]
        public void Seal()
        {
            ScienceExperiment exp;
            if (cometExperiment != null && cometExposure >= RequiredSeconds)
                exp = cometExperiment;
            else if (exposure >= RequiredSeconds)
                exp = experiment;
            else
            {
                FHSUtil.Message("Not enough material collected yet.");
                return;
            }

            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(vessel);
            if (!FHSUtil.IsInSpace(sit) || !exp.IsAvailableWhile(sit, vessel.mainBody))
            {
                FHSUtil.Message("Seal the canister while still in space.");
                return;
            }

            ScienceData data = FHSUtil.CreateData(exp, vessel, xmitDataScalar, part.flightID);
            if (exp == cometExperiment) cometExposure = 0;
            else exposure = 0;
            Retract();
            StoreAndReview(data);
        }

        [KSPAction("Deploy Collector")]
        public void DeployAction(KSPActionParam p)
        {
            Deploy();
        }

        [KSPAction("Retract Collector")]
        public void RetractAction(KSPActionParam p)
        {
            Retract();
        }

        [KSPAction("Toggle Collector")]
        public void ToggleAction(KSPActionParam p)
        {
            if (deployed) Retract();
            else Deploy();
        }

        private void UpdateEvents()
        {
            Events["Deploy"].active = !deployed;
            Events["Retract"].active = deployed;
            bool ready = exposure >= RequiredSeconds || (cometExperiment != null && cometExposure >= RequiredSeconds);
            Events["Seal"].active = ready && HighLogic.LoadedSceneIsFlight;
        }

        // ------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------

        private string Contaminant()
        {
            if (vessel == null) return null;
            if (vessel.mainBody != null && vessel.mainBody.atmosphere && vessel.staticPressurekPa > contaminationPressure)
                return "atmosphere";
            if (vessel.LandedOrSplashed) return "surface dust";
            return null;
        }

        /// <summary>Collection rate relative to reference conditions (1.0 = solar orbit at Kerbin's distance).</summary>
        private double CurrentRate()
        {
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(vessel);
            if (!FHSUtil.IsInSpace(sit)) return 0;

            if (DustMode)
            {
                double rate = FHSUtil.InSunSOI(vessel) ? 1.0 : planetarySOIDustFactor;
                if (nearComet) rate *= cometMultiplier;
                return rate;
            }

            double ratio = FHSUtil.ReferenceSunDistance / Math.Max(FHSUtil.SunDistance(vessel), 1.0);
            double flux = ratio * ratio;
            if (FHSUtil.InsideMagnetosphere(vessel, magnetosphereRadii)) flux *= magnetosphereShielding;
            return Math.Min(flux, 1000.0);
        }

        private void Accumulate(double amount)
        {
            if (nearComet) cometExposure += amount;
            else exposure += amount;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
            double now = FHSUtil.Now;
            double dt = lastUT > 0 ? Math.Max(0, now - lastUT) : 0;
            lastUT = now;

            if (DustMode && cometExperiment != null && now >= nextCometCheck)
            {
                nearComet = FHSUtil.NearComet(vessel, cometRange);
                nextCometCheck = now + 2.0;
            }

            double rate = CurrentRate();
            fluxDisplay = rate <= 0 ? "none" : "x" + rate.ToString(rate < 0.1 ? "0.000" : "0.00") + (nearComet ? " (comet!)" : "");

            if (deployed)
            {
                string c = Contaminant();
                if (c != null)
                {
                    if (exposure > 0 || cometExposure > 0)
                        FHSUtil.Message(part.partInfo.title + " contaminated by " + c + "! Collected material lost.", 8f);
                    exposure = cometExposure = 0;
                    Retract();
                }
                else if (dt > 0 && rate > 0)
                {
                    double need = ecPerSecond * dt;
                    if (need <= 0 || part.RequestResource(FHSUtil.ElectricCharge, need) >= need * 0.95)
                        Accumulate(rate * dt);
                }
            }

            UpdateDisplay(rate);
            UpdateEvents();
        }

        private void UpdateDisplay(double rate)
        {
            double req = RequiredSeconds;
            if (cometExperiment != null && (nearComet || cometExposure > 0))
            {
                collectedDisplay = string.Format("Coma {0:P0} / IPD {1:P0}", Math.Min(1, cometExposure / req), Math.Min(1, exposure / req));
                return;
            }
            double frac = Math.Min(1.0, exposure / req);
            if (frac >= 1.0) collectedDisplay = "Ready to seal";
            else if (deployed && rate > 0) collectedDisplay = string.Format("{0:P1} ({1} left)", frac, FHSUtil.FormatDuration((req - exposure) / rate));
            else collectedDisplay = string.Format("{0:P1}{1}", frac, deployed ? " (no flux)" : " (stowed)");
        }
    }
}
