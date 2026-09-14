using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// The icon shown on each Track button. Resolution order:
    ///   1. a user-supplied PNG at <c>BepInEx/config/DeadReckoning/track-icon.png</c> - custom art
    ///      swapped in without a rebuild;
    ///   2. the built-in pin icon, embedded in the DLL so it ships with the plugin (the
    ///      release zip bundles only the DLL, so a bundled default has to live inside it).
    /// Returns null only if the embedded resource is missing/undecodable, which shouldn't happen in a
    /// real build; the button falls back to a text label then. The PNG scales to the button rect.
    /// </summary>
    internal static class TrackIcon
    {
        private const string FileName = "track-icon.png";

        // Pinned by <LogicalName> in the .csproj, so it does not depend on the file's on-disk folder.
        private const string EmbeddedResourceName = "DeadReckoning.track-icon.png";

        private static Sprite cached;          // the user-supplied override, once resolved
        private static bool tried;

        private static Sprite embeddedDefault; // the built-in pin icon, once loaded
        private static bool embeddedTried;

        internal static Sprite Get()
        {
            if (tried) return cached != null ? cached : DefaultIcon();
            tried = true;

            try
            {
                string path = ResolveOverridePath();
                if (path == null)
                {
                    DeadReckoningPlugin.Log.LogInfo("Track button: no custom track-icon.png in config - using the built-in pin icon.");
                    return DefaultIcon();
                }

                cached = SpriteFromPng(File.ReadAllBytes(path));
                if (cached != null)
                {
                    DeadReckoningPlugin.Log.LogInfo($"Track button: loaded custom icon {cached.texture.width}x{cached.texture.height} from '{path}'.");
                }
                else
                {
                    DeadReckoningPlugin.Log.LogWarning($"Track button: '{path}' is not a decodable PNG - using the built-in pin icon.");
                }
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Track button: failed to load custom icon: {e}");
            }

            return cached != null ? cached : DefaultIcon();
        }

        /// <summary>
        /// The built-in pin icon embedded in the DLL, loaded once. Null only if the embedded
        /// resource is missing/undecodable, which shouldn't happen in a real build.
        /// </summary>
        private static Sprite DefaultIcon()
        {
            if (embeddedTried) return embeddedDefault;
            embeddedTried = true;

            try
            {
                var assembly = typeof(TrackIcon).Assembly;
                using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        DeadReckoningPlugin.Log.LogWarning(
                            $"Track button: embedded '{EmbeddedResourceName}' not found (have: " +
                            $"{string.Join(", ", assembly.GetManifestResourceNames())}) - using a text label.");
                        return null;
                    }

                    embeddedDefault = SpriteFromPng(ReadAll(stream));
                    if (embeddedDefault != null)
                    {
                        DeadReckoningPlugin.Log.LogInfo($"Track button: using the built-in pin icon {embeddedDefault.texture.width}x{embeddedDefault.texture.height}.");
                    }
                    else
                    {
                        DeadReckoningPlugin.Log.LogWarning("Track button: the embedded pin icon is not a decodable PNG - using a text label.");
                    }
                }
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Track button: failed to load the built-in pin icon: {e}");
            }

            return embeddedDefault;
        }

        /// <summary>The user override at <c>BepInEx/config/DeadReckoning/track-icon.png</c>, or null
        /// when there is none - the normal case, covered by the embedded default. Deliberately does
        /// NOT look beside the DLL: a stale loose PNG there would mask a broken embedded
        /// resource, which is exactly the v1.2.1 bug (see ADR-011).</summary>
        private static string ResolveOverridePath()
        {
            string configPath = Path.Combine(Paths.ConfigPath, "DeadReckoning", FileName);
            if (File.Exists(configPath)) return configPath;

            return null;
        }

        private static byte[] ReadAll(Stream stream)
        {
            var data = new byte[stream.Length];
            int read = 0;
            while (read < data.Length)
            {
                int n = stream.Read(data, read, data.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return data;
        }

        /// <summary>Decodes PNG bytes into a sprite, or null if the data isn't a decodable PNG.
        /// Destroys the scratch texture on any failure - a false result OR an exception - so it can
        /// never leak.</summary>
        private static Sprite SpriteFromPng(byte[] data)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "DeadReckoning_TrackIcon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            bool ok = false;
            try
            {
                // LoadImage resizes the texture to the PNG's dimensions and decodes it (alpha kept).
                if (ImageConversion.LoadImage(texture, data))
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    ok = true;
                    return sprite;
                }
                return null;
            }
            finally
            {
                if (!ok) UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
