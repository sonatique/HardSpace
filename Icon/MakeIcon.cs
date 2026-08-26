using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Draws the HardSpace icon: a folder carrying a chain link, which is what the tool is about.
// Every size is drawn at its own resolution rather than downscaled from one large bitmap: at 16px a
// downscaled chain turns to mush, while a chain drawn for 16px keeps two readable loops.
public static class MakeIcon
{
    public static string Build(string path, int[] sizes)
    {
        var frames = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (Bitmap bmp = Draw(size))
                frames.Add(size >= 256 ? EncodePng(bmp) : EncodeDib(bmp));
        }

        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(file))
        {
            w.Write((ushort)0);                 // reserved
            w.Write((ushort)1);                 // type: icon
            w.Write((ushort)frames.Count);

            int offset = 6 + (16 * frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                byte dim = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
                w.Write(dim); w.Write(dim);
                w.Write((byte)0); w.Write((byte)0);      // palette size, reserved
                w.Write((ushort)1); w.Write((ushort)32); // planes, bits per pixel
                w.Write((uint)frames[i].Length);
                w.Write((uint)offset);
                offset += frames[i].Length;
            }

            foreach (byte[] frame in frames)
                w.Write(frame);
        }

        return path + "  (" + new FileInfo(path).Length + " bytes, " + frames.Count + " frames)";
    }

    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float u = size / 16f;    // one unit = one pixel at 16x16, so the design scales exactly
            var folder = Color.FromArgb(255, 60, 122, 184);
            var folderDark = Color.FromArgb(255, 38, 86, 134);

            using (var brush = new SolidBrush(folder))
            using (var pen = new Pen(folderDark, 0.7f * u))
            {
                // Folder: a tab across the top left, then the body under it.
                g.FillRectangle(brush, 1.0f * u, 2.4f * u, 6.4f * u, 2.6f * u);
                g.FillRectangle(brush, 1.0f * u, 4.0f * u, 14.0f * u, 9.6f * u);
                g.DrawRectangle(pen, 1.0f * u, 4.0f * u, 14.0f * u, 9.6f * u);
            }

            // Chain link: two loops, the second broken where it passes behind the first, which is
            // what reads as "interlocked" even when it is only a few pixels across.
            using (var link = new Pen(Color.White, 1.6f * u))
            {
                link.StartCap = LineCap.Round;
                link.EndCap = LineCap.Round;
                g.DrawEllipse(link, 3.2f * u, 6.4f * u, 5.8f * u, 4.6f * u);
                g.DrawArc(link, 7.0f * u, 6.4f * u, 5.8f * u, 4.6f * u, -55, 250);
            }
        }

        return bmp;
    }

    static byte[] EncodePng(Bitmap bmp)
    {
        using (var stream = new MemoryStream())
        {
            bmp.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    // A frame below 256px is stored as a DIB, not PNG: GDI+ cannot read PNG-compressed frames at
    // all -- it silently hands back the next size down -- and older shell paths are the same. PNG is
    // used only at 256, where the format effectively requires it.
    static byte[] EncodeDib(Bitmap bmp)
    {
        int size = bmp.Width;
        var pixels = new byte[size * size * 4];
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        int maskStride = ((size + 31) / 32) * 4;

        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            // BITMAPINFOHEADER -- the height is doubled because the AND mask counts as a second image.
            w.Write((uint)40);
            w.Write(size);
            w.Write(size * 2);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)0);
            w.Write((uint)((size * size * 4) + (maskStride * size)));
            w.Write(0); w.Write(0); w.Write((uint)0); w.Write((uint)0);

            for (int y = size - 1; y >= 0; y--)      // colour data is bottom-up
                w.Write(pixels, y * size * 4, size * 4);

            w.Write(new byte[maskStride * size]);    // AND mask: zeroed, the alpha channel carries it

            w.Flush();
            return stream.ToArray();
        }
    }
}
