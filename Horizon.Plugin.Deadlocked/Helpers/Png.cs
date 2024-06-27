using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class Png
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Indexed { get; private set; }
    public int Bpp { get; private set; }

    private PngColor[] Palette { get; set; }
    private int[] PixelIndices { get; set; }
    private PngColor[] PixelColors { get; set; }

    public Png(int paletteSize, int bpp, int width, int height)
    {
        Palette = new PngColor[paletteSize];
        Width = width;
        Height = height;
        PixelIndices = new int[width * height];
        Indexed = true;
        Bpp = bpp;
    }

    public Png(string path)
    {
        ReadFromFile(path);
    }

    public Png(Stream stream)
    {
        ReadFromStream(stream);
    }

    public Png(Png png)
    {
        Width = png.Width;
        Height = png.Height;
        Indexed = png.Indexed;
        Bpp = png.Bpp;
        Palette = png.Palette?.ToArray();
        PixelIndices = png.PixelIndices?.ToArray();
        PixelColors = png.PixelColors?.ToArray();
    }

    public Png(int width, int height, PngColor[] pixels)
    {
        Width = width;
        Height = height;
        Indexed = false;
        PixelColors = pixels;
        Bpp = 32;
    }

    #region Resize

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;

        if (Indexed) resizeIndexed(width, height);
        else resize(width, height);
    }

    private static byte BilinearInterpolation(byte c00, byte c01, byte c10, byte c11, float xFraction, float yFraction)
    {
        return (byte)(c00 * (1 - xFraction) * (1 - yFraction) +
                      c01 * (1 - xFraction) * yFraction +
                      c10 * xFraction * (1 - yFraction) +
                      c11 * xFraction * yFraction);
    }

    private void resize(int width, int height)
    {
        var newPixelColors = new PngColor[width * height];
        float widthRatio = (float)(Width - 1) / (width - 1);
        float heightRatio = (float)(Height - 1) / (height - 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; ++x)
            {
                float sourceX = x * widthRatio;
                float sourceY = y * heightRatio;

                int x0 = (int)sourceX;
                int y0 = (int)sourceY;
                int x1 = Math.Min(x0 + 1, Width - 1);
                int y1 = Math.Min(y0 + 1, Height - 1);

                float xFraction = sourceX - x0;
                float yFraction = sourceY - y0;

                var c00 = GetPixel(x0, y0);
                var c01 = GetPixel(x0, y1);
                var c10 = GetPixel(x1, y0);
                var c11 = GetPixel(x1, y1);

                byte r = BilinearInterpolation(c00.R, c01.R, c10.R, c11.R, xFraction, yFraction);
                byte g = BilinearInterpolation(c00.G, c01.G, c10.G, c11.G, xFraction, yFraction);
                byte b = BilinearInterpolation(c00.B, c01.B, c10.B, c11.B, xFraction, yFraction);
                byte a = BilinearInterpolation(c00.A, c01.A, c10.A, c11.A, xFraction, yFraction);

                newPixelColors[y * width + x] = new PngColor(r, g, b, a);
            }
        }

        PixelColors = newPixelColors;
        Width = width;
        Height = height;
    }


    private void resizeIndexed(int width, int height)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Quantization

    public void Quantize(int paletteSize)
    {
        // already quantized
        if (Indexed && Palette.Length <= paletteSize) return;

        if (Indexed) requantize(paletteSize);
        else quantize(paletteSize);
    }

    private void requantize(int paletteSize)
    {
        // quantize
        var newPalette = new PngColor[paletteSize];
        var newIndices = new int[Width * Height];

        var quantizer = new Octree.PaletteQuantizer();
        foreach (var col in Palette)
            quantizer.AddColour(col);
        quantizer.Quantize(paletteSize);

        int totalIndex = 0, paletteCount = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var colour = quantizer.GetQuantizedColour(GetPixel(x, y));
                var index = Array.IndexOf(newPalette, colour);
                if (index >= 0)
                {
                    newIndices[totalIndex] = index; //TextureHelpers.DecodePaletteIndex((byte)index);
                }
                else
                {
                    index = (byte)paletteCount++;
                    newIndices[totalIndex] = index; //TextureHelpers.DecodePaletteIndex((byte)index);
                    newPalette[index] = colour;
                }
                totalIndex++;
            }
        }

        // cleanup old pixels
        Indexed = true;
        PixelColors = null;
        Palette = newPalette;
        PixelIndices = newIndices;
    }

    private void quantize(int paletteSize)
    {
        // quantize
        Palette = new PngColor[paletteSize];
        PixelIndices = new int[Width * Height];

        var quantizer = new Octree.PaletteQuantizer();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var colour = GetPixel(x, y);
                quantizer.AddColour(colour);
            }
        }
        quantizer.Quantize(paletteSize);

        int totalIndex = 0, paletteCount = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var colour = quantizer.GetQuantizedColour(GetPixel(x, y));
                var index = Array.IndexOf(Palette, colour);
                if (index >= 0)
                {
                    // bump palette in case where first color is empty
                    if (paletteCount == 0) ++paletteCount;

                    PixelIndices[totalIndex] = index; //TextureHelpers.DecodePaletteIndex((byte)index);
                }
                else
                {
                    index = (byte)paletteCount++;
                    PixelIndices[totalIndex] = index; //TextureHelpers.DecodePaletteIndex((byte)index);
                    Palette[index] = colour;
                }
                totalIndex++;
            }
        }

        // cleanup old pixels
        Indexed = true;
        PixelColors = null;
    }

    #endregion

    #region Get

    public int GetPaletteCount()
    {
        return Indexed ? Palette.Length : 0;
    }

    public PngColor GetPaletteColor(int index)
    {
        if (!Indexed) throw new InvalidOperationException("Cannot write palette of non-indexed png.");

        return Palette[index];
    }

    public PngColor GetPixel(int x, int y)
    {
        var idx = y * Width + x;
        if (Indexed)
        {
            return Palette[PixelIndices[idx]];
        }
        else
        {
            return PixelColors[idx];
        }
    }

    public int GetPixelIndexed(int x, int y)
    {
        if (!Indexed) throw new InvalidOperationException("Png is not indexed");

        var idx = y * Width + x;
        return PixelIndices[idx];
    }

    public void GetRawIndexedData(out byte[] paletteBuffer, out byte[] indexBuffer, bool decode = true)
    {
        if (!Indexed) throw new InvalidOperationException("Png is not indexed");

        paletteBuffer = new byte[Palette.Length * 4];
        indexBuffer = new byte[PixelIndices.Length];

        using (var ms = new MemoryStream(paletteBuffer, true))
        {
            using (var writer = new BinaryWriter(ms))
            {
                for (int i = 0; i < Palette.Length; ++i)
                {
                    Palette[i].Write(writer);
                }
            }
        }

        for (int i = 0; i < PixelIndices.Length; ++i)
        {
            indexBuffer[i] = decode ? TextureHelper.DecodePaletteIndex((byte)PixelIndices[i]) : (byte)PixelIndices[i];
        }
    }

    #endregion

    #region Set

    public void SetPaletteColor(int idx, PngColor color)
    {
        if (!Indexed) throw new InvalidOperationException("Png is not indexed");

        Palette[idx] = color;
    }

    public void SetPixelIndexed(int x, int y, int palIndex)
    {
        if (!Indexed) throw new InvalidOperationException("Png is not indexed");

        if (y * Width + x >= PixelIndices.Length)
        {

        }
        PixelIndices[y * Width + x] = palIndex;
    }

    public void SetPixelColor(int x, int y, PngColor color)
    {
        if (Indexed) throw new InvalidOperationException("Png is indexed");

        PixelColors[y * Width + x] = color;
    }

    #endregion

    #region Read

    private void ReadFromFile(string path)
    {
        PngReader pngr = FileHelper.CreatePngReader(path);
        try
        {
            pngr.SetUnpackedMode(true); // we dont want to do the unpacking ourselves, we want a sample per array element
            var imageInfo = pngr.ImgInfo;
            Indexed = imageInfo.Indexed;
            Height = imageInfo.Rows;
            Width = imageInfo.Cols;
            Bpp = imageInfo.BitspPixel;

            if (Indexed)
            {
                PngChunkPLTE plte = pngr.GetMetadata().GetPLTE();
                PngChunkTRNS trns = pngr.GetMetadata().GetTRNS();
                Palette = new PngColor[plte.GetNentries()];
                PixelIndices = new int[Width * Height];

                // read palette
                for (int i = 0; i < Palette.Length; i++)
                    Palette[i] = new PngColor(plte, trns, i);

                // read pixel indices
                for (int row = 0; row < pngr.ImgInfo.Rows; ++row)
                {
                    ImageLine line = pngr.ReadRowInt(row);
                    bool isbyte = line.SampleType == Hjg.Pngcs.ImageLine.ESampleType.BYTE;
                    for (int x = 0; x < Width; ++x)
                    {
                        if (line.Scanline[x] >= Palette.Length)
                        {

                        }
                        PixelIndices[row * Width + x] = isbyte ? line.ScanlineB[x] : line.Scanline[x];
                    }
                }
            }
            else
            {
                PixelColors = new PngColor[Height * Width];
                for (int row = 0; row < pngr.ImgInfo.Rows; ++row)
                {
                    ImageLine imline = pngr.ReadRowInt(row);
                    for (int x = 0; x < Width; ++x)
                    {
                        PixelColors[row * Width + x] = new PngColor(imline, x);
                    }
                }
            }
        }
        finally
        {
            pngr?.End();
        }
    }

    private void ReadFromStream(Stream stream)
    {
        PngReader pngr = new PngReader(stream);
        try
        {
            pngr.SetUnpackedMode(true); // we dont want to do the unpacking ourselves, we want a sample per array element
            var imageInfo = pngr.ImgInfo;
            Indexed = imageInfo.Indexed;
            Height = imageInfo.Rows;
            Width = imageInfo.Cols;
            Bpp = imageInfo.BitspPixel;

            if (Indexed)
            {
                PngChunkPLTE plte = pngr.GetMetadata().GetPLTE();
                PngChunkTRNS trns = pngr.GetMetadata().GetTRNS();
                Palette = new PngColor[plte.GetNentries()];
                PixelIndices = new int[Width * Height];

                // read palette
                for (int i = 0; i < Palette.Length; i++)
                    Palette[i] = new PngColor(plte, trns, i);

                // read pixel indices
                for (int row = 0; row < pngr.ImgInfo.Rows; ++row)
                {
                    ImageLine line = pngr.ReadRowInt(row);
                    bool isbyte = line.SampleType == Hjg.Pngcs.ImageLine.ESampleType.BYTE;
                    for (int x = 0; x < Width; ++x)
                    {
                        if (line.Scanline[x] >= Palette.Length)
                        {

                        }
                        PixelIndices[row * Width + x] = isbyte ? line.ScanlineB[x] : line.Scanline[x];
                    }
                }
            }
            else
            {
                PixelColors = new PngColor[Height * Width];
                for (int row = 0; row < pngr.ImgInfo.Rows; ++row)
                {
                    ImageLine imline = pngr.ReadRowInt(row);
                    for (int x = 0; x < Width; ++x)
                    {
                        PixelColors[row * Width + x] = new PngColor(imline, x);
                    }
                }
            }
        }
        finally
        {
            pngr?.End();
        }
    }

    #endregion

    #region Save

    public void Save(string outPath)
    {
        if (Indexed)
        {
            SaveIndexed(outPath);
        }
        else
        {
            SaveColored(outPath);
        }
    }

    public void WritePalette(BinaryWriter writer, int? count = null)
    {
        if (!Indexed) throw new InvalidOperationException("Cannot write palette of non-indexed png.");

        var n = count ?? Palette.Length;
        for (int x = 0; x < n; ++x)
        {
            if (x < Palette.Length) Palette[x].Write(writer);
            else writer.Write(0);
        }
    }

    public void WritePixelIndices(BinaryWriter writer)
    {
        if (!Indexed) throw new InvalidOperationException("Cannot write pixel indices of non-indexed png.");
        if (Bpp == 4 && Palette.Length > 16) throw new InvalidOperationException($"Cannot write 4bpp png with palette of size {Palette.Length}. Palette must be at most 16 colors.");

        var n = Width * Height;
        for (int x = 0; x < n; ++x)
        {
            if (Bpp == 4)
            {
                writer.Write((byte)((PixelIndices[x + 1] & 0xf) << 4 | PixelIndices[x] & 0xf));
                ++x;
            }
            else
            {
                writer.Write((byte)(PixelIndices[x] & 0xff));
            }
        }
    }

    private void SaveColored(string outPath)
    {
        PngWriter pngw = FileHelper.CreatePngWriter(outPath, new ImageInfo(Width, Height, 8, true, false, false), true);
        try
        {
            pngw.SetUseUnPackedMode(true);

            // write pixels
            int[] buf = new int[Width * 4];
            for (int row = 0; row < Height; ++row)
            {
                var rowIdx = row * Width;
                for (int x = 0; x < Width; ++x)
                {
                    buf[x * 4 + 0] = PixelColors[rowIdx + x].R;
                    buf[x * 4 + 1] = PixelColors[rowIdx + x].G;
                    buf[x * 4 + 2] = PixelColors[rowIdx + x].B;
                    buf[x * 4 + 3] = PixelColors[rowIdx + x].A;
                }

                pngw.WriteRow(buf, row);
            }

        }
        finally
        {
            pngw?.End();
        }
    }

    private void SaveIndexed(string outPath)
    {
        PngWriter pngw = FileHelper.CreatePngWriter(outPath, new ImageInfo(Width, Height, 8, false, false, true), true);

        try
        {
            pngw.SetUseUnPackedMode(true);

            // write palette
            var plte = pngw.GetMetadata().CreatePLTEChunk();
            var trns = pngw.GetMetadata().CreateTRNSChunk();

            plte.SetNentries(Palette.Length);
            for (int i = 0; i < Palette.Length; ++i)
                plte.SetEntry(i, Palette[i].R, Palette[i].G, Palette[i].B);
            trns.SetPalletteAlpha(Palette.Select(x => (int)x.A).ToArray());

            // write pixel indices
            for (int row = 0; row < Height; ++row)
            {
                var rowIdx = row * Width;
                var indices = PixelIndices.Skip(rowIdx).Take(Width).ToArray();
                pngw.WriteRow(indices, row);
            }

        }
        finally
        {
            pngw?.End();
        }
    }

    #endregion

}

