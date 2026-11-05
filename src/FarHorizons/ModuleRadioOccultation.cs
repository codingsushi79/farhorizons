using System;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Radio occultation: while orbiting a world with an atmosphere, the line of sight from the vessel
    /// to Kerbin must graze that atmosphere (i.e. Kerbin is setting or rising behind the planet's limb).
    /// The signal bends and fades through the air, which profiles it. Record enough grazing time to
    /// complete the experiment. Tip: don't use more than 10x time warp, or you'll skip past it.
    /// </summary>
    public class ModuleRadioOccultation : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsRadioOccultation";
        [KSPField] public double requiredSeconds = 20;
        [KSPField] public float ecPerSecond = 1.5f;

        [KSPField(isPersistant = true)] public bool armed;
        [KSPField(isPersistant = true)] public double recorded;
        [KSPField(isPersistant = true)] public string occultBody = "";

        [KSPField(guiActive = true, guiName = "Occultation")]
        public string status = "Off";

        private ScienceExperiment experiment;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            experiment = ResearchAndDevelopment.GetExperiment(experimentID);
            UpdateEvents();
        }

        public override string GetModuleDisplayName()
        {
            return "Radio Occultation";
        }

        public override string GetInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Records Kerbin's signal passing through another world's atmosphere as it sets behind the limb.");
            sb.AppendLine("Needs " + requiredSeconds.ToString("0") + " s of grazing geometry and an antenna on the vessel.");
            sb.AppendLine("Power: " + ecPerSecond.ToString("0.##") + " EC/s while recording");
            sb.AppendLine("<color=#ff9900>Use 10x time warp or less near occultation.</color>");
            return sb.ToString();
        }

        [KSPEvent(guiActive = true, guiName = "Arm Occultation Receiver")]
        public void Arm()
        {
            armed = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Disarm Occultation Receiver", active = false)]
        public void Disarm()
        {
            armed = false;
            UpdateEvents();
        }

        [KSPAction("Toggle Occultation Receiver")]
        public void ToggleAction(KSPActionParam p)
        {
            if (armed) Disarm();
            else Arm();
        }

        private void UpdateEvents()
        {
            Events["Arm"].active = !armed;
            Events["Disarm"].active = armed;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || experiment == null) return;
            if (!armed)
            {
                status = "Off";
                return;
            }

            CelestialBody body = vessel.mainBody;
            CelestialBody home = FlightGlobals.GetHomeBody();
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(vessel);
            if (home == null || body == home || body == FHSUtil.Sun || !body.atmosphere || !FHSUtil.IsInSpace(sit))
            {
                status = "Orbit a world with an atmosphere (not Kerbin)";
                return;
            }
            if (ScienceUtil.GetBestTransmitter(vessel) == null)
            {
                status = "Needs an antenna";
                return;
            }
            if (occultBody != body.name)
            {
                occultBody = body.name;
                recorded = 0;
            }

            double rayAltitude;
            string geometry = Geometry(body, home, out rayAltitude);
            if (geometry != null)
            {
                status = string.Format("{0:P0}: {1}", recorded / requiredSeconds, geometry);
                return;
            }

            float dt = TimeWarp.fixedDeltaTime;
            double need = ecPerSecond * dt;
            if (need > 0 && part.RequestResource(FHSUtil.ElectricCharge, need) < need * 0.95)
            {
                status = "No power!";
                return;
            }

            recorded += dt;
            status = string.Format("RECORDING {0:P0}, ray at {1:0.0} km", recorded / requiredSeconds, rayAltitude / 1000.0);
            if (recorded >= requiredSeconds) Complete(body, sit);
        }

        /// <summary>Null if the line of sight to home grazes the atmosphere; otherwise why not.</summary>
        private string Geometry(CelestialBody body, CelestialBody home, out double rayAltitude)
        {
            Vector3d p = vessel.GetWorldPos3D();
            Vector3d toHome = home.position - p;
            double len2 = Vector3d.Dot(toHome, toHome);
            double t = len2 > 0 ? Vector3d.Dot(body.position - p, toHome) / len2 : 0;
            rayAltitude = 0;
            if (t <= 0 || t >= 1) return "Kerbin in clear view";
            Vector3d closest = p + toHome * t;
            rayAltitude = (closest - body.position).magnitude - body.Radius;
            if (rayAltitude < 0) return "Kerbin hidden behind " + body.name;
            if (rayAltitude > body.atmosphereDepth) return string.Format("waiting, ray {0:0} km above the air", (rayAltitude - body.atmosphereDepth) / 1000.0);
            return null;
        }

        private void Complete(CelestialBody body, ExperimentSituations sit)
        {
            recorded = 0;
            armed = false;
            UpdateEvents();
            if (!experiment.IsAvailableWhile(sit, body) || StorageFull)
            {
                FHSUtil.Message("Occultation recorded, but there's nowhere to store it.");
                return;
            }
            ScienceData data = FHSUtil.CreateData(experiment, body, sit, string.Empty, xmitDataScalar, part.flightID);
            FHSUtil.Message("Radio occultation of " + body.name + " recorded!", 6f);
            if (vessel == FlightGlobals.ActiveVessel) StoreAndReview(data);
            else
            {
                storedData.Add(data);
                UpdateDataEvents();
            }
        }
    }
}
