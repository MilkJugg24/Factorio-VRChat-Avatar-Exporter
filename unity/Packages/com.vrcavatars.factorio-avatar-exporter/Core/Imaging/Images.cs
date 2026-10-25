using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// A float RGBA image. Pixels are interleaved R, G, B, A, row by row, with row 0 at the TOP
    /// (Unity's ReadPixels gives row 0 at the bottom; the Unity layer flips rows when copying in).
    /// Whether colors are premultiplied, linear or sRGB depends on where the image comes from.
    /// </summary>
    public sealed class ImageF
    {
        public ImageF(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "image size must be positive");
            }
            Width = width;
            Height = height;
            Pixels = new float[width * height * 4];
        }

        public ImageF(int width, int height, float[] pixels)
        {
            if (pixels == null || pixels.Length != width * height * 4)
            {
                throw new ArgumentException("pixel array does not match the size", nameof(pixels));
            }
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public float[] Pixels { get; }

        public int Index(int x, int y) => (y * Width + x) * 4;

        public float this[int x, int y, int channel]
        {
            get => Pixels[Index(x, y) + channel];
            set => Pixels[Index(x, y) + channel] = value;
        }

        public void Fill(float r, float g, float b, float a)
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                Pixels[i] = r;
                Pixels[i + 1] = g;
                Pixels[i + 2] = b;
                Pixels[i + 3] = a;
            }
        }
    }

    /// <summary>
    /// An 8-bit straight-alpha sRGB RGBA image, as written to PNG. Row 0 is at the top.
    /// </summary>
    public sealed class Image8
    {
        public Image8(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "image size must be positive");
            }
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public Image8(int width, int height, byte[] pixels)
        {
            if (pixels == null || pixels.Length != width * height * 4)
            {
                throw new ArgumentException("pixel array does not match the size", nameof(pixels));
            }
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public int Index(int x, int y) => (y * Width + x) * 4;

        public byte this[int x, int y, int channel]
        {
            get => Pixels[Index(x, y) + channel];
            set => Pixels[Index(x, y) + channel] = value;
        }

        public byte Alpha(int x, int y) => Pixels[Index(x, y) + 3];
    }
}
