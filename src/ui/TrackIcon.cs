using System;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// The icon shown on each Track button. Ships bundled next to the mod DLL as
    /// <c>track-icon.png</c>; a file at <c>BepInEx/config/DeadReckoning/track-icon.png</c> overrides it
    /// so custom art can be swapped in without touching the mod folder. Returns null when neither is
    /// present/readable; the button falls back to a text label then. The PNG scales to the button rect.
    /// </summary>
    internal static class TrackIcon
    {
        private const string FileName = "track-icon.png";

        private static Sprite cached;
        private static bool tried;

        internal static Sprite Get()
        {
            if (tried) return cached;
            tried = true;

            try
            {
                string path = ResolvePath();
                if (path == null)
                {
                    DeadReckoningPlugin.Log.LogInfo("Track button: no track-icon.png in the mod folder or config - using a text label.");
                    return null;
                }

                byte[] data = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "DeadReckoning_TrackIcon",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

                if (ImageConversion.LoadImage(texture, data)) // resizes to the PNG's dimensions, keeps alpha
                {
                    cached = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    DeadReckoningPlugin.Log.LogInfo($"Track button: loaded custom icon {texture.width}x{texture.height} from '{path}'.");
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    DeadReckoningPlugin.Log.LogWarning($"Track button: '{path}' is not a decodable PNG - using a text label.");
                }
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Track button: failed to load custom icon: {e}");
            }

            return cached;
        }

        /// <summary>Config override first (user art), then the bundled copy next to the mod DLL.</summary>
        private static string ResolvePath()
        {
            string configPath = Path.Combine(Paths.ConfigPath, "DeadReckoning", FileName);
            if (File.Exists(configPath)) return configPath;

            string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string modPath = modDir != null ? Path.Combine(modDir, FileName) : null;
            if (modPath != null && File.Exists(modPath)) return modPath;

            return null;
        }
    }
}
