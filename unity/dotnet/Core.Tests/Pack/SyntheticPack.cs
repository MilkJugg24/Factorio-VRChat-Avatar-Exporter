using System;
using System.Collections.Generic;
using System.IO;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    /// <summary>Small synthetic frames and packs for the pack tests.</summary>
    internal static class SyntheticPack
    {
        public const int BodyWidth = 12;
        public const int BodyHeight = 16;
        public const int BodyAnchorX = 6;
        public const int BodyAnchorY = 13;
        public const int ShadowWidth = 16;
        public const int ShadowHeight = 8;

        /// <summary>
        /// Canvases [direction][frame] with a few pixels that move with the direction and frame, so
        /// every cell of a sheet differs. <paramref name="salt"/> varies colours and positions.
        /// </summary>
        public static List<IReadOnlyList<Image8>> Frames(int directions, int frames, int width, int height, int salt)
        {
            var result = new List<IReadOnlyList<Image8>>();
            for (int d = 0; d < directions; d++)
            {
                var row = new List<Image8>();
                for (int f = 0; f < frames; f++)
                {
                    var image = new Image8(width, height);
                    int x = 2 + (d + f + salt) % (width - 4);
                    int y = 2 + (d * 3 + f + salt) % (height - 4);
                    Paint(image, x, y, (byte)(40 + d * 9), (byte)(40 + f * 13), (byte)(salt * 31), 255);
                    Paint(image, x + 1, y, 200, 10, 10, 128);
                    Paint(image, width / 2, height - 3, 10, 200, 10, 255);
                    row.Add(image);
                }
                result.Add(row);
            }
            return result;
        }

        public static void Paint(Image8 image, int x, int y, byte r, byte g, byte b, byte a)
        {
            image[x, y, 0] = r;
            image[x, y, 1] = g;
            image[x, y, 2] = b;
            image[x, y, 3] = a;
        }

        public static Image8 Filled(int width, int height, byte r, byte g, byte b, byte a)
        {
            var image = new Image8(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Paint(image, x, y, r, g, b, a);
                }
            }
            return image;
        }

        /// <summary>
        /// A valid pack holding every animation of <see cref="AnimationSpec.ForPack"/>, with base,
        /// shadow, optionally mask and (run-gun) shadow-flipped layers. The 18-direction animations
        /// use a small sheet limit, so their base and mask sheets are split over several files.
        /// </summary>
        public static PackContent Build(bool flight, bool tintable, int pixelsPerTile = 64, string id = "test-avatar")
        {
            var content = new PackContent
            {
                Id = id,
                Title = "Test Avatar",
                Author = "Tester",
                SourceAvatar = "Test Model",
                Permission = "Made for the tests; free to share.",
                GeneratorVersion = "0.1.0",
                PixelsPerTile = pixelsPerTile,
                Flight = flight,
                Icon = Filled(64, 64, 200, 120, 40, 255),
                Thumbnail = Filled(PackFormat.ThumbnailSize, PackFormat.ThumbnailSize, 40, 40, 48, 255),
            };
            int salt = 0;
            foreach (AnimationInfo info in AnimationSpec.ForPack(flight))
            {
                // 192 px: the widest row (12 frames of a 16 px shadow) fits, 18 body rows do not
                int maxSheet = info.DirectionCount == 18 ? 192 : PackFormat.MaxSheetSize;
                var layers = new List<string> { PackLayers.Base, PackLayers.Shadow };
                if (tintable)
                {
                    layers.Add(PackLayers.Mask);
                }
                layers.AddRange(PackLayers.OptionalFor(info.Name));
                foreach (string layer in layers)
                {
                    bool shadow = layer.StartsWith(PackLayers.Shadow, StringComparison.Ordinal);
                    int width = shadow ? ShadowWidth : BodyWidth;
                    int height = shadow ? ShadowHeight : BodyHeight;
                    List<IReadOnlyList<Image8>> frames = Frames(info.DirectionCount, info.FrameCount, width, height, salt++);
                    SheetSet sheets = shadow
                        ? SheetBuilder.Build(frames, 5, 3, pixelsPerTile, maxSheet)
                        : SheetBuilder.Build(frames, BodyAnchorX, BodyAnchorY, pixelsPerTile, maxSheet);
                    content.SetLayer(info.Name, layer, sheets);
                }
            }
            return content;
        }
    }

    /// <summary>Keeps the files a <see cref="PackWriter"/> writes, in order, for inspection.</summary>
    internal sealed class MemoryPackOutput : IPackOutput
    {
        public string PackId { get; private set; }
        public string Version { get; private set; }
        public bool Completed { get; private set; }
        public List<string> Paths { get; } = new List<string>();
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public void Begin(string packId, string version)
        {
            PackId = packId;
            Version = version;
        }

        public void Write(string relativePath, byte[] data)
        {
            Paths.Add(relativePath);
            Files.Add(relativePath, data);
        }

        public void Complete() => Completed = true;

        public string Text(string path) => new System.Text.UTF8Encoding(false, true).GetString(Files[path]);
    }

    /// <summary>A temporary folder deleted on dispose.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pack-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
