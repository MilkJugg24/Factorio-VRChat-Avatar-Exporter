using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Dragon
{
    /// <summary>One pose of Goldens/dragon_poses.json (written by tools/dragon_render/export_goldens.py).</summary>
    internal sealed class GoldenCase
    {
        public GoldenCase(JsonElement json)
        {
            Body = json.GetProperty("body").GetString();
            Animation = json.GetProperty("animation").GetString();
            Frame = json.GetProperty("frame").GetInt32();
            T = json.GetProperty("t").GetDouble();
            Move = json.GetProperty("move").GetDouble();
            Pose = json.GetProperty("pose");
        }

        public string Body { get; }
        public string Animation { get; }
        public int Frame { get; }
        public double T { get; }
        public double Move { get; }
        public JsonElement Pose { get; }

        public override string ToString() =>
            FormattableString.Invariant($"{Body}/{Animation} frame {Frame} (t {T:R}, move {Move:R})");
    }

    /// <summary>The golden file, parsed once per test run.</summary>
    internal static class DragonGoldens
    {
        private static readonly Lazy<JsonDocument> Document = new Lazy<JsonDocument>(Load);

        private static readonly Lazy<IReadOnlyList<GoldenCase>> AllCases = new Lazy<IReadOnlyList<GoldenCase>>(
            () => Root.GetProperty("cases").EnumerateArray().Select(c => new GoldenCase(c)).ToList());

        public static JsonElement Root => Document.Value.RootElement;

        public static IReadOnlyList<GoldenCase> Cases => AllCases.Value;

        private static JsonDocument Load()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Goldens", "dragon_poses.json");
            Assert.That(File.Exists(path), Is.True,
                "missing " + path + " (written by tools/dragon_render/export_goldens.py, on the archive/dragon-character branch)");
            return JsonDocument.Parse(File.ReadAllText(path));
        }
    }

    internal static class RepoRoot
    {
        /// <summary>The repository root: the first parent of the test directory with vrc-avatars/info.json.</summary>
        public static string Find()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "vrc-avatars", "info.json")))
            {
                dir = dir.Parent;
            }
            Assert.That(dir, Is.Not.Null, "repository root (with vrc-avatars/info.json) not found");
            return dir.FullName;
        }
    }

    internal static class Python
    {
        /// <summary>
        /// Runs python3 with <paramref name="arguments"/> in <paramref name="workingDirectory"/>.
        /// Skips the test (Assume) when python3 or the renderer's packages are missing.
        /// </summary>
        public static (int exitCode, string output) Run(string workingDirectory, params string[] arguments)
        {
            (int probe, string probeOutput) = Start(workingDirectory, "-c", "import numpy, numba");
            Assume.That(probe, Is.EqualTo(0), "python3 with numpy and numba is needed: " + probeOutput);
            return Start(workingDirectory, arguments);
        }

        private static (int exitCode, string output) Start(string workingDirectory, params string[] arguments)
        {
            var info = new ProcessStartInfo("python3")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in arguments)
            {
                info.ArgumentList.Add(a);
            }
            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Win32Exception e)
            {
                Assume.That(false, "python3 is not available: " + e.Message);
                throw;
            }
            using (process)
            {
                System.Threading.Tasks.Task<string> stderr = process.StandardError.ReadToEndAsync();
                string stdout = process.StandardOutput.ReadToEnd();
                Assert.That(process.WaitForExit(120_000), Is.True, "python3 timed out");
                return (process.ExitCode, stdout + stderr.Result);
            }
        }
    }

    /// <summary>Compares whole <see cref="DragonPose"/>s field by field.</summary>
    internal static class DragonPoseAssert
    {
        /// <summary>Every field of a pose, named like model.py, as vectors (scalars in x).</summary>
        public static IEnumerable<(string name, Vec3 value)> Fields(DragonPose p)
        {
            yield return ("pelvis", p.Pelvis);
            yield return ("tr", p.TorsoRight);
            yield return ("tf", p.TorsoForward);
            yield return ("tu", p.TorsoUp);
            yield return ("hip_r", p.HipRight);
            yield return ("hip_f", p.HipForward);
            yield return ("hip_u", p.HipUp);
            yield return ("waist", p.Waist);
            yield return ("chest", p.Chest);
            yield return ("neck_base", p.NeckBase);
            yield return ("neck_top", p.NeckTop);
            yield return ("head", p.Head);
            yield return ("hr", p.HeadRight);
            yield return ("hf", p.HeadForward);
            yield return ("hu", p.HeadUp);
            yield return ("breath", new Vec3(p.Breath, 0, 0));
            yield return ("jaw_open", new Vec3(p.JawOpen, 0, 0));
            foreach (int side in p.Arms.Keys.OrderBy(s => s))
            {
                DragonArm a = p.Arms[side];
                string arm = "arms[" + side.ToString(CultureInfo.InvariantCulture) + "].";
                yield return (arm + "shoulder", a.Shoulder);
                yield return (arm + "elbow", a.Elbow);
                yield return (arm + "hand", a.Hand);
                yield return (arm + "hand_dir", a.HandDirection);
            }
            foreach (int side in p.Legs.Keys.OrderBy(s => s))
            {
                DragonLeg l = p.Legs[side];
                string leg = "legs[" + side.ToString(CultureInfo.InvariantCulture) + "].";
                yield return (leg + "hip", l.Hip);
                yield return (leg + "knee", l.Knee);
                yield return (leg + "ankle", l.Ankle);
                yield return (leg + "ball", l.Ball);
                yield return (leg + "toe", l.Toe);
            }
            switch (p.Prop)
            {
                case DragonGun gun:
                    yield return ("prop.stock", gun.Stock);
                    yield return ("prop.muzzle", gun.Muzzle);
                    yield return ("prop.aim", gun.Aim);
                    break;
                case DragonPick pick:
                    yield return ("prop.base", pick.Base);
                    yield return ("prop.top", pick.Top);
                    yield return ("prop.d", pick.HandleDirection);
                    yield return ("prop.e", pick.HeadDirection);
                    break;
            }
        }

        /// <summary>Asserts both poses have the same fields, equal within <paramref name="tolerance"/>.</summary>
        public static void Same(DragonPose expected, DragonPose actual, double tolerance, string context = "")
        {
            List<(string name, Vec3 value)> e = Fields(expected).ToList();
            List<(string name, Vec3 value)> a = Fields(actual).ToList();
            Assert.That(a.Select(f => f.name), Is.EqualTo(e.Select(f => f.name)), context + ": fields differ");
            for (int i = 0; i < e.Count; i++)
            {
                Assert.That(a[i].value.ApproximatelyEquals(e[i].value, tolerance), Is.True,
                    $"{context}: {e[i].name} is {a[i].value}, expected {e[i].value}");
            }
        }
    }
}
