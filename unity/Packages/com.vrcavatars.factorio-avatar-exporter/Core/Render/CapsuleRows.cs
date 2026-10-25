using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>The pixel columns <see cref="First"/> .. <see cref="Last"/> of a row that capsule <see cref="Index"/> may cover.</summary>
    internal readonly struct CapsuleSpan
    {
        public CapsuleSpan(int index, int first, int last)
        {
            Index = index;
            First = first;
            Last = last;
        }

        public int Index { get; }
        public int First { get; }
        public int Last { get; }
    }

    /// <summary>
    /// Per-pixel culling for a render target: for each row, the capsules whose screen box
    /// (<see cref="ScreenMap.Box"/>) contains some pixel centre of that row, with the columns it covers. A
    /// ray through a pixel centre can only hit capsules listed for that pixel.
    /// </summary>
    internal sealed class CapsuleRows
    {
        // allowance for rounding in the box computation, in render pixels
        private const double Slack = 1e-6;

        private static readonly CapsuleSpan[] None = Array.Empty<CapsuleSpan>();

        private readonly List<CapsuleSpan>[] rows;

        public CapsuleRows(IReadOnlyList<Capsule> capsules, ScreenMap map, int width, int height)
        {
            rows = new List<CapsuleSpan>[height];
            for (int k = 0; k < capsules.Count; k++)
            {
                (double minX, double maxX, double minY, double maxY) = map.Box(capsules[k]);
                (int first, int last) = CentresWithin(minX, maxX, width);
                (int top, int bottom) = CentresWithin(minY, maxY, height);
                if (first > last || top > bottom)
                {
                    continue;
                }
                var span = new CapsuleSpan(k, first, last);
                for (int y = top; y <= bottom; y++)
                {
                    (rows[y] ??= new List<CapsuleSpan>()).Add(span);
                }
            }
        }

        /// <summary>The capsules that may cover pixels of row <paramref name="y"/>, in capsule order.</summary>
        public IReadOnlyList<CapsuleSpan> Row(int y) => (IReadOnlyList<CapsuleSpan>)rows[y] ?? None;

        /// <summary>
        /// The pixels i in [0, count) whose centre i + 0.5 lies in [min, max]; first > last when there are none.
        /// Clamping happens in double precision, so huge or infinite boxes are safe.
        /// </summary>
        private static (int first, int last) CentresWithin(double min, double max, int count)
        {
            double first = Math.Max(0.0, Math.Ceiling(min - 0.5 - Slack));
            double last = Math.Min(count - 1.0, Math.Floor(max - 0.5 + Slack));
            return first > last ? (1, 0) : ((int)first, (int)last);
        }
    }
}
