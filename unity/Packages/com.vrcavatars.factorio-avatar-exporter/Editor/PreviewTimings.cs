using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// How long the parts of a preview frame take, for the command line's timing run. Does nothing unless
    /// <see cref="Enabled"/>, so the window and the export pay only for a test of that flag.
    /// </summary>
    internal static class PreviewTimings
    {
        private sealed class Entry
        {
            public long Ticks;
            public int Calls;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        public static bool Enabled { get; set; }

        public static void Reset() => Entries.Clear();

        /// <summary>Counts the time until it is disposed under <paramref name="name"/>.</summary>
        public static Scope Time(string name) => new Scope(Enabled ? name : null);

        public readonly struct Scope : IDisposable
        {
            private readonly string name;
            private readonly long started;

            public Scope(string name)
            {
                this.name = name;
                started = name == null ? 0 : Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (name == null)
                {
                    return;
                }
                if (!Entries.TryGetValue(name, out Entry entry))
                {
                    entry = new Entry();
                    Entries.Add(name, entry);
                }
                entry.Ticks += Stopwatch.GetTimestamp() - started;
                entry.Calls++;
            }
        }

        /// <summary>A table of what was counted since <see cref="Reset"/>, the longest first (parts of a part count in both).</summary>
        public static string Table(string title)
        {
            var text = new StringBuilder();
            text.AppendLine(title);
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32} {1,10} {2,8} {3,10}", "part", "total ms", "calls", "ms/call"));
            foreach (KeyValuePair<string, Entry> item in Entries.OrderByDescending(e => e.Value.Ticks))
            {
                double total = item.Value.Ticks * 1000.0 / Stopwatch.Frequency;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32} {1,10:0.0} {2,8} {3,10:0.00}",
                    item.Key, total, item.Value.Calls, total / item.Value.Calls));
            }
            return text.ToString();
        }
    }
}
