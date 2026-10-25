namespace FactorioAvatarExporter.Core
{
    /// <summary>Constants of the PNG format shared by <see cref="PngWriter"/> and <see cref="PngReader"/>.</summary>
    internal static class PngFormat
    {
        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public const string Ihdr = "IHDR";
        public const string Plte = "PLTE";
        public const string Idat = "IDAT";
        public const string Iend = "IEND";
        public const string Trns = "tRNS";

        public const int IhdrLength = 13;

        public const int ColorGray = 0;
        public const int ColorRgb = 2;
        public const int ColorPalette = 3;
        public const int ColorGrayAlpha = 4;
        public const int ColorRgba = 6;

        public static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

        public static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
