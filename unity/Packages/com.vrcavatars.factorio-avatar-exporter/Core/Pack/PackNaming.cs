using System;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Names of a pack and its files (docs/vrc-avatar-format.md, "Names"), and the checks of
    /// tools/avatar_pack/spec.py on ids, titles and versions.
    /// </summary>
    public static class PackNaming
    {
        private const string FallbackId = "avatar";
        private const string CorpsePrefix = "corpse-";
        private const string SprintPrefix = "sprint-";
        private const string InfoTitlePrefix = "VRC Avatar: ";
        private const string Ellipsis = "…";

        /// <summary>
        /// A valid pack id made from a title: ASCII letters (lowercased) and digits are kept, every run
        /// of other characters becomes one '-', and the result is trimmed of '-' and cut to
        /// <see cref="PackFormat.MaxIdLength"/>. A title without ASCII letters or digits gives "avatar";
        /// an id that would start with "corpse-" or "sprint-" (reserved for the corpse and sprinting prototypes) gets
        /// "avatar-" in front.
        /// </summary>
        public static string DeriveId(string title)
        {
            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }
            var id = new StringBuilder();
            bool separator = false;
            foreach (char c in title)
            {
                bool lower = c >= 'a' && c <= 'z';
                bool upper = c >= 'A' && c <= 'Z';
                bool digit = c >= '0' && c <= '9';
                if (!lower && !upper && !digit)
                {
                    separator = true;
                    continue;
                }
                if (separator && id.Length > 0)
                {
                    id.Append('-');
                }
                separator = false;
                id.Append(upper ? (char)(c - 'A' + 'a') : c);
            }
            string result = Shorten(id.ToString());
            if (result.Length == 0)
            {
                return FallbackId;
            }
            if (result.StartsWith(CorpsePrefix, StringComparison.Ordinal) || result.StartsWith(SprintPrefix, StringComparison.Ordinal))
            {
                result = Shorten(FallbackId + "-" + result);
            }
            return result;
        }

        private static string Shorten(string id) =>
            (id.Length > PackFormat.MaxIdLength ? id.Substring(0, PackFormat.MaxIdLength) : id).Trim('-');

        /// <summary>
        /// True for 1 to <see cref="PackFormat.MaxIdLength"/> characters of lowercase ASCII letters,
        /// digits and '-', starting with a letter or digit and not with "corpse-" or "sprint-" (spec.valid_id).
        /// </summary>
        public static bool IsValidId(string id)
        {
            if (id == null || id.Length < 1 || id.Length > PackFormat.MaxIdLength)
            {
                return false;
            }
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool alphanumeric = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!alphanumeric && (c != '-' || i == 0))
                {
                    return false;
                }
            }
            return !id.StartsWith(CorpsePrefix, StringComparison.Ordinal) && !id.StartsWith(SprintPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// True for a Factorio mod version "major.minor.patch": three ASCII numbers from 0 to 65535.
        /// </summary>
        public static bool IsValidVersion(string version)
        {
            if (version == null)
            {
                return false;
            }
            string[] parts = version.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }
            foreach (string part in parts)
            {
                if (part.Length < 1 || part.Length > 5)
                {
                    return false;
                }
                int value = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9')
                    {
                        return false;
                    }
                    value = value * 10 + (c - '0');
                }
                if (value > ushort.MaxValue)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The pack's mod name, mod-data and character prototype name: "vrc-avatar-&lt;id&gt;".</summary>
        public static string ModName(string id)
        {
            if (!IsValidId(id))
            {
                throw new ArgumentException("\"" + id + "\" is not a valid pack id", nameof(id));
            }
            return PackFormat.ModPrefix + id;
        }

        /// <summary>The single folder in the pack's zip: "vrc-avatar-&lt;id&gt;_&lt;version&gt;".</summary>
        public static string ZipRootFolder(string id, string version)
        {
            if (!IsValidVersion(version))
            {
                throw new ArgumentException("\"" + version + "\" is not a version like 1.0.0", nameof(version));
            }
            return ModName(id) + "_" + version;
        }

        /// <summary>The file name Factorio expects for the pack's zip: "vrc-avatar-&lt;id&gt;_&lt;version&gt;.zip".</summary>
        public static string ZipFileName(string id, string version) => ZipRootFolder(id, version) + ".zip";

        /// <summary>
        /// Length of a title in characters as the format counts them: Unicode code points, so a
        /// surrogate pair (an emoji, say) is one character.
        /// </summary>
        public static int TitleLength(string title)
        {
            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }
            return CodePoints.Count(title);
        }

        /// <summary>
        /// The info.json title for a pack (spec.info_title): "VRC Avatar: " + title + " by " + author (without the
        /// " by" part when there is no author). Longer than <see cref="PackFormat.InfoTitleMaxBytes"/> bytes of UTF-8,
        /// the title is cut at a character boundary and ended with "…", keeping the author; when even that can't
        /// fit, the whole text is cut.
        /// </summary>
        public static string InfoTitle(string title, string author = null)
        {
            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }
            string by = string.IsNullOrWhiteSpace(author) ? "" : " by " + author.Trim();
            string text = InfoTitlePrefix + title + by;
            if (Encoding.UTF8.GetByteCount(text) <= PackFormat.InfoTitleMaxBytes)
            {
                return text;
            }
            int room = PackFormat.InfoTitleMaxBytes - Encoding.UTF8.GetByteCount(InfoTitlePrefix + Ellipsis + by);
            if (room >= 8)
            {
                return InfoTitlePrefix + Cut(title, room) + Ellipsis + by;
            }
            return Cut(text, PackFormat.InfoTitleMaxBytes - Encoding.UTF8.GetByteCount(Ellipsis)) + Ellipsis;
        }

        /// <summary>The longest start of <paramref name="text"/> of at most <paramref name="maxBytes"/> UTF-8 bytes, whole characters only.</summary>
        private static string Cut(string text, int maxBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= maxBytes)
            {
                return text;
            }
            int cut = maxBytes;
            // drop a character whose UTF-8 bytes would be cut apart: back up to its first byte
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            {
                cut--;
            }
            return Encoding.UTF8.GetString(bytes, 0, cut);
        }
    }

    /// <summary>Unicode code point counting for .NET strings (UTF-16).</summary>
    internal static class CodePoints
    {
        /// <summary>Characters counted as Python's len() does: a surrogate pair counts once.</summary>
        public static int Count(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                }
                count++;
            }
            return count;
        }
    }
}
