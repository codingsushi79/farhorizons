using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarHorizons
{
    /// <summary>Shared helpers: environment queries, subject handling, messaging.</summary>
    internal static class FHSUtil
    {
        public const string ElectricCharge = "ElectricCharge";
        public const string LogPrefix = "[FarHorizons] ";

        // Kerbin's semi-major axis; only used if the home body can't be found.
        private const double FallbackHomeSMA = 13599840256.0;

        public static double Now
        {
            get { return Planetarium.GetUniversalTime(); }
        }

        public static void Log(string msg)
        {
            Debug.Log(LogPrefix + msg);
        }

        public static void Message(string msg, float seconds = 5f)
        {
            ScreenMessages.PostScreenMessage(msg, seconds, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------
        // Environment
        // ------------------------------------------------------------------

        public static CelestialBody Sun
        {
            get
            {
                if (Planetarium.fetch != null && Planetarium.fetch.Sun != null)
                    return Planetarium.fetch.Sun;
                return FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0 ? FlightGlobals.Bodies[0] : null;
            }
        }

        /// <summary>The home planet's orbital radius. All flux values are normalized to it (1.0 = "at Kerbin").</summary>
        public static double ReferenceSunDistance
        {
            get
            {
                CelestialBody home = FlightGlobals.GetHomeBody();
                return home != null && home.orbit != null ? home.orbit.semiMajorAxis : FallbackHomeSMA;
            }
        }

        public static double SunDistance(Vessel v)
        {
            CelestialBody sun = Sun;
            if (sun == null) return ReferenceSunDistance;
            return (v.GetWorldPos3D() - sun.position).magnitude;
        }

        public static double HomeBodyDistance(Vessel v)
        {
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home == null) return 0;
            return (v.GetWorldPos3D() - home.position).magnitude;
        }

        public static bool InSunSOI(Vessel v)
        {
            return v.mainBody != null && v.mainBody == Sun;
        }

        public static bool IsInSpace(ExperimentSituations s)
        {
            return s == ExperimentSituations.InSpaceLow || s == ExperimentSituations.InSpaceHigh;
        }

        /// <summary>
        /// Crude magnetosphere model: planets with an atmosphere are assumed to have a protective
        /// field reaching out to <paramref name="radii"/> planetary radii.
        /// </summary>
        public static bool InsideMagnetosphere(Vessel v, double radii)
        {
            CelestialBody b = v.mainBody;
            if (b == null || b == Sun || !b.atmosphere) return false;
            return v.altitude < b.Radius * radii;
        }

        /// <summary>True if a comet (KSP 1.10+) is within <paramref name="range"/> meters.</summary>
        public static bool NearComet(Vessel v, double range)
        {
            if (FlightGlobals.Vessels == null) return false;
            Vector3d here = v.GetWorldPos3D();
            double rangeSq = range * range;
            for (int i = 0; i < FlightGlobals.Vessels.Count; i++)
            {
                Vessel other = FlightGlobals.Vessels[i];
                if (other == null || other == v || other.vesselType != VesselType.SpaceObject) continue;
                if ((other.GetWorldPos3D() - here).sqrMagnitude > rangeSq) continue;
                if (IsComet(other)) return true;
            }
            return false;
        }

        private static bool IsComet(Vessel o)
        {
            if (o.loaded)
            {
                for (int i = 0; i < o.parts.Count; i++)
                    if (o.parts[i].Modules.Contains("ModuleComet")) return true;
                return false;
            }
            if (o.protoVessel == null) return false;
            foreach (ProtoPartSnapshot pp in o.protoVessel.protoPartSnapshots)
                foreach (ProtoPartModuleSnapshot m in pp.modules)
                    if (m.moduleName == "ModuleComet") return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Science subjects
        // ------------------------------------------------------------------

        public static string CurrentBiome(Vessel v, ScienceExperiment exp, ExperimentSituations sit)
        {
            if (!exp.BiomeIsRelevantWhile(sit)) return string.Empty;
            if (!string.IsNullOrEmpty(v.landedAt)) return Vessel.GetLandedAtString(v.landedAt);
            return ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
        }

        public static string BiomeDisplayName(CelestialBody body, string biome)
        {
            if (string.IsNullOrEmpty(biome)) return string.Empty;
            try
            {
                string display = ScienceUtil.GetBiomedisplayName(body, biome);
                return string.IsNullOrEmpty(display) ? biome : display;
            }
            catch (Exception)
            {
                return biome;
            }
        }

        public static ScienceData CreateData(ScienceExperiment exp, CelestialBody body, ExperimentSituations sit,
            string biome, float xmitScalar, uint flightID)
        {
            ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(exp, sit, body, biome, BiomeDisplayName(body, biome));
            float amount = exp.baseValue * exp.dataScale;
            return new ScienceData(amount, xmitScalar, 0f, subject.id, subject.title, false, flightID);
        }

        public static ScienceData CreateData(ScienceExperiment exp, Vessel v, float xmitScalar, uint flightID)
        {
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(v);
            return CreateData(exp, v.mainBody, sit, CurrentBiome(v, exp, sit), xmitScalar, flightID);
        }

        public static string ExperimentIdOf(string subjectID)
        {
            if (string.IsNullOrEmpty(subjectID)) return string.Empty;
            int at = subjectID.IndexOf('@');
            return at < 0 ? subjectID : subjectID.Substring(0, at);
        }

        /// <summary>
        /// Splits a subject id ("exp@KerbinSrfLandedHighlands") back into body / situation / biome,
        /// so a derived experiment (e.g. lab analysis) can be filed under the same location.
        /// </summary>
        public static bool TryParseSubject(string subjectID, out CelestialBody body, out ExperimentSituations situation, out string biome)
        {
            body = null;
            situation = ExperimentSituations.SrfLanded;
            biome = string.Empty;

            int at = subjectID == null ? -1 : subjectID.IndexOf('@');
            if (at < 0) return false;
            string rest = subjectID.Substring(at + 1);

            int bestLen = 0;
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                string n = b.name;
                if (!string.IsNullOrEmpty(n) && n.Length > bestLen && rest.StartsWith(n, StringComparison.Ordinal))
                {
                    body = b;
                    bestLen = n.Length;
                }
            }
            if (body == null) return false;
            rest = rest.Substring(bestLen);

            foreach (ExperimentSituations s in Enum.GetValues(typeof(ExperimentSituations)))
            {
                string sn = s.ToString();
                if (rest.StartsWith(sn, StringComparison.Ordinal))
                {
                    situation = s;
                    biome = RestoreBiomeName(body, rest.Substring(sn.Length));
                    return true;
                }
            }
            return false;
        }

        // Subject ids have spaces stripped from biome names; map back to the real name when possible.
        private static string RestoreBiomeName(CelestialBody body, string stripped)
        {
            if (string.IsNullOrEmpty(stripped) || body.BiomeMap == null || body.BiomeMap.Attributes == null)
                return stripped;
            foreach (CBAttributeMapSO.MapAttribute attr in body.BiomeMap.Attributes)
            {
                if (attr != null && attr.name != null && attr.name.Replace(" ", string.Empty) == stripped)
                    return attr.name;
            }
            return stripped;
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
            // Kerbin day = 6h; use GameSettings-agnostic d/h/m formatting.
            long s = (long)Math.Round(seconds);
            long day = KerbinTime ? 21600 : 86400;
            long d = s / day; s %= day;
            long h = s / 3600; s %= 3600;
            long m = s / 60; s %= 60;
            if (d > 0) return string.Format("{0}d {1}h {2}m", d, h, m);
            if (h > 0) return string.Format("{0}h {1}m", h, m);
            if (m > 0) return string.Format("{0}m {1}s", m, s);
            return string.Format("{0}s", s);
        }

        private static bool KerbinTime
        {
            get { return GameSettings.KERBIN_TIME; }
        }

        public static string FormatDistance(double meters)
        {
            if (meters >= 1e9) return (meters / 1e9).ToString("0.00") + " Gm";
            if (meters >= 1e6) return (meters / 1e6).ToString("0.0") + " Mm";
            if (meters >= 1e3) return (meters / 1e3).ToString("0.0") + " km";
            return meters.ToString("0") + " m";
        }

        public static List<T> SplitList<T>(string s, char sep, Func<string, T> parse)
        {
            var list = new List<T>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (string raw in s.Split(sep))
            {
                string t = raw.Trim();
                if (t.Length > 0) list.Add(parse(t));
            }
            return list;
        }
    }

    /// <summary>Plays an optional deploy animation on a part's model, forwards or backwards.</summary>
    internal class FHSAnimator
    {
        private readonly string animName;
        private readonly Animation[] animators;

        public FHSAnimator(Part part, string animationName)
        {
            animName = animationName;
            animators = string.IsNullOrEmpty(animationName) ? new Animation[0] : part.FindModelAnimators(animationName);
            if (animators == null) animators = new Animation[0];
        }

        public void Set(bool deployed, bool instant)
        {
            foreach (Animation a in animators)
            {
                AnimationState st = a[animName];
                if (st == null) continue;
                st.wrapMode = WrapMode.ClampForever;
                st.speed = deployed ? 1f : -1f;
                if (instant)
                    st.normalizedTime = deployed ? 1f : 0f;
                else if (!a.IsPlaying(animName))
                    st.normalizedTime = deployed ? 0f : 1f;
                a.Play(animName);
            }
        }
    }
}
