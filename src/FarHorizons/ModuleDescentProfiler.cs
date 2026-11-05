using System;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Atmospheric entry probe. Once armed it logs every altitude band it falls through. Science scales
    /// with how much of the atmosphere was profiled. If the probe is about to cook, it fires off whatever
    /// it has through the best antenna, so you get the data even if it doesn't survive.
    /// </summary>
    public class ModuleDescentProfiler : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsDescentProfile";
        [KSPField] public int bands = 20;
        /// <summary>Fraction of bands needed for a full-value profile.</summary>
        [KSPField] public float fullCoverage = 0.7f;
        [KSPField] public float ecPerSecond = 0.3f;
        [KSPField] public bool autoTransmit = true;
        /// <summary>Emergency transmit when part temperature exceeds this fraction of its limit.</summary>
        [KSPField] public float emergencyHeatFraction = 0.9f;

        [KSPField(isPersistant = true)] public bool armed;
        [KSPField(isPersistant = true)] public int bandMask;
        [KSPField(isPersistant = true)] public string profileBody = "";

        [KSPField(guiActive = true, guiName = "Descent Profile")]
        public string status = "Stowed";

        private ScienceExperiment experiment;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            experiment = ResearchAndDevelopment.GetExperiment(experimentID);
            bands = Math.Max(4, Math.Min(bands, 30));
            UpdateEvents();
        }

        public override string GetModuleDisplayName()
        {
            return "Descent Profiler";
        }

        public override string GetInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Logs temperature, pressure and composition through " + bands + " altitude bands while descending.");
            sb.AppendLine("Full value after " + (fullCoverage * 100f).ToString("0") + "% of the atmosphere; partial profiles are worth partial science.");
            sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s while recording");
            if (autoTransmit)
                sb.AppendLine("<color=#99ff00>Automatically transmits before the probe overheats.</color>");
            return sb.ToString();
        }

        private float Coverage
        {
            get
            {
                int n = 0;
                for (int i = 0; i < bands; i++) if ((bandMask & (1 << i)) != 0) n++;
                return (float)n / bands;
            }
        }

        [KSPEvent(guiActive = true, guiName = "Arm Descent Profiler")]
        public void Arm()
        {
            armed = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Disarm Descent Profiler", active = false)]
        public void Disarm()
        {
            armed = false;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Finish Profile Now", active = false)]
        public void FinishNow()
        {
            Finish(false);
        }

        [KSPAction("Arm Descent Profiler")]
        public void ArmAction(KSPActionParam p)
        {
            Arm();
        }

        [KSPAction("Finish Profile Now")]
        public void FinishAction(KSPActionParam p)
        {
            FinishNow();
        }

        private void UpdateEvents()
        {
            Events["Arm"].active = !armed;
            Events["Disarm"].active = armed;
            Events["FinishNow"].active = bandMask != 0;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || experiment == null) return;
            CelestialBody body = vessel.mainBody;

            if (!armed)
            {
                status = bandMask != 0 ? string.Format("Holding {0:P0} profile", Coverage) : "Stowed";
                return;
            }
            if (!body.atmosphere)
            {
                status = "Armed: no atmosphere here";
                return;
            }
            if (profileBody != body.name)
            {
                profileBody = body.name;
                bandMask = 0;
            }
            if (vessel.LandedOrSplashed)
            {
                if (bandMask != 0) Finish(false);
                status = "Landed";
                return;
            }
            if (vessel.altitude > body.atmosphereDepth)
            {
                status = "Armed: waiting for entry";
                return;
            }
            if (vessel.verticalSpeed > -1)
            {
                status = string.Format("{0:P0}: not descending", Coverage);
                return;
            }

            double need = ecPerSecond * TimeWarp.fixedDeltaTime;
            if (need > 0 && part.RequestResource(FHSUtil.ElectricCharge, need) < need * 0.95)
            {
                status = "No power!";
                return;
            }

            int band = (int)(vessel.altitude / body.atmosphereDepth * bands);
            band = Math.Max(0, Math.Min(bands - 1, band));
            bandMask |= 1 << band;
            UpdateEvents();
            status = string.Format("Recording {0:P0}, {1:0.0} km", Coverage, vessel.altitude / 1000.0);

            if (Coverage >= fullCoverage)
            {
                FHSUtil.Message("Descent profile of " + body.name + " complete!", 5f);
                Finish(true);
                return;
            }

            if (autoTransmit && Overheating())
            {
                FHSUtil.Message("Probe overheating! Emergency transmission of descent profile.", 6f);
                Finish(true);
            }
        }

        private bool Overheating()
        {
            return part.temperature > part.maxTemp * emergencyHeatFraction ||
                   part.skinTemperature > part.skinMaxTemp * emergencyHeatFraction;
        }

        private void Finish(bool transmit)
        {
            float coverage = Coverage;
            bandMask = 0;
            armed = false;
            UpdateEvents();
            if (coverage <= 0 || experiment == null) return;

            CelestialBody body = vessel.mainBody;
            if (!experiment.IsAvailableWhile(ExperimentSituations.FlyingLow, body)) return;
            ScienceData data = FHSUtil.CreateData(experiment, body, ExperimentSituations.FlyingLow, string.Empty, xmitDataScalar, part.flightID);
            float fraction = Math.Min(1f, coverage / fullCoverage);
            data.dataAmount *= fraction;
            if (fraction < 1f) data.title += string.Format(" (partial, {0:P0})", fraction);

            if (transmit && autoTransmit)
            {
                IScienceDataTransmitter t = ScienceUtil.GetBestTransmitter(vessel);
                if (t != null)
                {
                    t.TransmitData(new System.Collections.Generic.List<ScienceData> { data });
                    return;
                }
                FHSUtil.Message("No antenna! Descent profile stored on board instead.");
            }
            storedData.Add(data);
            UpdateDataEvents();
            if (vessel == FlightGlobals.ActiveVessel) ReviewDataItem(data);
        }
    }
}
