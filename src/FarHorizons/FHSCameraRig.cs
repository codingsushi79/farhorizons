using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FarHorizons
{
    public enum PhotoFilter
    {
        Natural,
        Enhanced,
        Monochrome,
        FalseColor,
        HydrogenAlpha
    }

    /// <summary>
    /// Renders KSP's layered camera stack (galaxy skybox, scaled space planets, local scene) from an
    /// arbitrary point of view into a RenderTexture, and saves filtered PNG photos.
    /// </summary>
    internal class FHSCameraRig : IDisposable
    {
        private struct Layer
        {
            public string name;
            public Camera source;
            public Camera cam;
        }

        // Back to front. Missing cameras are skipped (KSP versions differ on "Camera 01").
        private static readonly string[] CameraNames = { "GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00" };

        private readonly List<Layer> layers = new List<Layer>();

        public FHSCameraRig()
        {
            foreach (string n in CameraNames)
            {
                Camera src = FindCamera(n);
                if (src == null) continue;
                var go = new GameObject("FarHorizons " + n);
                Camera c = go.AddComponent<Camera>();
                c.enabled = false;
                layers.Add(new Layer { name = n, source = src, cam = c });
            }
            if (layers.Count == 0) FHSUtil.Log("No flight cameras found; photos will be blank.");
        }

        public bool Valid
        {
            get { return layers.Count > 0; }
        }

        private static Camera FindCamera(string name)
        {
            foreach (Camera c in Camera.allCameras)
                if (c != null && c.name == name) return c;
            return null;
        }

        public void Render(RenderTexture rt, Vector3d worldPos, Quaternion rotation, float fov)
        {
            float aspect = (float)rt.width / rt.height;
            foreach (Layer l in layers)
            {
                if (l.source == null || l.cam == null) continue;
                Camera c = l.cam;
                c.CopyFrom(l.source);
                c.enabled = false;
                c.targetTexture = rt;
                c.fieldOfView = fov;
                c.aspect = aspect;
                if (l.name == "Camera ScaledSpace")
                    c.transform.position = (Vector3)ScaledSpace.LocalToScaledSpace(worldPos);
                else if (l.name == "GalaxyCamera")
                    c.transform.position = l.source.transform.position;
                else
                    c.transform.position = (Vector3)worldPos;
                c.transform.rotation = rotation;
                c.Render();
                c.targetTexture = null;
            }
        }

        public void Dispose()
        {
            foreach (Layer l in layers)
                if (l.cam != null) UnityEngine.Object.Destroy(l.cam.gameObject);
            layers.Clear();
        }

        // ------------------------------------------------------------------
        // Photo capture
        // ------------------------------------------------------------------

        public static string PhotoDirectory
        {
            get { return Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "Screenshots"), "FarHorizons"); }
        }

        /// <summary>Offscreen capture with the cloned cameras (no visual-mod effects). Returns the file path or null.</summary>
        public string Capture(int width, int height, Vector3d worldPos, Quaternion rotation, float fov, PhotoFilter filter, string baseName)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
            try
            {
                Render(rt, worldPos, rotation, fov);
                return SaveTexture(rt, width, height, filter, baseName);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Center-crops <paramref name="source"/> to the requested aspect ratio, scales it to fit
        /// (never upscaling past the source), applies the filter and writes a PNG.
        /// </summary>
        public static string SaveTexture(Texture source, int width, int height, PhotoFilter filter, string baseName)
        {
            float srcAspect = (float)source.width / source.height;
            float dstAspect = (float)width / height;
            Vector2 scale = new Vector2(1f, 1f);
            Vector2 offset = new Vector2(0f, 0f);
            if (srcAspect > dstAspect)
            {
                scale.x = dstAspect / srcAspect;
                offset.x = (1f - scale.x) * 0.5f;
            }
            else if (srcAspect < dstAspect)
            {
                scale.y = srcAspect / dstAspect;
                offset.y = (1f - scale.y) * 0.5f;
            }
            int cropW = Mathf.RoundToInt(source.width * scale.x);
            if (cropW < width)
            {
                height = Mathf.Max(1, Mathf.RoundToInt(height * (float)cropW / width));
                width = cropW;
            }

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            RenderTexture prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                Graphics.Blit(source, rt, scale, offset);
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                ApplyFilter(tex, filter);
                byte[] png = ImageConversion.EncodeToPNG(tex);

                Directory.CreateDirectory(PhotoDirectory);
                string path = Path.Combine(PhotoDirectory, Sanitize(baseName) + ".png");
                File.WriteAllBytes(path, png);
                return path;
            }
            catch (Exception e)
            {
                FHSUtil.Log("Saving photo failed: " + e);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ------------------------------------------------------------------
        // Filters
        // ------------------------------------------------------------------

        public static string FilterName(PhotoFilter f)
        {
            switch (f)
            {
                case PhotoFilter.Enhanced: return "Enhanced Color";
                case PhotoFilter.Monochrome: return "Monochrome";
                case PhotoFilter.FalseColor: return "False Color (IR)";
                case PhotoFilter.HydrogenAlpha: return "Solar H-alpha";
                default: return "Natural";
            }
        }

        private static readonly Color[] FalseColorRamp =
        {
            new Color(0.02f, 0.0f, 0.10f),
            new Color(0.35f, 0.0f, 0.55f),
            new Color(0.85f, 0.10f, 0.25f),
            new Color(1.00f, 0.55f, 0.00f),
            new Color(1.00f, 0.95f, 0.75f)
        };

        public static void ApplyFilter(Texture2D tex, PhotoFilter filter)
        {
            if (filter == PhotoFilter.Natural) return;
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                float lum = 0.299f * r + 0.587f * g + 0.114f * b;
                switch (filter)
                {
                    case PhotoFilter.Enhanced:
                        r = Contrast(lum + (r - lum) * 1.6f, 1.15f);
                        g = Contrast(lum + (g - lum) * 1.6f, 1.15f);
                        b = Contrast(lum + (b - lum) * 1.6f, 1.15f);
                        break;
                    case PhotoFilter.Monochrome:
                        r = g = b = Contrast(lum, 1.3f);
                        break;
                    case PhotoFilter.FalseColor:
                    {
                        Color c = Ramp(FalseColorRamp, Contrast(lum, 1.2f));
                        r = c.r; g = c.g; b = c.b;
                        break;
                    }
                    case PhotoFilter.HydrogenAlpha:
                        r = Mathf.Pow(lum, 0.7f);
                        g = lum * lum * 0.45f;
                        b = lum * lum * lum * 0.15f;
                        break;
                }
                px[i] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
        }

        private static float Contrast(float v, float k)
        {
            return Mathf.Clamp01((v - 0.5f) * k + 0.5f);
        }

        private static byte ToByte(float v)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        }

        private static Color Ramp(Color[] stops, float t)
        {
            t = Mathf.Clamp01(t) * (stops.Length - 1);
            int i = Mathf.Min((int)t, stops.Length - 2);
            return Color.Lerp(stops[i], stops[i + 1], t - i);
        }
    }
}
