using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FarHorizons
{
    internal delegate bool PoseProvider(out Vector3d worldPos, out Quaternion rotation, out float fov);

    internal class PhotoRequest
    {
        public int width = 1920;
        public int height = 1080;
        public PhotoFilter filter = PhotoFilter.Natural;
        public string baseName = "photo";
        public PoseProvider pose;
        /// <summary>Called with the saved file path (null on failure), after the UI is restored.</summary>
        public Action<string> onComplete;
    }

    /// <summary>Global photo settings from GameData/FarHorizons/Settings.cfg (FARHORIZONS_SETTINGS node).</summary>
    internal static class FHSSettings
    {
        private static bool loaded;
        private static string photoMode = "Auto";
        private static int settleFrames = 8;
        private static int superSize = 1;

        /// <summary>Use the game's real cameras so visual mods appear in photos.</summary>
        public static bool LivePhotos
        {
            get { Load(); return !string.Equals(photoMode, "Offscreen", StringComparison.OrdinalIgnoreCase); }
        }

        public static int SettleFrames
        {
            get { Load(); return Math.Max(1, Math.Min(settleFrames, 120)); }
        }

        public static int SuperSize
        {
            get { Load(); return Math.Max(1, Math.Min(superSize, 4)); }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (GameDatabase.Instance == null) return;
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("FARHORIZONS_SETTINGS"))
            {
                if (n.HasValue("photoMode")) photoMode = n.GetValue("photoMode").Trim();
                int i;
                if (n.HasValue("settleFrames") && int.TryParse(n.GetValue("settleFrames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) settleFrames = i;
                if (n.HasValue("superSize") && int.TryParse(n.GetValue("superSize"), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) superSize = i;
            }
        }
    }

    /// <summary>
    /// Takes photos through the game's REAL render pipeline, so everything that hooks KSP's own cameras
    /// (Scatterer atmospheres and oceans, EVE clouds, TUFX post-processing, etc.) shows up in the photo.
    ///
    /// How: for a handful of frames the stock cameras are posed at the science camera's position,
    /// rotation and FOV, with the UI hidden. The pose is applied in Application.onBeforeRender, after
    /// every LateUpdate (FlightCamera, ScaledCamera, mods) and before any camera culls, so per-camera
    /// mod callbacks see a consistent pose. FlightCamera is paused meanwhile so it doesn't fight us,
    /// and the pose persists between frames for mods that sample it in Update/LateUpdate. A few
    /// "settle" frames let temporal effects (volumetric clouds, TAA) converge. Then the finished frame
    /// is captured with ScreenCapture and everything is restored. The player sees a brief shutter blink.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FHSPhotoCapture : MonoBehaviour
    {
        private static FHSPhotoCapture instance;

        private static readonly string[] CameraNames = { "GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00" };

        private class SavedCamera
        {
            public string name;
            public Camera cam;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public float fov;
        }

        private readonly List<SavedCamera> saved = new List<SavedCamera>();
        private PhotoRequest active;
        private bool posing;
        private bool uiHidden;
        private bool flightCameraWasEnabled;
        private Vector3 flightCamLocalPos;
        private Quaternion flightCamLocalRot;

        /// <summary>True while a live capture is in progress (UI should stay hidden).</summary>
        public static bool Busy
        {
            get { return instance != null && instance.active != null; }
        }

        /// <summary>Live capture needs the normal flight view (not map view or IVA).</summary>
        public static bool LiveAvailable
        {
            get
            {
                if (instance == null || !FHSSettings.LivePhotos) return false;
                if (MapView.MapIsEnabled) return false;
                if (CameraManager.Instance == null || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) return false;
                return FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null;
            }
        }

        /// <summary>Starts a live capture. Returns false if unavailable or busy (caller should fall back).</summary>
        internal static bool TryCapture(PhotoRequest request)
        {
            if (!LiveAvailable || Busy || request == null || request.pose == null) return false;
            instance.active = request;
            instance.StartCoroutine(instance.Run());
            return true;
        }

        public void Awake()
        {
            instance = this;
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            Application.onBeforeRender += OnBeforeRender;
        }

        public void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            RestoreCameras();
            if (instance == this) instance = null;
        }

        private void OnHideUI()
        {
            uiHidden = true;
        }

        private void OnShowUI()
        {
            uiHidden = false;
        }

        private IEnumerator Run()
        {
            PhotoRequest req = active;
            bool weHidUI = !uiHidden;
            if (weHidUI) GameEvents.onHideUI.Fire();

            SaveCameras();
            posing = true;

            // Let the posed view render and settle (temporal effects need history).
            for (int i = 0; i < FHSSettings.SettleFrames; i++)
                yield return null;
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture(FHSSettings.SuperSize);
            }
            catch (Exception e)
            {
                FHSUtil.Log("Live capture failed: " + e);
            }

            posing = false;
            RestoreCameras();
            if (weHidUI) GameEvents.onShowUI.Fire();

            string path = null;
            if (shot != null)
            {
                path = FHSCameraRig.SaveTexture(shot, req.width, req.height, req.filter, req.baseName);
                Destroy(shot);
            }
            active = null;
            if (req.onComplete != null)
            {
                try { req.onComplete(path); }
                catch (Exception e) { FHSUtil.Log("Photo callback failed: " + e); }
            }
        }

        private void SaveCameras()
        {
            saved.Clear();
            foreach (string n in CameraNames)
            {
                Camera c = FindCamera(n);
                if (c == null) continue;
                saved.Add(new SavedCamera
                {
                    name = n,
                    cam = c,
                    localPosition = c.transform.localPosition,
                    localRotation = c.transform.localRotation,
                    fov = c.fieldOfView
                });
            }

            FlightCamera fc = FlightCamera.fetch;
            if (fc != null)
            {
                flightCameraWasEnabled = fc.enabled;
                flightCamLocalPos = fc.transform.localPosition;
                flightCamLocalRot = fc.transform.localRotation;
                fc.enabled = false;
            }
        }

        private void RestoreCameras()
        {
            foreach (SavedCamera s in saved)
            {
                if (s.cam == null) continue;
                s.cam.transform.localPosition = s.localPosition;
                s.cam.transform.localRotation = s.localRotation;
                s.cam.fieldOfView = s.fov;
            }
            if (saved.Count > 0)
            {
                FlightCamera fc = FlightCamera.fetch;
                if (fc != null)
                {
                    fc.transform.localPosition = flightCamLocalPos;
                    fc.transform.localRotation = flightCamLocalRot;
                    fc.enabled = flightCameraWasEnabled;
                }
            }
            saved.Clear();
        }

        private void OnBeforeRender()
        {
            if (!posing || active == null) return;
            Vector3d pos;
            Quaternion rot;
            float fov;
            if (!active.pose(out pos, out rot, out fov)) return;

            foreach (SavedCamera s in saved)
            {
                if (s.cam == null) continue;
                Transform t = s.cam.transform;
                if (s.name == "Camera ScaledSpace")
                    t.position = (Vector3)ScaledSpace.LocalToScaledSpace(pos);
                else if (s.name != "GalaxyCamera")
                    t.position = (Vector3)pos;
                // The galaxy skybox camera keeps its position; only its orientation matters.
                t.rotation = rot;
                s.cam.fieldOfView = fov;
            }
        }

        private static Camera FindCamera(string name)
        {
            foreach (Camera c in Camera.allCameras)
                if (c != null && c.name == name) return c;
            return null;
        }
    }
}
