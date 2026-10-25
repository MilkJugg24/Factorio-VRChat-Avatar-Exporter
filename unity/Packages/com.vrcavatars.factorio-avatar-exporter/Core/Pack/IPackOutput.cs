using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Where <see cref="PackWriter"/> puts a pack: it calls <see cref="Begin"/> once, then
    /// <see cref="Write"/> for every file, then <see cref="Complete"/>. An output holds one pack.
    /// </summary>
    public interface IPackOutput
    {
        /// <summary>Starts the pack; the output names its folder after the id (and version).</summary>
        void Begin(string packId, string version);

        /// <summary>
        /// Adds one file. <paramref name="relativePath"/> is relative to the pack's root folder and
        /// '/'-separated, e.g. "graphics/idle-base.png" (<see cref="PackFiles.CheckRelativePath"/>).
        /// </summary>
        void Write(string relativePath, byte[] data);

        /// <summary>Finishes the pack after the last file.</summary>
        void Complete();
    }

    /// <summary>
    /// The call order and file checks shared by the <see cref="IPackOutput"/> implementations.
    /// </summary>
    internal sealed class PackOutputSequence
    {
        private readonly HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
        private bool begun;
        private bool completed;

        public void Begin()
        {
            if (begun)
            {
                throw new InvalidOperationException("this output already holds a pack; use a new output for each pack");
            }
            begun = true;
        }

        /// <summary>Checks a file before it is written: the path, the data and that it is new.</summary>
        public void Add(string relativePath, byte[] data)
        {
            CheckOpen();
            PackFiles.CheckRelativePath(relativePath);
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (!written.Add(relativePath))
            {
                throw new ArgumentException("\"" + relativePath + "\" was already written", nameof(relativePath));
            }
        }

        public void Complete()
        {
            CheckOpen();
            completed = true;
        }

        private void CheckOpen()
        {
            if (!begun || completed)
            {
                throw new InvalidOperationException("files can only be written between Begin and Complete");
            }
        }
    }
}
