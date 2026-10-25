using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// A software <see cref="IFrameRenderer"/> that draws poses as a <see cref="StickFigure"/>, for tests and
    /// the pack harness, where Unity is not available. It renders with the same camera as the Unity layer
    /// (<see cref="FactorioCamera"/>) at the canvas's supersampled resolution, one ray per pixel centre:
    /// <list type="bullet">
    /// <item><see cref="RenderPass.Color"/>: an orthographic view ray from the camera's near plane along
    /// <see cref="FactorioCamera.Forward"/>; the nearest capsule surface within the camera's
    /// <see cref="FactorioCamera.DepthTiles"/> deep view is shaded by <see cref="StickPalette.Shade"/>.
    /// The figure is opaque, so <see cref="RenderResult.OnBlack"/> and <see cref="RenderResult.OnWhite"/>
    /// hold the same color where it is and opaque black or white elsewhere (sRGB values, alpha 1).</item>
    /// <item><see cref="RenderPass.Shadow"/>: the ground point (z = 0) the pixel shows is black when a ray
    /// from it towards <see cref="Conventions.ShadowLight"/> passes through a capsule above the ground, white
    /// otherwise (<see cref="RenderResult.Silhouette"/>, alpha 1). This is the planar shadow
    /// (<see cref="PlanarShadow.Project"/>) of the figure's points above the ground.</item>
    /// </list>
    /// Capsules are culled per pixel with their screen-space boxes, so a frame costs little more than one
    /// pass over the image. Results are deterministic.
    /// </summary>
    public sealed class StickFigureRenderer : IFrameRenderer
    {
        /// <summary>
        /// The world bounds of every capsule of the figure (<see cref="StickFigure.Bounds"/>), placed and
        /// grounded exactly as <see cref="Render"/> places it; the color pass draws only points inside, and
        /// the shadow pass only shadows of points inside.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pose"/> is null.</exception>
        /// <exception cref="ArgumentException">The pose or orientation is not valid (see <see cref="StickFigure.Build"/>).</exception>
        public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
            StickFigure.Build(pose, orientation, mirrored).Bounds;

        /// <summary>Renders one pass of a frame; see the class description.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The request has no pose, its canvas is <c>default</c>, its pass is unknown, or its pose or
        /// orientation is not valid (see <see cref="StickFigure.Build"/>).
        /// </exception>
        public RenderResult Render(FrameRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.Pose == null)
            {
                throw new ArgumentException("the request has no pose", nameof(request));
            }
            FactorioCamera.RequireCanvas(request.Canvas);
            if (request.Pass != RenderPass.Color && request.Pass != RenderPass.Shadow)
            {
                throw new ArgumentException("unknown render pass " + request.Pass, nameof(request));
            }
            StickFigure figure = StickFigure.Build(request.Pose, request.Orientation, request.Mirrored);
            return request.Pass == RenderPass.Color
                ? RenderColor(figure.Capsules, request.Canvas)
                : RenderShadow(figure.Capsules, request.Canvas);
        }

        private static RenderResult RenderColor(IReadOnlyList<Capsule> capsules, CanvasSpec canvas)
        {
            var onBlack = new ImageF(canvas.RenderWidth, canvas.RenderHeight);
            var onWhite = new ImageF(canvas.RenderWidth, canvas.RenderHeight);
            onBlack.Fill(0, 0, 0, 1);
            onWhite.Fill(1, 1, 1, 1);
            var rows = new CapsuleRows(capsules, ScreenMap.Camera(canvas), canvas.RenderWidth, canvas.RenderHeight);
            Vec3 forward = FactorioCamera.Forward;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                IReadOnlyList<CapsuleSpan> spans = rows.Row(y);
                (int first, int last) = Columns(spans);
                for (int x = first; x <= last; x++)
                {
                    Vec3 origin = FactorioCamera.ViewRayOrigin(
                        (x + 0.5) / canvas.Supersampling, (y + 0.5) / canvas.Supersampling, canvas);
                    if (!NearestHit(capsules, spans, x, origin, out Capsule hit, out double distance))
                    {
                        continue;
                    }
                    (double r, double g, double b) = StickPalette.Shade(hit.Part, hit.Normal(origin + forward * distance));
                    SetOpaque(onBlack, x, y, r, g, b);
                    SetOpaque(onWhite, x, y, r, g, b);
                }
            }
            return new RenderResult { OnBlack = onBlack, OnWhite = onWhite, Linear = false };
        }

        private static RenderResult RenderShadow(IReadOnlyList<Capsule> capsules, CanvasSpec canvas)
        {
            var silhouette = new ImageF(canvas.RenderWidth, canvas.RenderHeight);
            silhouette.Fill(1, 1, 1, 1);
            var rows = new CapsuleRows(capsules, ScreenMap.GroundShadow(canvas), canvas.RenderWidth,
                canvas.RenderHeight);
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                IReadOnlyList<CapsuleSpan> spans = rows.Row(y);
                (int first, int last) = Columns(spans);
                for (int x = first; x <= last; x++)
                {
                    Vec3 ground = FactorioCamera.GroundPoint(
                        (x + 0.5) / canvas.Supersampling, (y + 0.5) / canvas.Supersampling, canvas);
                    if (InShadow(capsules, spans, x, ground))
                    {
                        SetOpaque(silhouette, x, y, 0, 0, 0);
                    }
                }
            }
            return new RenderResult { Silhouette = silhouette, Linear = false };
        }

        /// <summary>The columns any of <paramref name="spans"/> covers; first > last when there are none.</summary>
        private static (int first, int last) Columns(IReadOnlyList<CapsuleSpan> spans)
        {
            int first = int.MaxValue;
            int last = int.MinValue;
            for (int i = 0; i < spans.Count; i++)
            {
                first = Math.Min(first, spans[i].First);
                last = Math.Max(last, spans[i].Last);
            }
            return (first, last);
        }

        /// <summary>
        /// The capsule the view ray from <paramref name="origin"/> enters first, and how far along the ray,
        /// among the capsules spanning column <paramref name="x"/>. Entries behind the near plane or beyond
        /// the far plane do not count; ties go to the earlier capsule.
        /// </summary>
        private static bool NearestHit(IReadOnlyList<Capsule> capsules, IReadOnlyList<CapsuleSpan> spans, int x,
            Vec3 origin, out Capsule hit, out double distance)
        {
            hit = default;
            distance = double.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < spans.Count; i++)
            {
                CapsuleSpan span = spans[i];
                if (x < span.First || x > span.Last)
                {
                    continue;
                }
                Capsule capsule = capsules[span.Index];
                if (capsule.Intersect(origin, FactorioCamera.Forward, out double enter, out _)
                    && enter >= 0 && enter <= FactorioCamera.DepthTiles && enter < distance)
                {
                    hit = capsule;
                    distance = enter;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// True when the ray from <paramref name="ground"/> towards the shadow light runs inside a capsule
        /// spanning column <paramref name="x"/> somewhere above the ground.
        /// </summary>
        private static bool InShadow(IReadOnlyList<Capsule> capsules, IReadOnlyList<CapsuleSpan> spans, int x,
            Vec3 ground)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                CapsuleSpan span = spans[i];
                if (x >= span.First && x <= span.Last
                    && capsules[span.Index].Intersect(ground, Conventions.ShadowLight, out _, out double exit)
                    && exit > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SetOpaque(ImageF image, int x, int y, double r, double g, double b)
        {
            int i = image.Index(x, y);
            image.Pixels[i] = (float)r;
            image.Pixels[i + 1] = (float)g;
            image.Pixels[i + 2] = (float)b;
            image.Pixels[i + 3] = 1f;
        }
    }
}
