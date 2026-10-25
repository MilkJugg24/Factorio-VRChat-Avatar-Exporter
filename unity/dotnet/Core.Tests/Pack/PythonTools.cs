using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    /// <summary>Runs the repository's Python tools for cross-checks; tests are skipped without python3.</summary>
    internal static class PythonTools
    {
        private static readonly Lazy<bool> Available = new Lazy<bool>(() =>
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo("python3", "-c \"import numpy, PIL\"")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                }))
                {
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Win32Exception)
            {
                return false;
            }
        });

        private static readonly Lazy<string> Root = new Lazy<string>(() =>
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "vrc-avatars", "info.json")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new InvalidOperationException("the repository root (with vrc-avatars/info.json) was not found");
            }
            return dir.FullName;
        });

        public static string RepoRoot => Root.Value;

        public static string ToolsDirectory => Path.Combine(RepoRoot, "tools");

        /// <summary>Skips the calling test when python3 with numpy and Pillow is missing.</summary>
        public static void RequirePython() =>
            Assume.That(Available.Value, "python3 with numpy and Pillow is not available");

        /// <summary>
        /// Runs a Python script in the repository root with tools/ on sys.path, feeds it
        /// <paramref name="input"/> and returns what it prints. Fails the test when the script fails.
        /// </summary>
        public static string Run(string script, string input = "", params string[] args)
        {
            RequirePython();
            var info = new ProcessStartInfo("python3")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = RepoRoot,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("import sys\nsys.path.insert(0, " + PythonString(ToolsDirectory) + ")\n" + script);
            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            using (Process p = Process.Start(info))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                p.StandardInput.Write(input);
                p.StandardInput.Close();
                p.WaitForExit();
                Assert.That(p.ExitCode, Is.EqualTo(0), "python failed:\n" + stderr.Result + stdout.Result);
                return stdout.Result;
            }
        }

        /// <summary>Runs a tool script (path relative to the repository root) and returns exit code and output.</summary>
        public static (int exitCode, string output) RunTool(string tool, params string[] args)
        {
            RequirePython();
            var info = new ProcessStartInfo("python3")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepoRoot,
            };
            info.ArgumentList.Add(Path.Combine(RepoRoot, tool));
            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }
            using (Process p = Process.Start(info))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                return (p.ExitCode, stdout.Result + stderr.Result);
            }
        }

        // a JSON string literal is also a Python one
        private static string PythonString(string text) => JsonSerializer.Serialize(text);
    }
}
