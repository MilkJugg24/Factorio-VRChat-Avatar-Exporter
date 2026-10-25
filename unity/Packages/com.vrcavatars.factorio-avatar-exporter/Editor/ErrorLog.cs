using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// When loading an avatar or exporting fails, writes what's needed to find out why into ERROR_LOGS in the
    /// exporter's folder (Assets/FactorioAvatarExporter/ERROR_LOGS), one text file per failure, for the user to
    /// attach to a report: the error and where it happened, the exporter, Unity, system and graphics versions, the
    /// pack settings and lighting, the avatar report, and the warnings and errors Unity logged meanwhile. It holds
    /// no images and no author name, and the user's home folder and name are blanked out of paths.
    /// </summary>
    internal static class ErrorLog
    {
        private const string FolderName = "ERROR_LOGS";

        /// <summary>Writes the log; returns its path, or null when even that failed (the Console still has the error).</summary>
        public static string Write(string what, Exception error, PosePreview preview, PackOptions options,
            StageLighting lighting, IReadOnlyList<string> console)
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("Factorio Avatar Exporter error log");
                text.AppendLine("Attach this file when you report the problem. It contains no images.");
                text.AppendLine();
                text.AppendLine("What failed:   " + what);
                text.AppendLine("When (UTC):    " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                text.AppendLine("Exporter:      " + ExporterInfo.Version);
                text.AppendLine("Unity:         " + Application.unityVersion);
                text.AppendLine("System:        " + SystemInfo.operatingSystem + ", " + SystemInfo.systemMemorySize + " MB memory");
                text.AppendLine("Graphics:      " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ", " +
                    SystemInfo.graphicsDeviceVersion + ", " + SystemInfo.graphicsMemorySize + " MB)");
                text.AppendLine("Colour space:  " + PlayerSettings.colorSpace);
                text.AppendLine("Pipeline:      " + (GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.GetType().Name : "built-in"));
                if (preview != null)
                {
                    text.AppendLine("Avatar:        " + preview.AvatarName);
                }
                if (options != null)
                {
                    text.AppendLine($"Pack:          \"{options.Title}\" ({options.Id}) {options.Version}, {options.PixelsPerTile} pixels per tile, " +
                        (options.Flight ? "with" : "without") + " flight");
                }
                if (lighting != null)
                {
                    text.AppendLine("Lighting:      " + lighting.Effective.Describe());
                }
                text.AppendLine();
                text.AppendLine("Error:");
                text.AppendLine(error.ToString());
                if (preview != null && preview.Notes.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("Avatar report:");
                    foreach (string note in preview.Notes)
                    {
                        text.AppendLine("  " + note);
                    }
                }
                if (console != null && console.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("Warnings and errors Unity logged meanwhile:");
                    foreach (string line in console)
                    {
                        text.AppendLine("  " + line);
                    }
                }

                string folder = Path.Combine(Folder(), FolderName);
                Directory.CreateDirectory(folder);
                string name = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                    Safe(preview != null ? preview.AvatarName : what) + ".txt";
                string path = Path.Combine(folder, name);
                File.WriteAllText(path, Private(text.ToString()), new UTF8Encoding(false));
                AssetDatabase.Refresh();
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Factorio Avatar Exporter: could not write an error log: " + e.Message);
                return null;
            }
        }

        /// <summary>The sentence for the window telling where the log is, or "" when there is none.</summary>
        public static string Note(string path) => path == null ? "" :
            " Details were saved to " + Private(path) + "; attach that file when you report the problem.";

        /// <summary>
        /// The exporter's own folder (where this package was imported, normally Assets/FactorioAvatarExporter), or,
        /// when it can't be written to (a package Unity keeps read-only), FactorioAvatarExport next to Assets.
        /// </summary>
        private static string Folder()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string script = AssetDatabase.FindAssets("ErrorLog t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/Editor/ErrorLog.cs", StringComparison.Ordinal));
            if (script != null)
            {
                string root = Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
                if (root.StartsWith("Assets", StringComparison.Ordinal))
                {
                    return Path.Combine(project, root);
                }
                PackageInfo package = PackageInfo.FindForAssetPath(script);
                if (package != null && (package.source == PackageSource.Local || package.source == PackageSource.Embedded))
                {
                    return package.resolvedPath;
                }
            }
            return Path.Combine(project, "FactorioAvatarExport");
        }

        private static string Safe(string name)
        {
            var safe = new StringBuilder();
            foreach (char c in name ?? "export")
            {
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return safe.Length > 0 ? safe.ToString() : "export";
        }

        /// <summary>Blanks the user's home folder and name out of the text (paths in stack traces and messages).</summary>
        private static string Private(string text)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                text = text.Replace(home, "~").Replace(home.Replace('\\', '/'), "~");
            }
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length > 2)
            {
                text = text.Replace(user, "<user>");
            }
            return text;
        }
    }
}
