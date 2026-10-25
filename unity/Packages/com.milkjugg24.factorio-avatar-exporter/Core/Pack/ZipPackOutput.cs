using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Writes a pack as a mod zip (save it as <see cref="PackNaming.ZipFileName"/>). The zip is
    /// deterministic, so the same pack always gives the same bytes: every file sits under
    /// "vrc-avatar-&lt;id&gt;_&lt;version&gt;/", entries are in ordinal path order with the fixed
    /// time <see cref="EntryTime"/>, and there are no folder entries.
    /// </summary>
    public sealed class ZipPackOutput : IPackOutput
    {
        /// <summary>The modification time of every entry (2000-01-01 00:00:00, whatever the local time zone).</summary>
        public static readonly DateTimeOffset EntryTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly Stream stream;
        private readonly PackOutputSequence sequence = new PackOutputSequence();
        private readonly SortedDictionary<string, byte[]> files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        private string rootFolder;

        /// <param name="stream">A writable stream; the zip is written to it by <see cref="Complete"/>,
        /// and the stream is left open.</param>
        public ZipPackOutput(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (!stream.CanWrite)
            {
                throw new ArgumentException("the stream is not writable", nameof(stream));
            }
            this.stream = stream;
        }

        public void Begin(string packId, string version)
        {
            string root = PackNaming.ZipRootFolder(packId, version);
            sequence.Begin();
            rootFolder = root;
        }

        /// <summary>Keeps the file until <see cref="Complete"/>; <paramref name="data"/> must not change until then.</summary>
        public void Write(string relativePath, byte[] data)
        {
            sequence.Add(relativePath, data);
            files.Add(relativePath, data);
        }

        /// <summary>Writes the whole zip to the stream.</summary>
        public void Complete()
        {
            sequence.Complete();
            WriteZip(stream, rootFolder, files);
            files.Clear();
        }

        /// <summary>
        /// Writes <paramref name="files"/> (path inside the folder -> bytes) as a zip of the one folder
        /// <paramref name="rootFolder"/>, deterministically: ordinal path order, the fixed time
        /// <see cref="EntryTime"/>, no folder entries. The stream is left open.
        /// </summary>
        internal static void WriteZip(Stream stream, string rootFolder, IEnumerable<KeyValuePair<string, byte[]>> files)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (KeyValuePair<string, byte[]> file in files.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(rootFolder + "/" + file.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = EntryTime;
                    using (Stream entryStream = entry.Open())
                    {
                        entryStream.Write(file.Value, 0, file.Value.Length);
                    }
                }
            }
        }
    }
}
