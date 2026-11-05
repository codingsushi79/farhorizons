using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FarHorizons
{
    /// <summary>
    /// A science camera with a live viewfinder. Every photo is saved as a PNG in
    /// [KSP]/Screenshots/FarHorizons. What's in the frame decides what science (if any) it earns:
    ///   - landed: a surface panorama of the biome
    ///   - low orbit / high flight, pointed down: orbital imaging of the biome below
    ///   - a comet or asteroid close by and in frame: small-body close-up
    ///   - Kerbin in frame from very far away: the "distant portrait" of home
    ///   - the sun through the H-alpha filter: solar imaging (without it, narrow lenses burn out)
    ///   - any other body filling enough of the frame: telescopic imaging
    /// The camera looks along the part's "up" axis (away from the surface it's attached to).
    /// </summary>
    public class ModuleFHSCamera : FHSScienceModule
    {
        [KSPField] public string cameraName = "Camera";
        /// <summary>Field-of-view zoom steps in degrees, widest first.</summary>
        [KSPField] public string fovSteps = "60, 35, 20";
        [KSPField] public int photoWidth = 1920;
        [KSPField] public int photoHeight = 1080;
        [KSPField] public float cameraOffset = 0.4f;
        [KSPField] public float ecPerPhoto = 5f;
        [KSPField] public float viewfinderEcPerSecond = 0.2f;
        /// <summary>Comma-separated filters this camera carries.</summary>
        [KSPField] public string filters = "Natural, Enhanced, Monochrome, FalseColor";

        // Science capabilities (empty = disabled)
        [KSPField] public string panoramaExperimentID = "";
        [KSPField] public string orbitalExperimentID = "";
        [KSPField] public string telescopeExperimentID = "";
        [KSPField] public string solarExperimentID = "";
        [KSPField] public string cometExperimentID = "";
        [KSPField] public string asteroidExperimentID = "";
        [KSPField] public string homePortraitExperimentID = "";

        /// <summary>Fraction of the frame a body must span for telescopic imaging.</summary>
        [KSPField] public float minFrameFill = 0.15f;
        [KSPField] public float maxNadirAngle = 25f;
        [KSPField] public double smallBodyRange = 10000;
        /// <summary>Minimum distance from home for the distant portrait.</summary>
        [KSPField] public double homePortraitDistance = 50e9;
        /// <summary>Pointing a lens this narrow (deg) at the sun without H-alpha filter damages the sensor.</summary>
        [KSPField] public float sunDamageFov = 5f;

        [KSPField(isPersistant = true)] public int fovIndex;
        [KSPField(isPersistant = true)] public int filterIndex;
        [KSPField(isPersistant = true)] public bool sensorDamaged;
        [KSPField(isPersistant = true)] public int photoCount;

        [KSPField(guiActive = true, guiName = "In frame")]
        public string frameStatus = "";

        private const int PreviewW = 400, PreviewH = 225;

        private float[] fovs = { 60f };
        private PhotoFilter[] filterList = { PhotoFilter.Natural };
        private FHSCameraRig rig;
        private RenderTexture preview;
        private bool viewfinderOpen;
        private bool photoRequested;
        private Rect windowRect = new Rect(200, 120, PreviewW + 24, PreviewH + 190);
        private double nextAnalysis;
        private FrameInfo frame;

        private float Fov
        {
            get { return fovs[Mathf.Clamp(fovIndex, 0, fovs.Length - 1)]; }
        }

        private PhotoFilter Filter
        {
            get { return filterList[Mathf.Clamp(filterIndex, 0, filterList.Length - 1)]; }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            fovs = FHSUtil.SplitList(fovSteps, ',', s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (fovs.Length == 0) fovs = new[] { 60f };
            var fl = FHSUtil.SplitList(filters, ',', s => (PhotoFilter)Enum.Parse(typeof(PhotoFilter), s.Trim(), true));
            filterList = fl.Count > 0 ? fl.ToArray() : new[] { PhotoFilter.Natural };
            fovIndex = Mathf.Clamp(fovIndex, 0, fovs.Length - 1);
            filterIndex = Mathf.Clamp(filterIndex, 0, filterList.Length - 1);
            Events["OpenViewfinder"].guiName = "Open " + cameraName + " Viewfinder";
            UpdateCameraEvents();
        }

        public void OnDestroy()
        {
            CloseViewfinder();
        }

        public override string GetModuleDisplayName()
        {
            return "Science Camera";
        }

        public override string GetInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Field of view: " + fovSteps.Replace(",", "°,") + "°");
            sb.AppendLine("Photo: " + photoWidth + "x" + photoHeight + " PNG, saved to Screenshots/FarHorizons");
            sb.AppendLine("Filters: " + filters);
            sb.AppendLine("Power: " + ecPerPhoto.ToString("0.#") + " EC per photo");
            sb.AppendLine("<b>Science targets:</b>");
            if (panoramaExperimentID != "") sb.AppendLine("  - surface panoramas (landed)");
            if (orbitalExperimentID != "") sb.AppendLine("  - orbital imaging, pointed down");
            if (telescopeExperimentID != "") sb.AppendLine("  - distant bodies filling " + (minFrameFill * 100f).ToString("0") + "% of the frame");
            if (cometExperimentID != "" || asteroidExperimentID != "") sb.AppendLine("  - comet/asteroid close-ups (< " + FHSUtil.FormatDistance(smallBodyRange) + ")");
            if (homePortraitExperimentID != "") sb.AppendLine("  - Kerbin, from beyond " + FHSUtil.FormatDistance(homePortraitDistance));
            if (solarExperimentID != "") sb.AppendLine("  - the sun, with the H-alpha filter");
            if (sunDamageFov > 0 && fovs.Length > 0)
                sb.AppendLine("<color=#ff9900>Narrow lenses pointed at the sun without the H-alpha filter will burn out.</color>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        [KSPEvent(guiActive = true, guiName = "Open Viewfinder")]
        public void OpenViewfinder()
        {
            if (rig == null) rig = new FHSCameraRig();
            if (preview == null)
            {
                preview = new RenderTexture(PreviewW, PreviewH, 24, RenderTextureFormat.ARGB32);
                preview.Create();
            }
            viewfinderOpen = true;
            UpdateCameraEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Close Viewfinder", active = false)]
        public void CloseViewfinder()
        {
            viewfinderOpen = false;
            if (preview != null)
            {
                preview.Release();
                UnityEngine.Object.Destroy(preview);
                preview = null;
            }
            if (rig != null)
            {
                rig.Dispose();
                rig = null;
            }
            UpdateCameraEvents();
        }

        [KSPEvent(guiActive = true, guiName = "Take Photo")]
        public void TakePhoto()
        {
            photoRequested = true;
        }

        [KSPEvent(guiActive = true, guiName = "Zoom In")]
        public void ZoomIn()
        {
            fovIndex = Mathf.Min(fovIndex + 1, fovs.Length - 1);
        }

        [KSPEvent(guiActive = true, guiName = "Zoom Out")]
        public void ZoomOut()
        {
            fovIndex = Mathf.Max(fovIndex - 1, 0);
        }

        [KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 2f,
            guiName = "Replace Burned-Out Sensor", active = false)]
        public void RepairSensor()
        {
            Vessel eva = FlightGlobals.ActiveVessel;
            List<ProtoCrewMember> crew = eva != null ? eva.GetVesselCrew() : null;
            if (crew == null || crew.Count == 0 || crew[0].trait != "Engineer")
            {
                FHSUtil.Message("Only an engineer can replace the imaging sensor.");
                return;
            }
            sensorDamaged = false;
            FHSUtil.Message(crew[0].name + " replaced the " + cameraName + " sensor.");
            UpdateCameraEvents();
        }

        [KSPAction("Take Photo")]
        public void TakePhotoAction(KSPActionParam p)
        {
            TakePhoto();
        }

        [KSPAction("Toggle Viewfinder")]
        public void ToggleViewfinderAction(KSPActionParam p)
        {
            if (viewfinderOpen) CloseViewfinder();
            else OpenViewfinder();
        }

        private void UpdateCameraEvents()
        {
            Events["OpenViewfinder"].active = !viewfinderOpen;
            Events["CloseViewfinder"].active = viewfinderOpen;
            Events["ZoomIn"].active = fovs.Length > 1;
            Events["ZoomOut"].active = fovs.Length > 1;
            Events["RepairSensor"].active = sensorDamaged;
        }

        // ------------------------------------------------------------------
        // Camera pose
        // ------------------------------------------------------------------

        private Vector3 LookDirection
        {
            get { return part.transform.up; }
        }

        private Vector3d CameraPosition
        {
            get { return (Vector3d)(part.transform.position + LookDirection * cameraOffset); }
        }

        private Quaternion CameraRotation
        {
            get
            {
                Vector3 look = LookDirection;
                Vector3 up = (Vector3)(vessel.GetWorldPos3D() - vessel.mainBody.position).normalized;
                if (Mathf.Abs(Vector3.Dot(up, look)) > 0.98f) up = part.transform.forward;
                return Quaternion.LookRotation(look, up);
            }
        }

        // ------------------------------------------------------------------
        // Update loop
        // ------------------------------------------------------------------

        public void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;

            if (FHSUtil.Now >= nextAnalysis)
            {
                frame = AnalyzeFrame();
                frameStatus = frame.description;
                nextAnalysis = FHSUtil.Now + 0.25;
            }

            if (viewfinderOpen)
            {
                if (vessel != FlightGlobals.ActiveVessel)
                {
                    CloseViewfinder();
                    return;
                }
                double need = viewfinderEcPerSecond * Time.deltaTime;
                bool powered = need <= 0 || part.RequestResource(FHSUtil.ElectricCharge, need) >= need * 0.9;
                if (powered && !sensorDamaged && rig != null && preview != null && !FHSPhotoCapture.Busy)
                    rig.Render(preview, CameraPosition, CameraRotation, Fov);
            }

            if (photoRequested && !FHSPhotoCapture.Busy)
            {
                photoRequested = false;
                Shoot();
            }
        }

        private void Shoot()
        {
            if (sensorDamaged)
            {
                FHSUtil.Message(cameraName + " sensor is burned out. An engineer on EVA must replace it.");
                return;
            }
            if (ecPerPhoto > 0 && part.RequestResource(FHSUtil.ElectricCharge, (double)ecPerPhoto) < ecPerPhoto * 0.95)
            {
                FHSUtil.Message("Not enough power to take a photo.");
                return;
            }

            frame = AnalyzeFrame();
            if (frame.burnsSensor)
            {
                sensorDamaged = true;
                UpdateCameraEvents();
                FHSUtil.Message("The full sun through a narrow lens! The " + cameraName + " sensor is burned out.", 8f);
                return;
            }

            photoCount++;
            string name = string.Format("{0}_{1}_{2}_{3:D3}", vessel.vesselName, frame.subjectName ?? vessel.mainBody.name,
                DateTime.Now.ToString("yyyyMMdd-HHmmss"), photoCount);
            FrameInfo shot = frame;

            // Preferred: the real render pipeline, so Scatterer/EVE/TUFX etc. appear in the photo.
            var request = new PhotoRequest
            {
                width = photoWidth,
                height = photoHeight,
                filter = Filter,
                baseName = name,
                pose = GetPose,
                onComplete = path => FinishShot(path, shot)
            };
            if (FHSPhotoCapture.TryCapture(request)) return;

            // Fallback (map view, IVA, Offscreen mode): private cloned cameras.
            bool ownRig = rig == null;
            FHSCameraRig r = rig ?? new FHSCameraRig();
            string offscreenPath = r.Capture(photoWidth, photoHeight, CameraPosition, CameraRotation, Fov, Filter, name);
            if (ownRig) r.Dispose();
            FinishShot(offscreenPath, shot);
        }

        private bool GetPose(out Vector3d pos, out Quaternion rot, out float fov)
        {
            pos = default(Vector3d);
            rot = Quaternion.identity;
            fov = Fov;
            if (this == null || part == null || vessel == null || vessel.mainBody == null) return false;
            pos = CameraPosition;
            rot = CameraRotation;
            return true;
        }

        private void FinishShot(string path, FrameInfo shot)
        {
            if (path != null) FHSUtil.Message("Photo saved: Screenshots/FarHorizons/" + System.IO.Path.GetFileName(path), 6f);
            else FHSUtil.Message("Photo capture failed (see KSP.log).");
            if (this == null || part == null || vessel == null) return;

            if (shot.experiment == null)
            {
                FHSUtil.Message(shot.description + " (no new science)", 4f);
                return;
            }
            if (StorageFull)
            {
                FHSUtil.Message("Image storage full. Transmit or discard images to record science.");
                return;
            }
            ScienceData data = FHSUtil.CreateData(shot.experiment, shot.body, shot.situation, shot.biome, xmitDataScalar, part.flightID);
            if (HasSubject(data.subjectID))
            {
                FHSUtil.Message("Already holding an image of this.");
                return;
            }
            StoreAndReview(data);
        }

        // ------------------------------------------------------------------
        // What's in the frame?
        // ------------------------------------------------------------------

        private struct FrameInfo
        {
            public string description;
            public string subjectName;
            public ScienceExperiment experiment;
            public CelestialBody body;
            public ExperimentSituations situation;
            public string biome;
            public bool burnsSensor;
        }

        private static ScienceExperiment Exp(string id)
        {
            return string.IsNullOrEmpty(id) ? null : ResearchAndDevelopment.GetExperiment(id);
        }

        private bool Accept(ref FrameInfo f, ScienceExperiment exp, CelestialBody body, ExperimentSituations sit, string biome)
        {
            if (exp == null || !exp.IsAvailableWhile(sit, body)) return false;
            f.experiment = exp;
            f.body = body;
            f.situation = sit;
            f.biome = exp.BiomeIsRelevantWhile(sit) ? biome : string.Empty;
            return true;
        }

        private FrameInfo AnalyzeFrame()
        {
            var f = new FrameInfo { description = "Empty sky" };
            if (vessel == null || vessel.mainBody == null) return f;

            Vector3d pos = CameraPosition;
            Vector3d look = (Vector3d)LookDirection;
            float halfFov = Fov * 0.5f;
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(vessel);
            CelestialBody main = vessel.mainBody;

            // 1. Surface panorama
            if (vessel.LandedOrSplashed)
            {
                string biome = ScienceUtil.GetExperimentBiome(main, vessel.latitude, vessel.longitude);
                if (!string.IsNullOrEmpty(vessel.landedAt)) biome = Vessel.GetLandedAtString(vessel.landedAt);
                f.description = "Surface panorama: " + FHSUtil.BiomeDisplayName(main, biome);
                f.subjectName = main.name;
                Accept(ref f, Exp(panoramaExperimentID), main, sit, biome);
                return f;
            }

            // 2. Comet / asteroid close-up
            Vessel small = FindSmallBodyInFrame(pos, look, halfFov);
            if (small != null && FHSUtil.IsInSpace(sit))
            {
                bool comet = IsComet(small);
                f.description = (comet ? "Comet nucleus" : "Asteroid") + " at " + FHSUtil.FormatDistance((small.GetWorldPos3D() - pos).magnitude);
                f.subjectName = comet ? "Comet" : "Asteroid";
                Accept(ref f, Exp(comet ? cometExperimentID : asteroidExperimentID), main, sit, string.Empty);
                return f;
            }

            // 3. Celestial bodies
            CelestialBody best = null;
            double bestScore = double.MaxValue, bestAngle = 0, bestAngRadius = 0, bestDist = 0;
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                Vector3d to = b.position - pos;
                double d = to.magnitude;
                if (d <= b.Radius) continue;
                double angRadius = Math.Asin(Math.Min(1.0, b.Radius / d)) * Mathf.Rad2Deg;
                double angle = AngleDeg(look, to);
                if (angle > halfFov + angRadius) continue;
                if (Occluded(pos, b, d)) continue;
                double score = Math.Max(0, angle - angRadius); // 0 = body covers the center
                if (score < bestScore || (score == bestScore && angRadius > bestAngRadius))
                {
                    best = b;
                    bestScore = score;
                    bestAngle = angle;
                    bestAngRadius = angRadius;
                    bestDist = d;
                }
            }

            // Kerbin from deep space takes priority if it's in frame at all.
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home != null && homePortraitExperimentID != "")
            {
                Vector3d toHome = home.position - pos;
                if (toHome.magnitude >= homePortraitDistance && AngleDeg(look, toHome) <= halfFov && !Occluded(pos, home, toHome.magnitude))
                {
                    f.description = "Distant portrait of " + home.name + " from " + FHSUtil.FormatDistance(toHome.magnitude);
                    f.subjectName = home.name + "_from_afar";
                    Accept(ref f, Exp(homePortraitExperimentID), FHSUtil.Sun, ExperimentSituations.InSpaceHigh, string.Empty);
                    return f;
                }
            }

            if (best == null) return f;
            f.subjectName = best.name;
            double fill = 2.0 * bestAngRadius / Fov;

            // 4. The sun
            if (best == FHSUtil.Sun)
            {
                if (Filter == PhotoFilter.HydrogenAlpha)
                {
                    f.description = "Solar disk in H-alpha";
                    ExperimentSituations s = main == best && FHSUtil.IsInSpace(sit) ? sit : ExperimentSituations.InSpaceHigh;
                    Accept(ref f, Exp(solarExperimentID), best, s, string.Empty);
                }
                else
                {
                    f.description = "The sun! Too bright without a solar filter";
                    f.burnsSensor = sunDamageFov > 0 && Fov <= sunDamageFov && bestAngle < halfFov;
                }
                return f;
            }

            // 5. Orbital imaging of the body below
            if (best == main && (sit == ExperimentSituations.InSpaceLow || sit == ExperimentSituations.FlyingHigh))
            {
                double nadir = AngleDeg(look, main.position - pos);
                if (nadir <= maxNadirAngle)
                {
                    string biome = ScienceUtil.GetExperimentBiome(main, vessel.latitude, vessel.longitude);
                    f.description = "Ground track: " + FHSUtil.BiomeDisplayName(main, biome);
                    if (Accept(ref f, Exp(orbitalExperimentID), main, sit, biome)) return f;
                }
                else
                {
                    f.description = string.Format("{0} horizon ({1:0}° off nadir)", main.name, nadir);
                }
            }

            // 6. Telescopic imaging (of the body you're orbiting, only from high orbit)
            if (best == main && sit != ExperimentSituations.InSpaceHigh)
            {
                if (f.description == "Empty sky") f.description = main.name + " fills the view";
                return f;
            }
            if (fill >= minFrameFill)
            {
                f.description = string.Format("{0}, {1:P0} of frame", best.name, Math.Min(fill, 9.99));
                Accept(ref f, Exp(telescopeExperimentID), best, ExperimentSituations.InSpaceHigh, string.Empty);
            }
            else
            {
                f.description = string.Format("{0} ({1:P1} of frame, need {2:P0}: zoom in or get closer)",
                    best.name, fill, minFrameFill);
            }
            return f;
        }

        private Vessel FindSmallBodyInFrame(Vector3d pos, Vector3d look, double halfFov)
        {
            if (string.IsNullOrEmpty(cometExperimentID) && string.IsNullOrEmpty(asteroidExperimentID)) return null;
            Vessel best = null;
            double bestDist = smallBodyRange;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v == vessel || v.vesselType != VesselType.SpaceObject) continue;
                Vector3d to = v.GetWorldPos3D() - pos;
                double d = to.magnitude;
                if (d > bestDist) continue;
                // Allow a generous margin: the object is big and its origin isn't its visual center.
                double slack = Math.Atan2(200.0, Math.Max(d, 1.0)) * Mathf.Rad2Deg;
                if (AngleDeg(look, to) > halfFov + slack) continue;
                best = v;
                bestDist = d;
            }
            return best;
        }

        private static bool IsComet(Vessel v)
        {
            if (v.loaded)
            {
                foreach (Part p in v.parts)
                    if (p.Modules.Contains("ModuleComet")) return true;
                return false;
            }
            if (v.protoVessel == null) return false;
            foreach (ProtoPartSnapshot pp in v.protoVessel.protoPartSnapshots)
                foreach (ProtoPartModuleSnapshot m in pp.modules)
                    if (m.moduleName == "ModuleComet") return true;
            return false;
        }

        private static double AngleDeg(Vector3d a, Vector3d b)
        {
            double denom = a.magnitude * b.magnitude;
            if (denom <= 0) return 180;
            double c = Vector3d.Dot(a, b) / denom;
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * Mathf.Rad2Deg;
        }

        /// <summary>Is the line of sight to <paramref name="target"/> blocked by another body?</summary>
        private static bool Occluded(Vector3d pos, CelestialBody target, double targetDist)
        {
            Vector3d dir = (target.position - pos) / targetDist;
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == target) continue;
                Vector3d oc = b.position - pos;
                double t = Vector3d.Dot(oc, dir);
                if (t <= 0 || t >= targetDist) continue;
                Vector3d closest = pos + dir * t;
                if ((closest - b.position).magnitude < b.Radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Viewfinder window
        // ------------------------------------------------------------------

        public void OnGUI()
        {
            if (!viewfinderOpen || !HighLogic.LoadedSceneIsFlight || FHSPhotoCapture.Busy) return;
            windowRect = GUILayout.Window((int)(part.flightID & 0x7fffffff), windowRect, DrawWindow,
                vessel.vesselName + " - " + cameraName);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Box(GUIContent.none, GUILayout.Width(PreviewW), GUILayout.Height(PreviewH));
            Rect r = GUILayoutUtility.GetLastRect();
            if (sensorDamaged)
                GUI.Label(new Rect(r.x + 10, r.y + r.height / 2 - 10, r.width - 20, 20), "SENSOR BURNED OUT. Needs EVA engineer.");
            else if (preview != null)
            {
                GUI.DrawTexture(r, preview, ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(r.x + r.width / 2 - 6, r.y + r.height / 2 - 11, 20, 22), "+");
            }

            GUILayout.Label(frame.description ?? "");
            GUILayout.Label(frame.experiment != null
                ? "Science: " + frame.experiment.experimentTitle
                : "No science target in frame");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zoom -", GUILayout.Width(70))) ZoomOut();
            GUILayout.Label(string.Format("FOV {0:0.##}°", Fov), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Zoom +", GUILayout.Width(70))) ZoomIn();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) filterIndex = (filterIndex + filterList.Length - 1) % filterList.Length;
            GUILayout.Label("Filter: " + FHSCameraRig.FilterName(Filter) + " (applied on capture)", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30))) filterIndex = (filterIndex + 1) % filterList.Length;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Take Photo", GUILayout.Height(30))) TakePhoto();
            if (GUILayout.Button("Close", GUILayout.Width(70), GUILayout.Height(30))) CloseViewfinder();
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }
    }
}