public struct PngColor
{
    public static PngColor Empty = new PngColor(0, 0, 0, 0);

    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public PngColor(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public PngColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        A = 255;
    }

    public PngColor(int r, int g, int b, int a)
    {
        R = Clamp(r);
        G = Clamp(g);
        B = Clamp(b);
        A = Clamp(a);
    }

    public PngColor(ImageLine line, int x)
    {
        var channels = line.ImgInfo.Channels;
        var bpp = line.ImgInfo.BitspPixel;
        var bdepth = line.ImgInfo.BitDepth;

        if (bdepth != 8) throw new NotImplementedException($"Png is unsupported {bdepth} bitdepth");

        switch ((bpp, channels))
        {
            case (32, 4):
                {
                    var idx = x * channels;
                    R = (byte)line.Scanline[idx + 0];
                    G = (byte)line.Scanline[idx + 1];
                    B = (byte)line.Scanline[idx + 2];
                    A = (byte)line.Scanline[idx + 3];
                    break;
                }
            case (24, 3):
                {
                    var idx = x * channels;
                    R = (byte)line.Scanline[idx + 0];
                    G = (byte)line.Scanline[idx + 1];
                    B = (byte)line.Scanline[idx + 2];
                    A = 255;
                    break;
                }
            default:
                {
                    throw new NotImplementedException($"Png is unsupported {bpp}bpp {channels} channels");
                }
        }

    }

