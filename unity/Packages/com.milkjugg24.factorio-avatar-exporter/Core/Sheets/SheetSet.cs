using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static System.FormattableString;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// The sprite sheets of one layer of an animation (docs/vrc-avatar-format.md, "Sheets"): one row
    /// per direction, one column per frame, every frame <see cref="FrameWidth"/> x
    /// <see cref="FrameHeight"/> px. A layer too tall for one file is split by rows into several files
    /// of <see cref="LinesPerFile"/> rows each; a layer too wide for one file is split by columns into
    /// files of <see cref="FramesPerFile"/> frames each (Factorio's <c>stripes</c>). Either way the last
    /// file may be smaller. A layer is never split both ways.
    /// </summary>
    public sealed class SheetSet
    {
        /// <summary>
        /// Checks that <paramref name="files"/> hold exactly the rows and columns described: one file
        /// when both splits are null, otherwise ceil(directions / lines) or ceil(frames / framesPerFile)
        /// files. The list is copied; the images are not.
        /// </summary>
        public SheetSet(int directionCount, int frameCount, int frameWidth, int frameHeight, double shiftX,
            double shiftY, IReadOnlyList<Image8> files, int? linesPerFile, int? framesPerFile = null)
        {
            if (directionCount < 1 || frameCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(directionCount), "direction and frame counts must be positive");
            }
            if (frameWidth < 1 || frameHeight < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(frameWidth), "frame sizes must be positive");
            }
            if (!IsFinite(shiftX) || !IsFinite(shiftY))
            {
                throw new ArgumentException("the shift must be finite", nameof(shiftX));
            }
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("a sheet set needs at least one file", nameof(files));
            }
            if (linesPerFile.HasValue && (linesPerFile.Value < 1 || linesPerFile.Value >= directionCount))
            {
                throw new ArgumentOutOfRangeException(nameof(linesPerFile),
                    "lines per file must be between 1 and the direction count - 1 (null for a single file)");
            }
            if (framesPerFile.HasValue && (framesPerFile.Value < 1 || framesPerFile.Value >= frameCount))
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerFile),
                    "frames per file must be between 1 and the frame count - 1 (null for a single file)");
            }
            if (linesPerFile.HasValue && framesPerFile.HasValue)
            {
                throw new ArgumentException("a layer is split by rows or by frames, not both", nameof(framesPerFile));
            }
            DirectionCount = directionCount;
            FrameCount = frameCount;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            ShiftX = shiftX;
            ShiftY = shiftY;
            LinesPerFile = linesPerFile;
            FramesPerFile = framesPerFile;
            int expectedFiles = linesPerFile.HasValue ? (directionCount + linesPerFile.Value - 1) / linesPerFile.Value
                : framesPerFile.HasValue ? (frameCount + framesPerFile.Value - 1) / framesPerFile.Value : 1;
            if (files.Count != expectedFiles)
            {
                throw new ArgumentException(
                    Invariant($"{files.Count} files given, but {directionCount} directions of {frameCount} frames need {expectedFiles}"),
                    nameof(files));
            }
            var copy = new Image8[files.Count];
            for (int k = 0; k < copy.Length; k++)
            {
                Image8 file = files[k];
                int width = FramesInFile(k) * frameWidth;
                int height = RowsInFile(k) * frameHeight;
                if (file == null || file.Width != width || file.Height != height)
                {
                    string actual = file == null ? "missing" : Invariant($"{file.Width}x{file.Height}");
                    throw new ArgumentException(Invariant($"file {k + 1} is {actual}, expected {width}x{height}"), nameof(files));
                }
                copy[k] = file;
            }
            Files = new ReadOnlyCollection<Image8>(copy);
        }

        /// <summary>Rows of the sheet: the animation's directions.</summary>
        public int DirectionCount { get; }

        /// <summary>Columns of the sheet: the animation's frames.</summary>
        public int FrameCount { get; }

        /// <summary>Size of one frame, px.</summary>
        public int FrameWidth { get; }
        public int FrameHeight { get; }

        /// <summary>
        /// Offset of the frame centre from the entity position, tiles, x to the right and y down
        /// (Factorio's <c>shift</c>).
        /// </summary>
        public double ShiftX { get; }
        public double ShiftY { get; }

        /// <summary>The sheet images, top rows first.</summary>
        public IReadOnlyList<Image8> Files { get; }

        /// <summary>Rows per file when the sheet is split by rows, otherwise null.</summary>
        public int? LinesPerFile { get; }

        /// <summary>Frames (columns) per file when the sheet is split by frames, otherwise null.</summary>
        public int? FramesPerFile { get; }

        /// <summary>Number of rows (directions) in file <paramref name="fileIndex"/> (0-based).</summary>
        public int RowsInFile(int fileIndex)
        {
            CheckFileIndex(fileIndex);
            int lines = LinesPerFile ?? DirectionCount;
            return LinesPerFile.HasValue ? Math.Min(lines, DirectionCount - fileIndex * lines) : DirectionCount;
        }

        /// <summary>Number of columns (frames) in file <paramref name="fileIndex"/> (0-based).</summary>
        public int FramesInFile(int fileIndex)
        {
            CheckFileIndex(fileIndex);
            int frames = FramesPerFile ?? FrameCount;
            return FramesPerFile.HasValue ? Math.Min(frames, FrameCount - fileIndex * frames) : FrameCount;
        }

        /// <summary>The first frame of file <paramref name="fileIndex"/>; 0 unless split by frames.</summary>
        public int FirstFrameInFile(int fileIndex)
        {
            CheckFileIndex(fileIndex);
            return FramesPerFile.HasValue ? fileIndex * FramesPerFile.Value : 0;
        }

        /// <summary>The first row (direction) of file <paramref name="fileIndex"/>; 0 unless split by rows.</summary>
        public int FirstRowInFile(int fileIndex)
        {
            CheckFileIndex(fileIndex);
            return LinesPerFile.HasValue ? fileIndex * LinesPerFile.Value : 0;
        }

        private void CheckFileIndex(int fileIndex)
        {
            int count = LinesPerFile.HasValue ? (DirectionCount + LinesPerFile.Value - 1) / LinesPerFile.Value
                : FramesPerFile.HasValue ? (FrameCount + FramesPerFile.Value - 1) / FramesPerFile.Value : 1;
            if (fileIndex < 0 || fileIndex >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            }
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
