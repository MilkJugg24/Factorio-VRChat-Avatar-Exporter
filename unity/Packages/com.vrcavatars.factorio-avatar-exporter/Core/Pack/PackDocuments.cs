using System;
using System.Globalization;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The text files of a pack besides the description: info.json, locale/en/locale.cfg and
    /// README.txt. Lines end with "\n". The content is expected to be valid (<see cref="PackValidator"/>).
    /// </summary>
    public static class PackDocuments
    {
        /// <summary>The mod description shown in Factorio's mod list (info.json and locale.cfg).</summary>
        public static string ModDescription(PackContent content)
        {
            CheckContent(content);
            return "Adds the look \"" + content.Title + "\" to " + PackFormat.MainModTitle + "; choose it in the avatar window. " +
                "Made from the avatar \"" + content.SourceAvatar + "\" with " + Generator(content) + ". " +
                "Read README.txt before removing this mod from a save.";
        }

        /// <summary>info.json, as in docs/vrc-avatar-format.md, with the title shortened by <see cref="PackNaming.InfoTitle"/>.</summary>
        public static string InfoJson(PackContent content)
        {
            CheckContent(content);
            var json = new StringBuilder();
            json.Append("{\n");
            AppendJsonField(json, "name", PackNaming.ModName(content.Id));
            AppendJsonField(json, "version", content.Version);
            AppendJsonField(json, "title", PackNaming.InfoTitle(content.Title, content.Author));
            AppendJsonField(json, "author", AuthorOrDefault(content.Author));
            AppendJsonField(json, "factorio_version", PackFormat.FactorioVersion);
            AppendJsonField(json, "description", ModDescription(content));
            json.Append("  ").Append(JsonString("dependencies")).Append(": [")
                .Append(JsonString(PackFormat.Dependency)).Append("]\n");
            json.Append("}\n");
            return json.ToString();
        }

        /// <summary>locale/en/locale.cfg: the mod's name and description in the mod list.</summary>
        public static string Locale(PackContent content)
        {
            CheckContent(content);
            string modName = PackNaming.ModName(content.Id);
            return "[mod-name]\n" + modName + "=" + PackNaming.InfoTitle(content.Title, content.Author) + "\n\n" +
                "[mod-description]\n" + modName + "=" + ModDescription(content) + "\n";
        }

        /// <summary>
        /// README.txt: what the pack is, where it comes from, the permission to share it, how to install
        /// it and how to remove it safely.
        /// </summary>
        public static string Readme(PackContent content)
        {
            CheckContent(content);
            string modName = PackNaming.ModName(content.Id);
            var text = new StringBuilder();
            text.Append(content.Title).Append('\n');
            text.Append('\n');
            text.Append("An avatar pack for the Factorio mod ").Append(PackFormat.MainModTitle)
                .Append(": one more look its players can choose.\n");
            text.Append('\n');
            text.Append("Mod:            ").Append(modName).Append(' ').Append(content.Version).Append('\n');
            if (!string.IsNullOrEmpty(content.Author))
            {
                text.Append("Author:         ").Append(content.Author).Append('\n');
            }
            text.Append("Source avatar:  ").Append(content.SourceAvatar).Append('\n');
            text.Append("Made with:      ").Append(Generator(content)).Append('\n');
            text.Append('\n');
            if (!string.IsNullOrWhiteSpace(content.Permission))
            {
                Section(text, "Permission");
                text.Append(NormalizeLines(content.Permission.Trim())).Append('\n');
                text.Append('\n');
            }
            Section(text, "Installing");
            text.Append("Put ").Append(PackNaming.ZipFileName(content.Id, content.Version))
                .Append(" (or the ").Append(modName).Append(" folder) into Factorio's mods folder\n");
            text.Append("and enable it together with ").Append(PackFormat.MainModTitle).Append(' ')
                .Append(PackFormat.MinMainModVersion).Append(" or newer. Then pick \"").Append(content.Title).Append("\" in the\n");
            text.Append("avatar window (shortcut bar, or Ctrl + Shift + A). In multiplayer, every player needs this\n");
            text.Append("pack too, the same version.\n");
            text.Append('\n');
            Section(text, "Removing");
            text.Append("Characters and corpses using this look stop existing when the pack is removed, and Factorio\n");
            text.Append("deletes them together with the items they carry when the save loads. Before removing the\n");
            text.Append("pack from a save, an admin must run\n");
            text.Append('\n');
            text.Append("    ").Append(PackFormat.RevertCommand).Append(' ').Append(content.Id).Append('\n');
            text.Append('\n');
            text.Append("which turns everyone using it back into the engineer.\n");
            return text.ToString();
        }

        private static void CheckContent(PackContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
        }

        private static string Generator(PackContent content) => content.GeneratorName + " " + content.GeneratorVersion;

        internal static void Section(StringBuilder text, string heading) =>
            text.Append(heading).Append('\n').Append('-', heading.Length).Append('\n');

        internal static string NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>
        /// The author for info.json, which Factorio requires ("Key \"author\" not found" otherwise): the given one,
        /// or <see cref="PackContent.DefaultAuthor"/>.
        /// </summary>
        internal static string AuthorOrDefault(string author) =>
            string.IsNullOrWhiteSpace(author) ? PackContent.DefaultAuthor : author.Trim();

        internal static void AppendJsonField(StringBuilder json, string name, string value) =>
            json.Append("  ").Append(JsonString(name)).Append(": ").Append(JsonString(value)).Append(",\n");

        /// <summary>A JSON string literal; non-ASCII characters stay as they are (the file is UTF-8).</summary>
        internal static string JsonString(string value)
        {
            var output = new StringBuilder(value.Length + 2);
            output.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        output.Append("\\\"");
                        break;
                    case '\\':
                        output.Append("\\\\");
                        break;
                    case '\n':
                        output.Append("\\n");
                        break;
                    case '\r':
                        output.Append("\\r");
                        break;
                    case '\t':
                        output.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            output.Append(c);
                        }
                        break;
                }
            }
            output.Append('"');
            return output.ToString();
        }
    }
}