    public PngColor(PngChunkPLTE palette, PngChunkTRNS trns, int x)
    {
        int[] color = new int[4];
        palette.GetEntryRgb(x, color);

        R = (byte)color[0];
        G = (byte)color[1];
        B = (byte)color[2];
        A = 255;

        // alpha
        if (trns != null)
        {
            var alpha = trns.GetPalletteAlpha();
            if (alpha != null && x < alpha.Length)
                A = (byte)alpha[x];
        }
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(R);
        writer.Write(G);
        writer.Write(B);
        writer.Write(A);
    }

    public void Read(BinaryReader reader)
    {
        R = reader.ReadByte();
        G = reader.ReadByte();
        B = reader.ReadByte();
        A = reader.ReadByte();
    }

    public static PngColor FromReader(BinaryReader reader)
    {
        return new PngColor(
            r: reader.ReadByte(),
            g: reader.ReadByte(),
            b: reader.ReadByte(),
            a: reader.ReadByte()
        );
    }

    public static PngColor operator +(PngColor a, PngColor b) => new PngColor(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
    public static PngColor operator -(PngColor a, PngColor b) => new PngColor(a.R - b.R, a.G - b.G, a.B - b.B, a.A - b.A);
    public static PngColor operator *(PngColor a, float s) => new PngColor((int)(a.R * s), (int)(a.G * s), (int)(a.B * s), (int)(a.A * s));
    public static PngColor operator /(PngColor a, float s) => new PngColor((int)(a.R / s), (int)(a.G / s), (int)(a.B / s), (int)(a.A / s));
    public static PngColor operator *(PngColor a, double s) => new PngColor((int)(a.R * s), (int)(a.G * s), (int)(a.B * s), (int)(a.A * s));
    public static PngColor operator /(PngColor a, double s) => new PngColor((int)(a.R / s), (int)(a.G / s), (int)(a.B / s), (int)(a.A / s));

    public override bool Equals(object obj)
    {
        if (obj is PngColor oColor)
            return oColor.R == R && oColor.G == G && oColor.B == B && oColor.A == A;

        return false;
    }

    public override int GetHashCode()
    {
        return R << 24 | G << 16 | B << 8 | A;
    }

    public override string ToString()
    {
        return $"{R:X2}{G:X2}{B:X2}{A:X2}";
    }

    private static byte Clamp(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;

        return (byte)value;
    }
}