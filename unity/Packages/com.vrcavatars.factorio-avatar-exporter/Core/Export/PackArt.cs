using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>The pack's icon and thumbnail, made from a rendered frame of the character facing the viewer.</summary>
    public static class PackArt
    {
        public const int IconSize = 64;

        /// <summary>Background of the thumbnail (the mod list shows it on a dark panel).</summary>
        private static readonly byte[] ThumbnailBackground = { 40, 40, 48, 255 };

        /// <summary>
        /// The GUI icon: the head and shoulders, a square hanging from the top row of the figure (the pixels
        /// with alpha &gt; 0), scaled to <see cref="IconSize"/>.
        /// <list type="bullet">
        /// <item>With <paramref name="cropBottom"/>, the side is the distance from the top row down to
        /// <paramref name="cropBottom"/>, at least 1 px and at most 60% of the figure's height. When the
        /// figure's pixels in the rows the square covers are wider (the shoulders), the side grows to their
        /// width. The square is centred on those pixels.</item>
        /// <item>Without it, the side is the figure's width, but at least 60% of its height and at most all of
        /// it, and the square is centred on the figure.</item>
        /// </list>
        /// </summary>
        /// <param name="portrait">A frame of the character facing the viewer.</param>
        /// <param name="cropBottom">The first pixel row below the shoulders, or null when it is not known.</param>
        public static Image8 Icon(Image8 portrait, int? cropBottom = null)
        {
            (int x0, int y0, int x1, int y1) = FigureBounds(portrait);
            int width = x1 - x0;
            int height = y1 - y0;
            int side;
            if (cropBottom.HasValue)
            {
                side = Math.Max(1, Math.Min(cropBottom.Value - y0, (int)Math.Round(height * 0.6)));
                (x0, _, x1, _) = FigureBounds(portrait, y0, y0 + side);
                side = Math.Max(side, x1 - x0);
            }
            else
            {
                side = Math.Max(1, Math.Min(height, Math.Max(width, (int)Math.Round(height * 0.6))));
            }
            int centre = (x0 + x1) / 2;
            Image8 square = Region(portrait, centre - side / 2, y0, side, side);
            return ImageOps.Resize(square, IconSize, IconSize);
        }

        /// <summary>The mod list thumbnail: the whole figure on a dark square.</summary>
        public static Image8 Thumbnail(Image8 portrait)
        {
            int size = PackFormat.ThumbnailSize;
            const int padding = 8;
            (int x0, int y0, int x1, int y1) = FigureBounds(portrait);
            int width = x1 - x0;
            int height = y1 - y0;
            double scale = Math.Min((size - 2.0 * padding) / width, (size - 2.0 * padding) / height);
            int w = Math.Max(1, (int)Math.Round(width * scale));
            int h = Math.Max(1, (int)Math.Round(height * scale));
            Image8 figure = ImageOps.Resize(ImageOps.Crop(portrait, x0, y0, width, height), w, h);
            var thumbnail = new Image8(size, size);
            for (int i = 0; i < thumbnail.Pixels.Length; i += 4)
            {
                Buffer.BlockCopy(ThumbnailBackground, 0, thumbnail.Pixels, i, 4);
            }
            ImageOps.CompositeOver(thumbnail, figure, (size - w) / 2, (size - h) / 2);
            return thumbnail;
        }

        /// <summary>Bounds [x0, x1) x [y0, y1) of the pixels with alpha &gt; 0 in rows [firstRow, endRow).</summary>
        private static (int x0, int y0, int x1, int y1) FigureBounds(Image8 image, int firstRow = 0,
            int endRow = int.MaxValue)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = firstRow; y < Math.Min(endRow, image.Height); y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.Alpha(x, y) > 0)
                    {
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x + 1);
                        y1 = Math.Max(y1, y + 1);
                    }
                }
            }
            if (x1 < 0)
            {
                throw new InvalidOperationException("the character frame used for the icon is empty");
            }
            return (x0, y0, x1, y1);
        }

        /// <summary>A copy of a rectangle that may reach outside the image (filled with transparency there).</summary>
        private static Image8 Region(Image8 image, int x, int y, int width, int height)
        {
            var region = new Image8(width, height);
            ImageOps.Blit(region, image, -x, -y);
            return region;
        }
    }
}
