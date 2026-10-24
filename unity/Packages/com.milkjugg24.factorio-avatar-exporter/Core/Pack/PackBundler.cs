using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>One character of an avatar bundle, as read from a pack's or a bundle's zip.</summary>
    public sealed class BundleCharacter
    {
        internal BundleCharacter(string id, string title, string author, string version, string source, string pathPrefix,
            SortedDictionary<string, byte[]> files)
        {
            Id = id;
            Title = title;
            Author = author;
            Version = version;
            Source = source;
            PathPrefix = pathPrefix;
            Files = files;
        }

        public string Id { get; }
        public string Title { get; }

        /// <summary>The pack's author, or null.</summary>
        public string Author { get; }

        /// <summary>The version of the pack it was made as.</summary>
        public string Version { get; }

        /// <summary>The zip it was read from (as the caller named it), for messages.</summary>
        public string Source { get; }

        /// <summary>How the file paths in its description start where it came from ("__avatar-pack-&lt;id&gt;__/").</summary>
        internal string PathPrefix { get; }

        /// <summary>Its files by path inside its folder: avatar-pack.lua (its description), its graphics, README.txt.</summary>
        internal SortedDictionary<string, byte[]> Files { get; }
    }

    /// <summary>What a pack's or a bundle's zip held.</summary>
    public sealed class BundleInput
    {
        internal BundleInput(string modName, string version, IReadOnlyList<BundleCharacter> characters)
        {
            ModName = modName;
            Version = version;
            Characters = characters;
        }

        /// <summary>The mod's name: avatar-pack-&lt;id&gt; or avatar-bundle-&lt;id&gt;.</summary>
        public string ModName { get; }
        public string Version { get; }
        public bool IsBundle => ModName.StartsWith(PackBundler.ModPrefix, StringComparison.Ordinal);
        public IReadOnlyList<BundleCharacter> Characters { get; }
    }

    /// <summary>The bundle being written: its id (the mod is avatar-bundle-&lt;id&gt;), title, version and author.</summary>
    public sealed class BundleInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Version { get; set; } = PackContent.DefaultVersion;
        public string Author { get; set; }
        public string GeneratorName { get; set; } = PackContent.DefaultGeneratorName;
        public string GeneratorVersion { get; set; }
    }

    /// <summary>
    /// Avatar bundles (docs/avatar-pack-format.md, "Bundles"): several avatar packs in one mod, so a server
    /// hands every player one file. Each character keeps its pack id, and so its character prototype names,
    /// whether it comes as a pack or in a bundle: a save keeps its characters when a server switches.
    ///
    /// A bundle holds, under its one folder: info.json, data.lua (<see cref="DataLua"/>), thumbnail.png,
    /// locale/en/locale.cfg, README.txt, and per character characters/&lt;id&gt;/ with the pack's description
    /// as avatar-pack.lua (its file paths pointing into the bundle), its other files (graphics, README.txt)
    /// and pack.json (id, title, author and version, for combining the bundle again later).
    /// </summary>
    public static class PackBundler
    {
        public const string ModPrefix = "avatar-bundle-";

        /// <summary>The first avatar-characters version that loads bundles (and frames_per_file, 0.4.0, and an animation's own pixels_per_tile, 0.5.0).</summary>
        public const string MinMainModVersion = "0.5.0";

        public const string Dependency = PackFormat.MainMod + " >= " + MinMainModVersion;

        private const string InfoTitlePrefix = "Avatar bundle: ";
        private const string CharactersFolder = "characters/";
        private const string DescriptionFile = "avatar-pack.lua";
        private const string MetadataFile = "pack.json";
        private const string PackDescription = "prototypes/avatar-pack.lua";

        // files of a pack that belong to it as a mod, not to its character
        private static readonly HashSet<string> PackModFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "info.json", "data.lua", "thumbnail.png", "changelog.txt", PackDescription,
        };

        public static string ModName(string bundleId) => ModPrefix + bundleId;

        /// <summary>The single folder in the bundle's zip: "avatar-bundle-&lt;id&gt;_&lt;version&gt;".</summary>
        public static string ZipRootFolder(string bundleId, string version) => ModName(bundleId) + "_" + version;

        /// <summary>The file name Factorio expects for the bundle's zip.</summary>
        public static string ZipFileName(string bundleId, string version) => ZipRootFolder(bundleId, version) + ".zip";

        /// <summary>
        /// The data.lua of a bundle, byte for byte (bundle_data_lua in spec.py): registers each character's
        /// description as its pack's mod-data, with <c>mod</c> naming the bundle that holds its files.
        /// </summary>
        public static string DataLua(string bundleId, IEnumerable<string> ids)
        {
            string list = string.Join(", ", ids.OrderBy(i => i, StringComparer.Ordinal).Select(i => "\"" + i + "\""));
            return "-- An avatar bundle: several avatar packs in one mod (docs/avatar-pack-format.md, \"Bundles\").\n" +
                "local bundle = \"" + ModName(bundleId) + "\"\n" +
                "for _, id in ipairs({ " + list + " }) do\n" +
                "  local pack = require(\"characters.\" .. id .. \".avatar-pack\")\n" +
                "  pack.mod = bundle\n" +
                "  data:extend({\n" +
                "    {\n" +
                "      type = \"mod-data\",\n" +
                "      name = \"" + PackFormat.ModPrefix + "\" .. id,\n" +
                "      data_type = \"" + PackFormat.DataType + "\",\n" +
                "      data = pack,\n" +
                "    },\n" +
                "  })\n" +
                "end\n";
        }

        /// <summary>
        /// The characters of a pack's or a bundle's mod zip. <paramref name="source"/> names the zip in
        /// messages. Throws <see cref="InvalidDataException"/> when it is neither.
        /// </summary>
        public static BundleInput Read(Stream zip, string source)
        {
            Dictionary<string, byte[]> files = ReadZip(zip, source);
            if (!files.TryGetValue("info.json", out byte[] infoBytes))
            {
                throw new InvalidDataException(source + " has no info.json: it is not a Factorio mod");
            }
            string info = Utf8(infoBytes);
            string modName = JsonField(info, "name") ?? "";
            string version = JsonField(info, "version") ?? "";
            if (!PackNaming.IsValidVersion(version))
            {
                throw new InvalidDataException($"{source}: info.json has no valid version");
            }
            if (modName.StartsWith(PackFormat.ModPrefix, StringComparison.Ordinal))
            {
                string id = modName.Substring(PackFormat.ModPrefix.Length);
                CheckId(id, source);
                if (!files.TryGetValue(PackDescription, out byte[] description))
                {
                    throw new InvalidDataException($"{source} has no {PackDescription}");
                }
                var own = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { [DescriptionFile] = description };
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    if (!PackModFiles.Contains(file.Key) && !file.Key.StartsWith("locale/", StringComparison.Ordinal))
                    {
                        own[file.Key] = file.Value;
                    }
                }
                string text = Utf8(description);
                var character = new BundleCharacter(id, LuaField(text, "title") ?? id, LuaField(text, "author"), version,
                    source, "__" + modName + "__/", own);
                return new BundleInput(modName, version, new[] { character });
            }
            if (modName.StartsWith(ModPrefix, StringComparison.Ordinal))
            {
                var characters = new List<BundleCharacter>();
                IEnumerable<string> ids = files.Keys
                    .Where(k => k.StartsWith(CharactersFolder, StringComparison.Ordinal) && k.EndsWith("/" + DescriptionFile, StringComparison.Ordinal))
                    .Select(k => k.Substring(CharactersFolder.Length, k.Length - CharactersFolder.Length - DescriptionFile.Length - 1))
                    .Where(id => id.IndexOf('/') < 0)
                    .OrderBy(id => id, StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    CheckId(id, source);
                    string folder = CharactersFolder + id + "/";
                    var own = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, byte[]> file in files)
                    {
                        if (file.Key.StartsWith(folder, StringComparison.Ordinal))
                        {
                            own[file.Key.Substring(folder.Length)] = file.Value;
                        }
                    }
                    string metadata = own.TryGetValue(MetadataFile, out byte[] m) ? Utf8(m) : "";
                    own.Remove(MetadataFile);
                    string text = Utf8(own[DescriptionFile]);
                    string characterVersion = JsonField(metadata, "version");
                    characters.Add(new BundleCharacter(id, JsonField(metadata, "title") ?? LuaField(text, "title") ?? id,
                        JsonField(metadata, "author") ?? LuaField(text, "author"),
                        PackNaming.IsValidVersion(characterVersion) ? characterVersion : version,
                        source, "__" + modName + "__/" + folder, own));
                }
                if (characters.Count == 0)
                {
                    throw new InvalidDataException(source + " is an avatar bundle without characters");
                }
                return new BundleInput(modName, version, characters);
            }
            throw new InvalidDataException(
                $"{source} is \"{modName}\", not an avatar pack ({PackFormat.ModPrefix}...) or bundle ({ModPrefix}...)");
        }

        /// <summary>
        /// All characters of <paramref name="inputs"/>, sorted by id. When several have the same id, the newest
        /// version wins, and of equal versions the later input; <paramref name="notes"/> says what replaced what.
        /// </summary>
        public static List<BundleCharacter> Merge(IEnumerable<BundleInput> inputs, ICollection<string> notes)
        {
            var byId = new Dictionary<string, BundleCharacter>(StringComparer.Ordinal);
            foreach (BundleInput input in inputs)
            {
                foreach (BundleCharacter character in input.Characters)
                {
                    if (byId.TryGetValue(character.Id, out BundleCharacter old))
                    {
                        int newer = CompareVersions(character.Version, old.Version);
                        if (newer < 0)
                        {
                            notes?.Add($"{character.Id}: kept {old.Version} from {old.Source}, not the older {character.Version} from {character.Source}");
                            continue;
                        }
                        notes?.Add(newer == 0
                            ? $"{character.Id}: {character.Version} from {character.Source} replaces the same version from {old.Source}"
                            : $"{character.Id}: {character.Version} from {character.Source} replaces {old.Version} from {old.Source}");
                    }
                    byId[character.Id] = character;
                }
            }
            return byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>Writes the bundle's zip (deterministic, like <see cref="ZipPackOutput"/>) to <paramref name="output"/>.</summary>
        public static void Write(Stream output, BundleInfo info, IReadOnlyList<BundleCharacter> characters)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }
            if (!PackNaming.IsValidId(info.Id))
            {
                throw new ArgumentException($"the bundle id \"{info.Id}\" is not valid (lowercase letters, digits and -, at most {PackFormat.MaxIdLength} characters)");
            }
            if (!PackNaming.IsValidVersion(info.Version))
            {
                throw new ArgumentException($"the bundle version \"{info.Version}\" is not valid (like 1.0.0)");
            }
            int titleLength = PackNaming.TitleLength(info.Title ?? "");
            if (titleLength == 0 || titleLength > PackFormat.MaxTitleLength || info.Title.IndexOfAny(new[] { '\n', '\r' }) >= 0)
            {
                throw new ArgumentException($"the bundle name must be 1 to {PackFormat.MaxTitleLength} characters on one line");
            }
            if (characters == null || characters.Count == 0)
            {
                throw new ArgumentException("a bundle needs at least one character");
            }
            string duplicate = characters.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate != null)
            {
                throw new ArgumentException($"the character \"{duplicate}\" is in the bundle twice (use Merge)");
            }

            string mod = ModName(info.Id);
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (BundleCharacter character in characters)
            {
                string folder = CharactersFolder + character.Id + "/";
                string prefix = "__" + mod + "__/" + folder;
                foreach (KeyValuePair<string, byte[]> file in character.Files)
                {
                    files[folder + file.Key] = file.Key == DescriptionFile
                        ? Encoding.UTF8.GetBytes(Utf8(file.Value).Replace(character.PathPrefix, prefix))
                        : file.Value;
                }
                files[folder + MetadataFile] = Encoding.UTF8.GetBytes(Metadata(character));
            }
            files["data.lua"] = Encoding.UTF8.GetBytes(DataLua(info.Id, characters.Select(c => c.Id)));
            files["info.json"] = Encoding.UTF8.GetBytes(InfoJson(info, characters));
            files["locale/en/locale.cfg"] = Encoding.UTF8.GetBytes(
                "[mod-name]\n" + mod + "=" + InfoTitle(info.Title) + "\n\n" +
                "[mod-description]\n" + mod + "=" + Description(characters) + "\n");
            files["README.txt"] = Encoding.UTF8.GetBytes(Readme(info, characters));
            Image8 thumbnail = Thumbnail(characters);
            if (thumbnail != null)
            {
                files["thumbnail.png"] = PngWriter.Encode(thumbnail);
            }
            ZipPackOutput.WriteZip(output, ZipRootFolder(info.Id, info.Version), files);
        }

        /// <summary>Compares two "major.minor.patch" versions numerically (an invalid one counts as oldest).</summary>
        public static int CompareVersions(string a, string b)
        {
            int[] x = ParseVersion(a), y = ParseVersion(b);
            for (int i = 0; i < 3; i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }
            return 0;
        }

        /// <summary>The version after <paramref name="version"/>: its last number plus one (1.0.0 when invalid).</summary>
        public static string NextVersion(string version)
        {
            if (!PackNaming.IsValidVersion(version))
            {
                return PackContent.DefaultVersion;
            }
            int[] v = ParseVersion(version);
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", v[0], v[1], v[2] + 1);
        }

        private static int[] ParseVersion(string version) =>
            PackNaming.IsValidVersion(version)
                ? version.Split('.').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray()
                : new[] { -1, -1, -1 };

        private static void CheckId(string id, string source)
        {
            if (!PackNaming.IsValidId(id))
            {
                throw new InvalidDataException($"{source} has a character with the invalid id \"{id}\"");
            }
        }

        private static Dictionary<string, byte[]> ReadZip(Stream zip, string source)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string root = null;
            try
            {
                using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, true))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        int slash = name.IndexOf('/');
                        if (slash <= 0 || (root != null && name.Substring(0, slash) != root))
                        {
                            throw new InvalidDataException(source + " must hold exactly one folder, as a Factorio mod zip does");
                        }
                        root = name.Substring(0, slash);
                        using (Stream stream = entry.Open())
                        using (var copy = new MemoryStream())
                        {
                            stream.CopyTo(copy);
                            files[name.Substring(slash + 1)] = copy.ToArray();
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                throw new InvalidDataException(source + " is not a readable zip: " + e.Message, e);
            }
            return files;
        }

        private static string Metadata(BundleCharacter character)
        {
            var json = new StringBuilder("{\n");
            PackDocuments.AppendJsonField(json, "id", character.Id);
            PackDocuments.AppendJsonField(json, "title", character.Title);
            if (!string.IsNullOrEmpty(character.Author))
            {
                PackDocuments.AppendJsonField(json, "author", character.Author);
            }
            json.Append("  ").Append(PackDocuments.JsonString("version")).Append(": ")
                .Append(PackDocuments.JsonString(character.Version)).Append('\n');
            return json.Append("}\n").ToString();
        }

        private static string InfoJson(BundleInfo info, IReadOnlyList<BundleCharacter> characters)
        {
            var json = new StringBuilder("{\n");
            PackDocuments.AppendJsonField(json, "name", ModName(info.Id));
            PackDocuments.AppendJsonField(json, "version", info.Version);
            PackDocuments.AppendJsonField(json, "title", InfoTitle(info.Title));
            PackDocuments.AppendJsonField(json, "author", PackDocuments.AuthorOrDefault(info.Author));
            PackDocuments.AppendJsonField(json, "factorio_version", PackFormat.FactorioVersion);
            PackDocuments.AppendJsonField(json, "description", Description(characters));
            json.Append("  ").Append(PackDocuments.JsonString("dependencies")).Append(": [")
                .Append(PackDocuments.JsonString(Dependency)).Append("]\n");
            return json.Append("}\n").ToString();
        }

        /// <summary>The info.json title, cut to Factorio's byte limit like <see cref="PackNaming.InfoTitle"/>.</summary>
        private static string InfoTitle(string title)
        {
            string text = InfoTitlePrefix + title.Trim();
            if (Encoding.UTF8.GetByteCount(text) <= PackFormat.InfoTitleMaxBytes)
            {
                return text;
            }
            const string ellipsis = "…";
            var cut = new StringBuilder();
            int bytes = Encoding.UTF8.GetByteCount(ellipsis);
            var chars = StringInfoElements(text);
            foreach (string element in chars)
            {
                int size = Encoding.UTF8.GetByteCount(element);
                if (bytes + size > PackFormat.InfoTitleMaxBytes)
                {
                    break;
                }
                cut.Append(element);
                bytes += size;
            }
            return cut.Append(ellipsis).ToString();
        }

        private static IEnumerable<string> StringInfoElements(string text)
        {
            TextElementEnumerator e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                yield return e.GetTextElement();
            }
        }

        private static string Description(IReadOnlyList<BundleCharacter> characters)
        {
            const int Listed = 10;
            string names = string.Join(", ", characters.Take(Listed).Select(c => c.Title));
            if (characters.Count > Listed)
            {
                names += $" and {characters.Count - Listed} more";
            }
            return $"Characters for {PackFormat.MainModTitle}: {names}. Every player picks their own in the avatar window. " +
                "Read README.txt before removing this mod from a save.";
        }

        private static string Readme(BundleInfo info, IReadOnlyList<BundleCharacter> characters)
        {
            string mod = ModName(info.Id);
            var text = new StringBuilder();
            text.Append(info.Title.Trim()).Append('\n').Append('\n');
            text.Append("An avatar bundle for the Factorio mod ").Append(PackFormat.MainModTitle)
                .Append(": several characters in one mod, for playing together. Every player picks\n")
                .Append("their own in the avatar window (shortcut bar, or Ctrl + Shift + A).\n\n");
            text.Append("Mod:        ").Append(mod).Append(' ').Append(info.Version).Append('\n');
            if (!string.IsNullOrEmpty(info.Author))
            {
                text.Append("Made by:    ").Append(info.Author).Append('\n');
            }
            text.Append("Made with:  ").Append(info.GeneratorName).Append(' ').Append(info.GeneratorVersion).Append('\n');
            text.Append('\n');
            PackDocuments.Section(text, "Characters");
            foreach (BundleCharacter c in characters)
            {
                text.Append(c.Title).Append(" (").Append(c.Id).Append(' ').Append(c.Version).Append(')');
                if (!string.IsNullOrEmpty(c.Author))
                {
                    text.Append(" by ").Append(c.Author);
                }
                text.Append('\n');
            }
            text.Append("Each character's source and the permission to use it are in characters/<id>/README.txt.\n\n");
            PackDocuments.Section(text, "Installing");
            text.Append("Every player, and the server, puts ").Append(ZipFileName(info.Id, info.Version))
                .Append(", still zipped, into\n");
            text.Append("Factorio's mods folder, together with ").Append(PackFormat.MainModTitle).Append(' ')
                .Append(MinMainModVersion).Append(" or newer, and deletes older versions of this bundle.\n");
            text.Append("Everyone needs the same version to play together.\n\n");
            PackDocuments.Section(text, "Removing a character, or this bundle");
            text.Append("Characters and corpses using a character stop existing when it is no longer installed, and\n");
            text.Append("Factorio deletes them together with the items they carry when the save loads. Before a new\n");
            text.Append("version of the bundle leaves a character out, or before removing the bundle, an admin runs\n\n");
            text.Append("    ").Append(PackFormat.RevertCommand).Append(" <id>\n\n");
            text.Append("for each character that goes, which turns everyone using it back into the engineer.\n");
            return text.ToString();
        }

        /// <summary>The mod list picture: the characters' icons, one large or up to four in a grid.</summary>
        private static Image8 Thumbnail(IReadOnlyList<BundleCharacter> characters)
        {
            var icons = new List<Image8>();
            foreach (BundleCharacter c in characters)
            {
                if (icons.Count == 4)
                {
                    break;
                }
                if (c.Files.TryGetValue("graphics/icon.png", out byte[] png))
                {
                    try
                    {
                        icons.Add(PngReader.Decode(png));
                    }
                    catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is NotSupportedException)
                    {
                        // an icon Core can't read: leave it out of the picture
                    }
                }
            }
            if (icons.Count == 0)
            {
                return null;
            }
            const int Size = 144;
            var thumbnail = new Image8(Size, Size);
            if (icons.Count == 1)
            {
                Blit(thumbnail, Fit(icons[0], 128), 8, 8);
                return thumbnail;
            }
            for (int i = 0; i < icons.Count; i++)
            {
                Blit(thumbnail, Fit(icons[i], 64), (i % 2) * 72 + 4, (i / 2) * 72 + 4);
            }
            return thumbnail;
        }

        /// <summary><paramref name="image"/> scaled to <paramref name="size"/> x <paramref name="size"/> (nearest pixel).</summary>
        private static Image8 Fit(Image8 image, int size)
        {
            var fitted = new Image8(size, size);
            for (int y = 0; y < size; y++)
            {
                int sy = Math.Min(image.Height - 1, y * image.Height / size);
                for (int x = 0; x < size; x++)
                {
                    int sx = Math.Min(image.Width - 1, x * image.Width / size);
                    for (int c = 0; c < 4; c++)
                    {
                        fitted[x, y, c] = image[sx, sy, c];
                    }
                }
            }
            return fitted;
        }

        private static void Blit(Image8 target, Image8 image, int left, int top)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        target[left + x, top + y, c] = image[x, y, c];
                    }
                }
            }
        }

        private static string Utf8(byte[] bytes)
        {
            string text = Encoding.UTF8.GetString(bytes);
            return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
        }

        /// <summary>A top-level string field of a Lua table literal, like <c>title = "Mika",</c>; null when absent.</summary>
        internal static string LuaField(string lua, string field)
        {
            Match m = Regex.Match(lua, @"(?m)^\s*" + field + @"\s*=\s*""((?:[^""\\]|\\.)*)""");
            return m.Success ? UnescapeLua(m.Groups[1].Value) : null;
        }

        /// <summary>A string field of a JSON object, like <c>"version": "1.0.0"</c>; null when absent.</summary>
        internal static string JsonField(string json, string field)
        {
            Match m = Regex.Match(json, "\"" + field + @"""\s*:\s*""((?:[^""\\]|\\.)*)""");
            return m.Success ? UnescapeJson(m.Groups[1].Value) : null;
        }

        private static string UnescapeLua(string s)
        {
            var output = new StringBuilder(s.Length);
            var bytes = new List<byte>();
            void Flush()
            {
                if (bytes.Count > 0)
                {
                    output.Append(Encoding.UTF8.GetString(bytes.ToArray()));
                    bytes.Clear();
                }
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 == s.Length)
                {
                    Flush();
                    output.Append(c);
                    continue;
                }
                char next = s[++i];
                if (char.IsDigit(next))
                {
                    int end = i;
                    while (end < s.Length && end < i + 3 && char.IsDigit(s[end]))
                    {
                        end++;
                    }
                    bytes.Add((byte)Math.Min(255, int.Parse(s.Substring(i, end - i), CultureInfo.InvariantCulture)));
                    i = end - 1;
                    continue;
                }
                Flush();
                output.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next == 'r' ? '\r' : next);
            }
            Flush();
            return output.ToString();
        }

        private static string UnescapeJson(string s)
        {
            var output = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 == s.Length)
                {
                    output.Append(c);
                    continue;
                }
                char next = s[++i];
                switch (next)
                {
                    case 'n': output.Append('\n'); break;
                    case 't': output.Append('\t'); break;
                    case 'r': output.Append('\r'); break;
                    case 'b': output.Append('\b'); break;
                    case 'f': output.Append('\f'); break;
                    case 'u' when i + 4 < s.Length:
                        output.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: output.Append(next); break;
                }
            }
            return output.ToString();
        }
    }
}
