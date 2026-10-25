using System;
using System.IO;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Writes a pack as the folder &lt;parent&gt;/vrc-avatar-&lt;id&gt;/, which Factorio loads as is
    /// (for example straight from its mods folder).
    /// </summary>
    public sealed class DirectoryPackOutput : IPackOutput
    {
        private readonly string parentDirectory;
        private readonly bool overwrite;
        private readonly PackOutputSequence sequence = new PackOutputSequence();

        /// <param name="parentDirectory">An existing folder to create the pack folder in.</param>
        /// <param name="overwrite">Replace an existing pack folder of the same name (deleting what it
        /// holds); otherwise <see cref="Begin"/> refuses to touch it.</param>
        public DirectoryPackOutput(string parentDirectory, bool overwrite)
        {
            if (string.IsNullOrEmpty(parentDirectory))
            {
                throw new ArgumentException("the folder to write the pack into is required", nameof(parentDirectory));
            }
            this.parentDirectory = parentDirectory;
            this.overwrite = overwrite;
        }

        /// <summary>The pack folder; null until <see cref="Begin"/>.</summary>
        public string PackDirectory { get; private set; }

        /// <exception cref="IOException">The pack folder exists and overwriting is off, or a file has its name.</exception>
        public void Begin(string packId, string version)
        {
            string folder = Path.Combine(parentDirectory, PackNaming.ModName(packId));
            sequence.Begin();
            if (!Directory.Exists(parentDirectory))
            {
                throw new DirectoryNotFoundException("the folder \"" + parentDirectory + "\" does not exist");
            }
            if (File.Exists(folder))
            {
                throw new IOException("\"" + folder + "\" is a file; move it away to write the pack folder there");
            }
            if (Directory.Exists(folder))
            {
                if (!overwrite)
                {
                    throw new IOException("the pack folder \"" + folder + "\" already exists; allow overwriting to replace it");
                }
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            PackDirectory = folder;
        }

        public void Write(string relativePath, byte[] data)
        {
            sequence.Add(relativePath, data);
            string path = Path.Combine(PackDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        public void Complete() => sequence.Complete();
    }
}
