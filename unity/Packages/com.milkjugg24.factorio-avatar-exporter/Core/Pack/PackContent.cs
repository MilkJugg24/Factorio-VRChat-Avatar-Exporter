using System;
using System.Collections.Generic;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Everything an avatar pack holds (docs/avatar-pack-format.md). <see cref="PackValidator"/> checks
    /// it and <see cref="PackWriter"/> writes it.
    /// </summary>
    public sealed class PackContent
    {
        public const string DefaultGeneratorName = "factorio-avatar-exporter";
        public const string DefaultVersion = "1.0.0";

        /// <summary>The pack id (<see cref="PackNaming.IsValidId"/>; <see cref="PackNaming.DeriveId"/> makes one).</summary>
        public string Id { get; set; }

        /// <summary>Shown in the avatar window; 1 to 80 characters on one line.</summary>
        public string Title { get; set; }

        /// <summary>The pack's author; optional (null or empty leaves it out).</summary>
        public string Author { get; set; }

        /// <summary>The author when none is given: a generic name (Factorio requires one in info.json).</summary>
        public const string DefaultAuthor = "Anonymous";

        /// <summary>The mod version, "major.minor.patch".</summary>
        public string Version { get; set; } = DefaultVersion;

        /// <summary>Name of the avatar the pack was made from, for README.txt.</summary>
        public string SourceAvatar { get; set; }

        /// <summary>
        /// A note on the avatar's license or permission, optional; written to README.txt when given, and may span
        /// several lines.
        /// </summary>
        public string Permission { get; set; }

        /// <summary>The tool that made the pack, written to the pack description and README.txt.</summary>
        public string GeneratorName { get; set; } = DefaultGeneratorName;
        public string GeneratorVersion { get; set; }

        /// <summary>64 (sprites drawn at scale 0.5) or 32 (scale 1).</summary>
        public int PixelsPerTile { get; set; } = Conventions.DefaultPixelsPerTile;

        /// <summary>
        /// Animations drawn at another resolution than <see cref="PixelsPerTile"/> (32 when too large for sheets
        /// at 64), by name.
        /// </summary>
        public Dictionary<string, int> AnimationPixelsPerTile { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>True when the flight animations (<see cref="AnimationInfo.InAir"/>) are included.</summary>
        public bool Flight { get; set; }

        public PackTiming Timing { get; set; } = new PackTiming();

        /// <summary>animation_speed overrides by animation name; animations not listed keep their default speed.</summary>
        public Dictionary<string, double> AnimationSpeeds { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>The square picture in the avatar window (usually 64 x 64).</summary>
        public Image8 Icon { get; set; }

        /// <summary>The 144 x 144 picture in Factorio's mod list.</summary>
        public Image8 Thumbnail { get; set; }

        /// <summary>Sheets by animation name, then by layer name (<see cref="PackLayers"/>).</summary>
        public Dictionary<string, Dictionary<string, SheetSet>> Animations { get; } =
            new Dictionary<string, Dictionary<string, SheetSet>>(StringComparer.Ordinal);

        /// <summary>True when the pack has mask layers (tinted with the player colour).</summary>
        public bool Tintable
        {
            get
            {
                foreach (Dictionary<string, SheetSet> layers in Animations.Values)
                {
                    if (layers != null && layers.ContainsKey(PackLayers.Mask))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Sets one layer of an animation, adding the animation when it is new.</summary>
        public void SetLayer(string animation, string layer, SheetSet sheets)
        {
            if (animation == null || layer == null)
            {
                throw new ArgumentNullException(animation == null ? nameof(animation) : nameof(layer));
            }
            if (!Animations.TryGetValue(animation, out Dictionary<string, SheetSet> layers) || layers == null)
            {
                layers = new Dictionary<string, SheetSet>(StringComparer.Ordinal);
                Animations[animation] = layers;
            }
            layers[layer] = sheets ?? throw new ArgumentNullException(nameof(sheets));
        }
    }
}
