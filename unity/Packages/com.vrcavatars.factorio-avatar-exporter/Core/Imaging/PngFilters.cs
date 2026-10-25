using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The five PNG scanline filters (PNG specification, section 9). Each predicts a byte from its
    /// neighbours left (a), up (b) and up-left (c), bytes of the same channel; filtering stores the
    /// difference modulo 256. Rows are byte arrays of equal length; the row above the first is all zeros.
    /// </summary>
    internal static class PngFilters
    {
        public const int None = 0;
        public const int Sub = 1;
        public const int Up = 2;
        public const int Average = 3;
        public const int Paeth = 4;
        public const int Count = 5;

        /// <summary>Filters <paramref name="row"/> into <paramref name="output"/>.</summary>
        public static void Apply(int filter, byte[] row, byte[] prior, int bytesPerPixel, byte[] output)
        {
            int n = row.Length;
            int bpp = bytesPerPixel;
            switch (filter)
            {
                case None:
                    Buffer.BlockCopy(row, 0, output, 0, n);
                    break;
                case Sub:
                    for (int i = 0; i < n; i++)
                    {
                        output[i] = (byte)(row[i] - (i >= bpp ? row[i - bpp] : 0));
                    }
                    break;
                case Up:
                    for (int i = 0; i < n; i++)
                    {
                        output[i] = (byte)(row[i] - prior[i]);
                    }
                    break;
                case Average:
                    for (int i = 0; i < n; i++)
                    {
                        output[i] = (byte)(row[i] - (((i >= bpp ? row[i - bpp] : 0) + prior[i]) >> 1));
                    }
                    break;
                case Paeth:
                    for (int i = 0; i < n; i++)
                    {
                        int predicted = i >= bpp ? PaethPredictor(row[i - bpp], prior[i], prior[i - bpp]) : prior[i];
                        output[i] = (byte)(row[i] - predicted);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(filter), filter, "PNG filter types are 0 to 4");
            }
        }

        /// <summary>
        /// Reverses <see cref="Apply"/> in place: <paramref name="row"/> holds filtered bytes on entry and
        /// the original bytes on return. <paramref name="prior"/> is the previous original row.
        /// </summary>
        public static void Undo(int filter, byte[] row, byte[] prior, int bytesPerPixel)
        {
            int n = row.Length;
            int bpp = bytesPerPixel;
            switch (filter)
            {
                case None:
                    break;
                case Sub:
                    for (int i = bpp; i < n; i++)
                    {
                        row[i] = (byte)(row[i] + row[i - bpp]);
                    }
                    break;
                case Up:
                    for (int i = 0; i < n; i++)
                    {
                        row[i] = (byte)(row[i] + prior[i]);
                    }
                    break;
                case Average:
                    for (int i = 0; i < n; i++)
                    {
                        row[i] = (byte)(row[i] + (((i >= bpp ? row[i - bpp] : 0) + prior[i]) >> 1));
                    }
                    break;
                case Paeth:
                    for (int i = 0; i < n; i++)
                    {
                        int predicted = i >= bpp ? PaethPredictor(row[i - bpp], prior[i], prior[i - bpp]) : prior[i];
                        row[i] = (byte)(row[i] + predicted);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(filter), filter, "PNG filter types are 0 to 4");
            }
        }

        /// <summary>
        /// The writer's filter choice: the sum of the filtered bytes read as signed values, in absolute
        /// value (the heuristic the PNG specification recommends). Smaller usually compresses better.
        /// </summary>
        public static long Score(byte[] filtered)
        {
            long sum = 0;
            foreach (byte b in filtered)
            {
                sum += b < 128 ? b : 256 - b;
            }
            return sum;
        }

        /// <summary>Of a, b and c, the one closest to a + b - c (ties prefer a, then b).</summary>
        public static int PaethPredictor(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc)
            {
                return a;
            }
            return pb <= pc ? b : c;
        }
    }
}
