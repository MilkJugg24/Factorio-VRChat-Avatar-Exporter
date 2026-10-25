using System;
using System.Collections.Generic;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Writes a complete avatar pack (docs/vrc-avatar-format.md, "Files"): info.json, data.lua,
    /// prototypes/avatar-pack.lua, the sheets, graphics/icon.png, thumbnail.png, locale/en/locale.cfg
    /// and README.txt. Text files are UTF-8 without a byte order mark, with "\n" line endings.
    /// </summary>
    public sealed class PackWriter
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly Func<Image8, byte[]> encodePng;

        /// <param name="encodePng">Encodes an image as an 8-bit RGBA PNG file.</param>
        public PackWriter(Func<Image8, byte[]> encodePng)
        {
            this.encodePng = encodePng ?? throw new ArgumentNullException(nameof(encodePng));
        }

        /// <summary>
        /// Validates the pack, encodes every file and only then hands them to
        /// <paramref name="output"/> in ordinal path order, so an invalid pack or a failing encoder
        /// leaves the output untouched.
        /// </summary>
        /// <exception cref="InvalidOperationException">The pack is not valid; the message lists every
        /// problem <see cref="PackValidator"/> found.</exception>
        public void Write(PackContent content, IPackOutput output)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            IReadOnlyList<string> errors = PackValidator.Validate(content);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("The avatar pack can not be written:\n- " + string.Join("\n- ", errors));
            }
            SortedDictionary<string, byte[]> files = Files(content);
            output.Begin(content.Id, content.Version);
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                output.Write(file.Key, file.Value);
            }
            output.Complete();
        }

        private SortedDictionary<string, byte[]> Files(PackContent content)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [PackFiles.InfoJson] = Utf8.GetBytes(PackDocuments.InfoJson(content)),
                [PackFiles.DataLua] = Utf8.GetBytes(PackFormat.DataLua),
                [PackFiles.Description] = Utf8.GetBytes(PackDescription.FileText(content)),
                [PackFiles.Locale] = Utf8.GetBytes(PackDocuments.Locale(content)),
                [PackFiles.Readme] = Utf8.GetBytes(PackDocuments.Readme(content)),
                [PackFiles.Icon] = Png(content.Icon),
                [PackFiles.Thumbnail] = Png(content.Thumbnail),
            };
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    IReadOnlyList<Image8> sheets = layer.Value.Files;
                    for (int k = 0; k < sheets.Count; k++)
                    {
                        files.Add(PackFiles.Sheet(animation.Key, layer.Key, k, sheets.Count), Png(sheets[k]));
                    }
                }
            }
            return files;
        }

        private byte[] Png(Image8 image)
        {
            byte[] png = encodePng(image);
            if (png == null || png.Length == 0)
            {
                throw new InvalidOperationException("the PNG encoder returned no data");
            }
            return png;
        }
    }
}
