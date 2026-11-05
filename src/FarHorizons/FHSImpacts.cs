using System;
using System.Collections.Generic;
using System.Text;

namespace FarHorizons
{
    /// <summary>
    /// Added to command parts by ModuleManager. When the part is destroyed hitting the ground hard
    /// enough, every landed seismometer (ModuleImpactListener) on that body records the impact,
    /// whether or not its vessel is loaded. Crash your spent stages on purpose!
    /// </summary>
    public class ModuleImpactor : PartModule
    {
        [KSPField] public double minImpactEnergyMJ = 5;
        [KSPField] public double fullImpactEnergyMJ = 60;
        [KSPField] public double maxImpactHeight = 150;

        private double lastSpeed, lastHeight, lastMass;
        private CelestialBody lastBody;

        private static double lastImpactUT = -1;
        private static string lastImpactBody = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsFlight) GameEvents.onPartDie.Add(OnPartDie);
        }

        public void OnDestroy()
        {
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        public override string GetModuleDisplayName()
        {
            return "Kinetic Impactor";
        }

        public override string GetInfo()
        {
            return "Crash this part into a world at " + minImpactEnergyMJ.ToString("0") +
                   "+ MJ and landed seismometers there will record the impact.\n(1 t at 100 m/s = 5 MJ)";
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || vessel.packed) return;
            lastBody = vessel.mainBody;
            lastSpeed = vessel.srfSpeed;
            lastMass = vessel.GetTotalMass();
            double ground = vessel.mainBody.ocean ? Math.Max(0, vessel.terrainAltitude) : vessel.terrainAltitude;
            lastHeight = vessel.altitude - ground;
        }

        private void OnPartDie(Part p)
        {
            if (p != part || lastBody == null || !HighLogic.LoadedSceneIsFlight) return;
            if (lastHeight > maxImpactHeight) return;

            double energyMJ = 0.5 * lastMass * 1000.0 * lastSpeed * lastSpeed / 1e6;
            if (energyMJ < minImpactEnergyMJ) return;

            // One impact event per body per couple of seconds, however many command parts break up.
            double now = FHSUtil.Now;
            if (lastImpactBody == lastBody.name && now - lastImpactUT < 3.0) return;
            lastImpactUT = now;
            lastImpactBody = lastBody.name;

            double fraction = Math.Max(0.25, Math.Min(1.0, energyMJ / fullImpactEnergyMJ));
            int heard = FHSImpacts.Broadcast(lastBody, fraction);
            FHSUtil.Message(heard > 0
                ? string.Format("Impact on {0}: {1:0.#} MJ! Recorded by {2} seismometer(s).", lastBody.name, energyMJ, heard)
                : string.Format("Impact on {0}: {1:0.#} MJ. No seismometers listening there.", lastBody.name, energyMJ), 8f);
        }
    }

    /// <summary>Seismometer component that records impacts (see <see cref="ModuleImpactor"/>).</summary>
    public class ModuleImpactListener : FHSScienceModule
    {
        [KSPField] public string experimentID = "fhsImpactSeismology";

        public override string GetModuleDisplayName()
        {
            return "Impact Listener";
        }

        public override string GetInfo()
        {
            return "Records seismic waves from impacts anywhere on the same world while landed, even when this vessel is far away.";
        }

        internal bool Receive(CelestialBody body, double fraction)
        {
            ScienceData data = FHSImpacts.MakeData(experimentID, body, fraction, part.flightID, xmitDataScalar);
            if (data == null || HasSubject(data.subjectID) || StorageFull) return false;
            storedData.Add(data);
            UpdateDataEvents();
            return true;
        }
    }

    internal static class FHSImpacts
    {
        private const string ListenerModule = "ModuleImpactListener";

        internal static ScienceData MakeData(string experimentID, CelestialBody body, double fraction, uint flightID, float xmit)
        {
            ScienceExperiment exp = ResearchAndDevelopment.GetExperiment(experimentID);
            if (exp == null || !exp.IsAvailableWhile(ExperimentSituations.SrfLanded, body)) return null;
            ScienceData data = FHSUtil.CreateData(exp, body, ExperimentSituations.SrfLanded, string.Empty, xmit, flightID);
            data.dataAmount *= (float)fraction;
            return data;
        }

        /// <summary>Delivers the impact to every landed listener on the body. Returns how many recorded it.</summary>
        public static int Broadcast(CelestialBody body, double fraction)
        {
            int count = 0;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.mainBody != body || v.situation != Vessel.Situations.LANDED) continue;

                if (v.loaded)
                {
                    foreach (ModuleImpactListener l in v.FindPartModulesImplementing<ModuleImpactListener>())
                        if (l.Receive(body, fraction)) count++;
                    continue;
                }

                if (v.protoVessel == null) continue;
                foreach (ProtoPartSnapshot pp in v.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot m in pp.modules)
                    {
                        if (m.moduleName != ListenerModule || m.moduleValues == null) continue;
                        if (DeliverToProto(m.moduleValues, body, fraction, pp.flightID)) count++;
                    }
                }
            }
            return count;
        }

        private static bool DeliverToProto(ConfigNode moduleNode, CelestialBody body, double fraction, uint flightID)
        {
            string expId = moduleNode.HasValue("experimentID") ? moduleNode.GetValue("experimentID") : "fhsImpactSeismology";
            ScienceData data = MakeData(expId, body, fraction, flightID, 1f);
            if (data == null) return false;
            foreach (ConfigNode existing in moduleNode.GetNodes("ScienceData"))
                if (existing.GetValue("subjectID") == data.subjectID) return false;
            data.Save(moduleNode.AddNode("ScienceData"));
            return true;
        }
    }
}
