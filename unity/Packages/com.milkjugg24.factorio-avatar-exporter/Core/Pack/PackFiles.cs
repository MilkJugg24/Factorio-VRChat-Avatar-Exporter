using System;
using System.Globalization;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>Paths of the files in a pack, relative to its root folder (docs/vrc-avatar-format.md, "Files").</summary>
    public static class PackFiles
    {
        public const string InfoJson = "info.json";
        public const string DataLua = "data.lua";
        public const string Description = "prototypes/avatar-pack.lua";
        public const string Locale = "locale/en/locale.cfg";
        public const string Readme = "README.txt";
        public const string Thumbnail = "thumbnail.png";
        public const string Icon = "graphics/icon.png";

        /// <summary>
        /// The file of a layer's sheet: graphics/&lt;animation&gt;-&lt;layer&gt;.png, or
        /// graphics/&lt;animation&gt;-&lt;layer&gt;-&lt;n&gt;.png (n from 1) when the sheet is split over
        /// <paramref name="fileCount"/> &gt; 1 files.
        /// </summary>
        public static string Sheet(string animation, string layer, int fileIndex, int fileCount)
        {
            if (fileCount < 1 || fileIndex < 0 || fileIndex >= fileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            }
            string name = "graphics/" + animation + "-" + layer;
            return fileCount == 1 ? name + ".png" : name + "-" + (fileIndex + 1).ToString(CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>How the game refers to a pack file: "__vrc-avatar-&lt;id&gt;__/&lt;path&gt;".</summary>
        public static string ModPath(string id, string relativePath) => "__" + PackNaming.ModName(id) + "__/" + relativePath;

        /// <summary>
        /// Checks that <paramref name="relativePath"/> names a file inside the pack: '/'-separated names,
        /// none of them empty, "." or "..", and no '\', ':' or control characters.
        /// </summary>
        /// <exception cref="ArgumentException">It does not.</exception>
        public static void CheckRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("the file path is empty", nameof(relativePath));
            }
            foreach (string part in relativePath.Split('/'))
            {
                bool bad = part.Length == 0 || part == "." || part == "..";
                foreach (char c in part)
                {
                    bad |= c == '\\' || c == ':' || c < 32 || c == 127;
                }
                if (bad)
                {
                    throw new ArgumentException(
                        "\"" + relativePath + "\" is not a file path inside the pack (folder/name, separated by '/')",
                        nameof(relativePath));
                }
            }
        }
    }
}
