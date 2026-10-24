using System;
using System.Collections.Generic;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Constants of the avatar pack format (docs/avatar-pack-format.md); the same values as
    /// tools/avatar_pack/spec.py and avatar-characters/scripts/pack-format.lua.
    /// </summary>
    public static class PackFormat
    {
        public const int FormatVersion = 1;

        /// <summary>Prefix of the pack's mod name and mod-data prototype: "avatar-pack-&lt;id&gt;".</summary>
        public const string ModPrefix = "avatar-pack-";

        /// <summary>The data_type of every pack's mod-data prototype.</summary>
        public const string DataType = "avatar-characters.avatar-pack";

        public const int MaxIdLength = 35;

        /// <summary>Longest pack title, in characters (Unicode code points).</summary>
        public const int MaxTitleLength = 80;

        /// <summary>Largest sprite sheet file, px in either direction.</summary>
        public const int MaxSheetSize = 4096;

        /// <summary>Most frames of one animation (Factorio's limit).</summary>
        public const int MaxFrames = 255;

        /// <summary>Largest icon size avatar-characters accepts, px.</summary>
        public const int MaxIconSize = 512;

        /// <summary>Size of thumbnail.png, the picture in Factorio's mod list, px.</summary>
        public const int ThumbnailSize = 144;

        /// <summary>
        /// Longest info.json title in UTF-8 bytes: Factorio 2.0 crashes on start, without a message, when
        /// a mod's title is longer.
        /// </summary>
        public const int InfoTitleMaxBytes = 100;

        /// <summary>Largest timing.distance_per_frame avatar-characters accepts, tiles.</summary>
        public const double MaxDistancePerFrame = 2.0;

        public const string FactorioVersion = "2.1";

        /// <summary>The Factorio mod that loads avatar packs.</summary>
        public const string MainMod = "avatar-characters";

        /// <summary>Its title in Factorio's mod list.</summary>
        public const string MainModTitle = "Avatar Characters";

        /// <summary>The first avatar-characters version that loads packs of this format (0.4.0 added frames_per_file, 0.5.0 an animation's own pixels_per_tile).</summary>
        public const string MinMainModVersion = "0.5.0";

        /// <summary>The info.json dependency every pack declares.</summary>
        public const string Dependency = MainMod + " >= " + MinMainModVersion;

        /// <summary>The chat command that turns every character using a pack back into the engineer.</summary>
        public const string RevertCommand = "/avatar-revert-all";

        /// <summary>The data.lua of every pack, byte for byte (DATA_LUA in spec.py).</summary>
        public const string DataLua =
            "local pack = require(\"prototypes.avatar-pack\")\n" +
            "data:extend({\n" +
            "  {\n" +
            "    type = \"mod-data\",\n" +
            "    name = \"avatar-pack-\" .. pack.id,\n" +
            "    data_type = \"avatar-characters.avatar-pack\",\n" +
            "    data = pack,\n" +
            "  },\n" +
            "})\n";
    }

    /// <summary>Layer names of a pack animation (docs/avatar-pack-format.md, "Layers").</summary>
    public static class PackLayers
    {
        /// <summary>The character, straight-alpha RGBA. Required.</summary>
        public const string Base = "base";

        /// <summary>Greyscale shading of the parts tinted with the player colour; in every animation or none.</summary>
        public const string Mask = "mask";

        /// <summary>The ground shadow: black, alpha = strength. Required.</summary>
        public const string Shadow = "shadow";

        /// <summary>The shadow of the mirrored sprite, still cast east. Optional, run-gun only.</summary>
        public const string ShadowFlipped = "shadow-flipped";

        private static readonly IReadOnlyList<string> NoLayers = Array.Empty<string>();
        private static readonly IReadOnlyList<string> RunGunOptional = new[] { ShadowFlipped };

        /// <summary>Layers <paramref name="animation"/> may have besides base, shadow and mask.</summary>
        public static IReadOnlyList<string> OptionalFor(string animation) =>
            animation == AnimationSpec.RunGun ? RunGunOptional : NoLayers;
    }
}
