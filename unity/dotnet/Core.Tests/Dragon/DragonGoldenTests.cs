using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Dragon
{
    /// <summary>
    /// The C# port against poses written by the Python renderer
    /// (python3 -m tools.dragon_render.export_goldens).
    /// </summary>
    public class DragonGoldenTests
    {
        private const double Tolerance = 1e-9;

        private static readonly string[] PoseKeys =
        {
            "pelvis", "tr", "tf", "tu", "hip_r", "hip_f", "hip_u", "waist", "chest", "neck_base", "neck_top",
            "head", "hr", "hf", "hu", "breath", "jaw_open", "arms", "legs", "prop",
        };

        public static IEnumerable<TestCaseData> BodyAnimations()
        {
            foreach (BodyShape body in BodyShape.All)
            {
                foreach (AnimationInfo animation in AnimationSpec.All)
                {
                    yield return new TestCaseData(body.Name, animation.Name).SetName($"Golden({body.Name}, {animation.Name})");
                }
            }
        }

        [TestCaseSource(nameof(BodyAnimations))]
        public void PortMatchesPythonPoses(string bodyName, string animation)
        {
            BodyShape body = BodyShape.ByName(bodyName);
            List<GoldenCase> cases = DragonGoldens.Cases.Where(c => c.Body == bodyName && c.Animation == animation).ToList();
            Assert.That(cases, Is.Not.Empty);
            var failures = new List<string>();
            foreach (GoldenCase c in cases)
            {
                Compare(c.Pose, DragonPoses.Pose(c.Animation, body, c.T, c.Move), c.ToString(), failures);
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(20)));
        }

        [Test]
        public void GoldensCoverEveryFrameAndMoveOfEveryAnimation()
        {
            var expected = new List<string>();
            foreach (BodyShape body in BodyShape.All)
            {
                foreach (AnimationInfo info in AnimationSpec.All)
                {
                    IEnumerable<double> moves = Enumerable.Range(0, info.DirectionCount)
                        .Select(row => AnimationSpec.Direction(info.Name, row).Move).Distinct();
                    foreach (double move in moves)
                    {
                        // the Python renderer's frame counts: its idles have 8 frames and its mine 12 (the exporter's have more)
                        int frames = info.Name == AnimationSpec.Idle || info.Name == AnimationSpec.IdleGun ? 8
                            : info.Name == AnimationSpec.Mine ? 12 : info.FrameCount;
                        for (int frame = 0; frame < frames; frame++)
                        {
                            double t = info.Loop ? (double)frame / frames : AnimationSpec.FramePhase(info.Name, frame);
                            expected.Add(Key(body.Name, info.Name, frame, t, move));
                        }
                    }
                }
            }
            List<string> actual = DragonGoldens.Cases.Select(c => Key(c.Body, c.Animation, c.Frame, c.T, c.Move)).ToList();
            Assert.That(actual, Is.Unique);
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void ConstantsMatchPython()
        {
            JsonElement root = DragonGoldens.Root;
            Assert.That(root.GetProperty("format_version").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("head_scale").GetDouble(), Is.EqualTo(DragonModel.HeadScale));
            Assert.That(root.GetProperty("mine_alpha").EnumerateArray().Select(e => e.GetInt32()),
                Is.EqualTo(DragonAnimations.MineAlpha));
            Assert.That(root.GetProperty("mine_lean").EnumerateArray().Select(e => e.GetDouble()),
                Is.EqualTo(DragonAnimations.MineLean));
            JsonElement bodies = root.GetProperty("bodies");
            Assert.That(bodies.EnumerateObject().Select(b => b.Name), Is.EqualTo(BodyShape.All.Select(b => b.Name)));
            foreach (BodyShape body in BodyShape.All)
            {
                JsonElement b = bodies.GetProperty(body.Name);
                Assert.Multiple(() =>
                {
                    Assert.That(b.GetProperty("shoulder_w").GetDouble(), Is.EqualTo(body.ShoulderWidth), body.Name);
                    Assert.That(ReadVec(b.GetProperty("chest_r")), Is.EqualTo(body.ChestRadii), body.Name);
                    Assert.That(ReadVec(b.GetProperty("waist_r")), Is.EqualTo(body.WaistRadii), body.Name);
                    Assert.That(ReadVec(b.GetProperty("hip_r")), Is.EqualTo(body.HipRadii), body.Name);
                    Assert.That(b.GetProperty("hip_w").GetDouble(), Is.EqualTo(body.HipWidth), body.Name);
                    Assert.That(b.GetProperty("arm_r").GetDouble(), Is.EqualTo(body.ArmRadius), body.Name);
                    Assert.That(b.GetProperty("forearm_r").GetDouble(), Is.EqualTo(body.ForearmRadius), body.Name);
                    Assert.That(b.GetProperty("thigh_r").GetDouble(), Is.EqualTo(body.ThighRadius), body.Name);
                    Assert.That(b.GetProperty("neck_r").GetDouble(), Is.EqualTo(body.NeckRadius), body.Name);
                    Assert.That(b.GetProperty("bust").GetBoolean(), Is.EqualTo(body.Bust), body.Name);
                    Assert.That(b.GetProperty("horn_style").GetString(), Is.EqualTo(body.HornStyle), body.Name);
                    Assert.That(b.GetProperty("eye_r").GetDouble(), Is.EqualTo(body.EyeRadius), body.Name);
                });
            }
        }

        private static string Key(string body, string animation, int frame, double t, double move) =>
            FormattableString.Invariant($"{body}/{animation}/{frame}/{t:R}/{move:R}");

        /// <summary>Compares a pose with its golden, adding what differs to <paramref name="failures"/>.</summary>
        private static void Compare(JsonElement golden, DragonPose pose, string context, List<string> failures)
        {
            var actual = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            foreach ((string name, Vec3 value) in DragonPoseAssert.Fields(pose))
            {
                actual.Add(name, value);
            }
            var expected = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            List<string> keys = golden.EnumerateObject().Select(p => p.Name).ToList();
            if (!keys.SequenceEqual(PoseKeys))
            {
                failures.Add(context + ": unexpected golden keys " + string.Join(", ", keys));
                return;
            }
            foreach (string key in PoseKeys.Take(15))
            {
                expected.Add(key, ReadVec(golden.GetProperty(key)));
            }
            expected.Add("breath", new Vec3(golden.GetProperty("breath").GetDouble(), 0, 0));
            expected.Add("jaw_open", new Vec3(golden.GetProperty("jaw_open").GetDouble(), 0, 0));
            AddLimbs(golden.GetProperty("arms"), "arms", expected);
            AddLimbs(golden.GetProperty("legs"), "legs", expected);
            JsonElement prop = golden.GetProperty("prop");
            if (prop.ValueKind != JsonValueKind.Null)
            {
                foreach (JsonProperty point in prop.EnumerateObject().Where(p => p.Name != "kind"))
                {
                    expected.Add("prop." + point.Name, ReadVec(point.Value));
                }
                PropKind kind = prop.GetProperty("kind").GetString() == "gun" ? PropKind.Gun : PropKind.Pickaxe;
                if (pose.Prop == null || pose.Prop.Kind != kind)
                {
                    failures.Add($"{context}: prop should be a {kind}, is {pose.Prop?.Kind.ToString() ?? "none"}");
                }
            }
            if (!actual.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(expected.Keys.OrderBy(k => k, StringComparer.Ordinal)))
            {
                failures.Add(context + ": fields " + string.Join(", ", actual.Keys) + " vs golden " + string.Join(", ", expected.Keys));
                return;
            }
            foreach (KeyValuePair<string, Vec3> e in expected)
            {
                Vec3 a = actual[e.Key];
                if (!a.ApproximatelyEquals(e.Value, Tolerance))
                {
                    failures.Add($"{context}: {e.Key} is {a}, Python {e.Value}");
                }
            }
        }

        private static void AddLimbs(JsonElement limbs, string name, Dictionary<string, Vec3> into)
        {
            foreach (JsonProperty side in limbs.EnumerateObject())
            {
                foreach (JsonProperty point in side.Value.EnumerateObject())
                {
                    into.Add(name + "[" + side.Name + "]." + point.Name, ReadVec(point.Value));
                }
            }
        }

        private static Vec3 ReadVec(JsonElement e)
        {
            Assert.That(e.GetArrayLength(), Is.EqualTo(3), "a vector has three numbers");
            return new Vec3(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
        }
    }
}
