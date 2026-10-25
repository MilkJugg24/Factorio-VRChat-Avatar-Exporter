using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    /// <summary>Hand-made poses for the stick figure tests (model space, tiles).</summary>
    internal static class TestPoses
    {
        /// <summary>A 1.48-tile figure in a T-pose facing model +y, toes 0.03 above the ground (feet radius below).</summary>
        public static PoseResult TPose(double lift = 0)
        {
            var pose = new PoseResult();
            void Set(string name, double x, double y, double z) => pose.Joints[name] = new Vec3(x, y, z + lift);
            Set(JointNames.Pelvis, 0, 0, 0.80);
            Set(JointNames.Waist, 0, 0, 0.95);
            Set(JointNames.Chest, 0, 0, 1.10);
            Set(JointNames.NeckBase, 0, 0, 1.22);
            Set(JointNames.NeckTop, 0, 0, 1.28);
            Set(JointNames.Head, 0, 0, 1.36);
            Set(JointNames.HeadTop, 0, 0, 1.48);
            foreach (int side in new[] { -1, 1 })
            {
                Set(JointNames.Shoulder(side), side * 0.16, 0, 1.18);
                Set(JointNames.Elbow(side), side * 0.40, 0, 1.18);
                Set(JointNames.Hand(side), side * 0.62, 0, 1.18);
                Set(JointNames.Hip(side), side * 0.09, 0, 0.78);
                Set(JointNames.Knee(side), side * 0.10, 0.02, 0.42);
                Set(JointNames.Ankle(side), side * 0.10, 0, 0.08);
                Set(JointNames.Ball(side), side * 0.10, 0.10, 0.03);
                Set(JointNames.Toe(side), side * 0.10, 0.16, 0.03);
            }
            return pose;
        }

        /// <summary>Only pelvis and waist: a single torso capsule from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static PoseResult Segment(Vec3 a, Vec3 b)
        {
            var pose = new PoseResult();
            pose.Joints[JointNames.Pelvis] = a;
            pose.Joints[JointNames.Waist] = b;
            return pose;
        }

        /// <summary>Only the right arm (chest, shoulder, elbow, hand of the T-pose): three red capsules.</summary>
        public static PoseResult RightArm()
        {
            PoseResult t = TPose();
            var pose = new PoseResult();
            foreach (string name in new[] { JointNames.Chest, JointNames.Shoulder(1), JointNames.Elbow(1), JointNames.Hand(1) })
            {
                pose.Joints[name] = t.Joints[name];
            }
            return pose;
        }

        /// <summary>A dragon pose of <paramref name="animation"/> at frame <paramref name="frame"/> of sheet row <paramref name="row"/>.</summary>
        public static (PoseResult pose, double orientation) Dragon(string animation, int row, int frame)
        {
            DirectionInfo direction = AnimationSpec.Direction(animation, row);
            var source = new DragonPoseSource(BodyShape.Male);
            PoseResult pose = source.Pose(animation, AnimationSpec.FramePhase(animation, frame), direction.Move);
            return (pose, direction.Orientation);
        }
    }

    /// <summary>
    /// Brute-force references for the renderer: every capsule against every ray, no culling, so comparing
    /// with the renderer checks its culling as well.
    /// </summary>
    internal static class ReferenceTracer
    {
        /// <summary>The nearest entry of the view ray into any capsule within the camera's depth.</summary>
        public static bool NearestHit(IReadOnlyList<Capsule> capsules, Vec3 origin, out Capsule hit, out double distance)
        {
            hit = default;
            distance = double.PositiveInfinity;
            foreach (Capsule c in capsules)
            {
                if (c.Intersect(origin, FactorioCamera.Forward, out double enter, out _) && enter >= 0
                    && enter <= FactorioCamera.DepthTiles && enter < distance)
                {
                    hit = c;
                    distance = enter;
                }
            }
            return !double.IsPositiveInfinity(distance);
        }

        /// <summary>
        /// A point of the figure above the ground on the shadow ray from <paramref name="ground"/>, if any.
        /// </summary>
        public static bool ShadowCaster(IReadOnlyList<Capsule> capsules, Vec3 ground, out Vec3 caster)
        {
            foreach (Capsule c in capsules)
            {
                if (c.Intersect(ground, Conventions.ShadowLight, out double enter, out double exit) && exit > 0)
                {
                    caster = ground + Conventions.ShadowLight * ((Math.Max(enter, 0) + exit) * 0.5);
                    return true;
                }
            }
            caster = default;
            return false;
        }

        /// <summary>Final pixel position of the centre of render pixel (x, y).</summary>
        public static (double x, double y) Centre(CanvasSpec canvas, int x, int y) =>
            ((x + 0.5) / canvas.Supersampling, (y + 0.5) / canvas.Supersampling);
    }

    /// <summary>Pixel helpers for renders.</summary>
    internal static class Pixels
    {
        public static (float r, float g, float b, float a) Get(ImageF image, int x, int y)
        {
            int i = image.Index(x, y);
            return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
        }

        /// <summary>A color pass pixel the figure covers (anything but the black background).</summary>
        public static bool IsFigure(RenderResult result, int x, int y)
        {
            (float r, float g, float b, _) = Get(result.OnBlack, x, y);
            return r != 0 || g != 0 || b != 0;
        }

        /// <summary>A silhouette pixel in shadow (black).</summary>
        public static bool IsShadow(RenderResult result, int x, int y) => Get(result.Silhouette, x, y).r == 0;

        /// <summary>A right limb pixel (red-ish; the skin colored head is not).</summary>
        public static bool IsReddish(RenderResult result, int x, int y)
        {
            (float r, float g, float b, _) = Get(result.OnBlack, x, y);
            return r > g + 0.25 && r > b + 0.25;
        }

        /// <summary>A left limb pixel (blue-ish).</summary>
        public static bool IsBluish(RenderResult result, int x, int y)
        {
            (float r, _, float b, _) = Get(result.OnBlack, x, y);
            return b > r + 0.25;
        }

        /// <summary>Mean position (render pixels) and count of the pixels matching <paramref name="predicate"/>.</summary>
        public static (double x, double y, int count) Centroid(ImageF image, Func<int, int, bool> predicate)
        {
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (predicate(x, y))
                    {
                        sx += x + 0.5;
                        sy += y + 0.5;
                        count++;
                    }
                }
            }
            return count == 0 ? (double.NaN, double.NaN, 0) : (sx / count, sy / count, count);
        }

        /// <summary>The number of pixels with alpha &gt; 0, and whether any of them lies within <paramref name="margin"/> of an edge.</summary>
        public static (int covered, bool touchesMargin) Coverage(Image8 image, int margin)
        {
            int covered = 0;
            bool touches = false;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.Alpha(x, y) == 0)
                    {
                        continue;
                    }
                    covered++;
                    if (x < margin || y < margin || x >= image.Width - margin || y >= image.Height - margin)
                    {
                        touches = true;
                    }
                }
            }
            return (covered, touches);
        }
    }

    /// <summary>Requests for the stick figure renderer.</summary>
    internal static class Requests
    {
        public static FrameRequest Color(PoseResult pose, CanvasSpec canvas, double orientation = 0, bool mirrored = false) =>
            new FrameRequest
            {
                Animation = AnimationSpec.Idle,
                Pose = pose,
                Orientation = orientation,
                Mirrored = mirrored,
                Pass = RenderPass.Color,
                Canvas = canvas,
            };

        public static FrameRequest Shadow(PoseResult pose, CanvasSpec canvas, double orientation = 0, bool mirrored = false)
        {
            FrameRequest request = Color(pose, canvas, orientation, mirrored);
            request.Pass = RenderPass.Shadow;
            return request;
        }
    }
}
