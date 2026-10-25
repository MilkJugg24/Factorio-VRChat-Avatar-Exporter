using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    /// <summary>
    /// Runs python3 (with numpy and Pillow) for cross-checks. Tests that use it are skipped, not failed,
    /// where python3 or those packages are missing.
    /// </summary>
    internal static class PythonTools
    {
        private static readonly Lazy<string> MissingReason = new Lazy<string>(Probe);

        /// <summary>The repository root: the first ancestor of the test directory holding vrc-avatars/info.json.</summary>
        public static string RepoRoot
        {
            get
            {
                for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "vrc-avatars", "info.json")))
                    {
                        return dir.FullName;
                    }
                }
                throw new InvalidOperationException("repository root not found above " + TestContext.CurrentContext.TestDirectory);
            }
        }

        /// <summary>
        /// Runs <paramref name="script"/> with <c>python3 -I -c</c> and the arguments (sys.argv[1:]),
        /// returning its standard output. Skips the test when python3, numpy or Pillow is unavailable.
        /// </summary>
        public static string Run(string script, params string[] args)
        {
            Assume.That(MissingReason.Value, Is.Null, "python3 with numpy and Pillow is needed for this cross-check");
            (int exitCode, string stdout, string stderr) = Execute(script, args);
            if (exitCode != 0)
            {
                Assert.Fail($"python3 failed with exit code {exitCode}:\n{stderr}");
            }
            return stdout;
        }

        private static string Probe()
        {
            try
            {
                (int exitCode, _, string stderr) = Execute("import numpy, PIL");
                return exitCode == 0 ? null : stderr;
            }
            catch (Win32Exception e)
            {
                return e.Message;
            }
        }

        private static (int exitCode, string stdout, string stderr) Execute(string script, params string[] args)
        {
            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-I");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(script);
            foreach (string a in args)
            {
                start.ArgumentList.Add(a);
            }
            using (Process process = Process.Start(start))
            {
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                string stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, stdout, stderr.Result);
            }
        }
    }

    /// <summary>A fresh temporary directory, deleted on dispose.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imaging-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
