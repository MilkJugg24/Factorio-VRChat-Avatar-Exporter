using System;
using System.Collections.Generic;
using static System.FormattableString;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Crops the frames of one animation layer and lays them out as sprite sheets, a port of
    /// <c>bbox_union()</c> and <c>write_sheet()</c> in tools/dragon_render/sheets.py, plus the row
    /// split of docs/vrc-avatar-format.md.
    /// </summary>
    public static class SheetBuilder
    {
        /// <summary>Decimal places of the shift, as in sheets.py.</summary>
        public const int ShiftDigits = 5;

        /// <summary>
        /// Builds the sheets of one layer.
        /// <list type="number">
        /// <item>The crop is the union of the pixels with alpha &gt; 0 over all frames (a completely
        /// empty layer gets a 1x1 box at the anchor), grown by 1 px on each side and clamped to the
        /// canvas. Every frame is cut to it.</item>
        /// <item>shift = round((cropLeft + width / 2 - anchorX) / pixelsPerTile, 5) tiles, and the
        /// same for y.</item>
        /// <item>Rows are directions and columns frames. When directions x height exceeds
        /// <paramref name="maxSheetSize"/>, the rows are split over files of
        /// floor(maxSheetSize / height) rows each. When instead frames x width exceeds it, the frames
        /// are split over files of floor(maxSheetSize / width) frames each, every file holding all
        /// directions. A layer too large both ways does not fit.</item>
        /// </list>
        /// </summary>
        /// <param name="frames">Canvases indexed [direction][frame], all the same size and frame count.</param>
        /// <param name="anchorX">Pixel column of the entity position on the canvas.</param>
        /// <param name="anchorY">Pixel row of the entity position on the canvas.</param>
        /// <param name="pixelsPerTile">Pixels per tile of the canvas (64 or 32 in a pack).</param>
        /// <param name="maxSheetSize">Largest file width and height, px.</param>
        /// <exception cref="InvalidOperationException">One frame is larger than <paramref name="maxSheetSize"/>,
        /// or the layer is too wide and too tall at once.</exception>
        public static SheetSet Build(IReadOnlyList<IReadOnlyList<Image8>> frames, int anchorX, int anchorY,
            int pixelsPerTile, int maxSheetSize = PackFormat.MaxSheetSize)
        {
            if (pixelsPerTile < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerTile), "pixels per tile must be positive");
            }
            if (maxSheetSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSheetSize), "the largest sheet size must be positive");
            }
            (int canvasWidth, int canvasHeight) = CheckFrames(frames);
            int directions = frames.Count;
            int frameCount = frames[0].Count;

            PixelBox box = OpaqueBounds(frames);
            if (box.IsEmpty)
            {
                if (anchorX < 0 || anchorX >= canvasWidth || anchorY < 0 || anchorY >= canvasHeight)
                {
                    throw new ArgumentException(
                        "the layer is empty, so it is cut to a pixel at the anchor, but the anchor is off the canvas",
                        nameof(anchorX));
                }
                box = new PixelBox(anchorX, anchorY, anchorX + 1, anchorY + 1);
            }
            box = box.Grown(1, canvasWidth, canvasHeight);
            int width = box.Width;
            int height = box.Height;
            bool wide = frameCount * width > maxSheetSize, tall = directions * height > maxSheetSize;
            if (width > maxSheetSize || height > maxSheetSize || (wide && tall))
            {
                throw new InvalidOperationException(Invariant(
                    $"{directions} directions of {frameCount} frames of {width}x{height} px do not fit in {maxSheetSize} px sprite sheets ") +
                    "(a sheet may be split by rows or by frames, not both). Use fewer frames, 32 pixels per tile, or " +
                    "keep the character from reaching out so far.");
            }

            int lines = tall ? maxSheetSize / height : directions;
            int framesPerFile = wide ? maxSheetSize / width : frameCount;
            int fileCount = tall ? (directions + lines - 1) / lines : (frameCount + framesPerFile - 1) / framesPerFile;
            var files = new Image8[fileCount];
            for (int k = 0; k < fileCount; k++)
            {
                int firstRow = tall ? k * lines : 0;
                int rows = Math.Min(lines, directions - firstRow);
                int firstFrame = wide ? k * framesPerFile : 0;
                int columns = Math.Min(framesPerFile, frameCount - firstFrame);
                var sheet = new Image8(columns * width, rows * height);
                for (int row = 0; row < rows; row++)
                {
                    for (int f = 0; f < columns; f++)
                    {
                        CopyCrop(frames[firstRow + row][firstFrame + f], box, sheet, f * width, row * height);
                    }
                }
                files[k] = sheet;
            }

            double shiftX = DecimalRounding.Round((box.X0 + width / 2.0 - anchorX) / pixelsPerTile, ShiftDigits);
            double shiftY = DecimalRounding.Round((box.Y0 + height / 2.0 - anchorY) / pixelsPerTile, ShiftDigits);
            return new SheetSet(directions, frameCount, width, height, shiftX, shiftY, files,
                tall ? lines : (int?)null, wide ? framesPerFile : (int?)null);
        }

        private static (int width, int height) CheckFrames(IReadOnlyList<IReadOnlyList<Image8>> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                throw new ArgumentException("at least one direction is needed", nameof(frames));
            }
            if (frames[0] == null || frames[0].Count == 0 || frames[0][0] == null)
            {
                throw new ArgumentException("direction 0 has no frames", nameof(frames));
            }
            int frameCount = frames[0].Count;
            int width = frames[0][0].Width;
            int height = frames[0][0].Height;
            for (int d = 0; d < frames.Count; d++)
            {
                if (frames[d] == null || frames[d].Count != frameCount)
                {
                    throw new ArgumentException(Invariant($"direction {d} does not have {frameCount} frames like direction 0"), nameof(frames));
                }
                for (int f = 0; f < frameCount; f++)
                {
                    Image8 frame = frames[d][f];
                    if (frame == null || frame.Width != width || frame.Height != height)
                    {
                        throw new ArgumentException(Invariant($"frame {f} of direction {d} is missing or not {width}x{height} like the others"), nameof(frames));
                    }
                }
            }
            return (width, height);
        }

        /// <summary>The union box of the pixels with alpha &gt; 0 in all frames.</summary>
        private static PixelBox OpaqueBounds(IReadOnlyList<IReadOnlyList<Image8>> frames)
        {
            PixelBox box = PixelBox.Empty;
            foreach (IReadOnlyList<Image8> direction in frames)
            {
                foreach (Image8 frame in direction)
                {
                    byte[] pixels = frame.Pixels;
                    for (int y = 0; y < frame.Height; y++)
                    {
                        int rowStart = y * frame.Width * 4;
                        for (int x = 0; x < frame.Width; x++)
                        {
                            if (pixels[rowStart + x * 4 + 3] != 0)
                            {
                                box = box.Including(x, y);
                            }
                        }
                    }
                }
            }
            return box;
        }

        private static void CopyCrop(Image8 frame, PixelBox box, Image8 sheet, int left, int top)
        {
            int rowBytes = box.Width * 4;
            for (int y = 0; y < box.Height; y++)
            {
                Buffer.BlockCopy(frame.Pixels, frame.Index(box.X0, box.Y0 + y), sheet.Pixels, sheet.Index(left, top + y), rowBytes);
            }
        }

        /// <summary>A pixel rectangle [X0, X1) x [Y0, Y1).</summary>
        private readonly struct PixelBox
        {
            public PixelBox(int x0, int y0, int x1, int y1)
            {
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
            }

            public static readonly PixelBox Empty = new PixelBox(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

            public int X0 { get; }
            public int Y0 { get; }
            public int X1 { get; }
            public int Y1 { get; }
            public int Width => X1 - X0;
            public int Height => Y1 - Y0;
            public bool IsEmpty => X0 >= X1 || Y0 >= Y1;

            public PixelBox Including(int x, int y) =>
                new PixelBox(Math.Min(X0, x), Math.Min(Y0, y), Math.Max(X1, x + 1), Math.Max(Y1, y + 1));

            public PixelBox Grown(int margin, int canvasWidth, int canvasHeight) =>
                new PixelBox(Math.Max(X0 - margin, 0), Math.Max(Y0 - margin, 0),
                    Math.Min(X1 + margin, canvasWidth), Math.Min(Y1 + margin, canvasHeight));
        }
    }
}
