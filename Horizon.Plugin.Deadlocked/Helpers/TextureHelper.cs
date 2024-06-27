using DotNetty.Common.Internal.Logging;
using Horizon.Plugin.Deadlocked;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class TextureHelper
{
    private static byte[] _cachedBannerImagePalette = null;
    private static byte[] _cachedBannerImagePixels = null;
    private static int _cachedBannerImageHash = default;

    public static int RemapPixelIndexFromRac4(int i, int width)
    {
        int s = i / (width * 2);
        int r = 0;
        if (s % 2 == 0)
            r = s * 2;
        else
            r = (s - 1) * 2 + 1;

        int q = ((i % (width * 2)) / 32);

        int m = i % 4;
        int n = (i / 4) % 4;
        int o = i % 2;
        int p = (i / 16) % 2;

        if ((s / 2) % 2 == 1)
            p = 1 - p;

        if (o == 0)
            m = (m + p) % 4;
        else
            m = ((m - p) + 4) % 4;


        int x = n + ((m + q * 4) * 4);
        int y = r + (o * 2);

        return (x % width) + (y * width);
    }

    public static byte DecodePaletteIndex(byte index, int high = 4, int low = 3)
    {
        int dif = high - low;
        uint mask1 = (uint)1 << high;
        uint mask2 = (uint)1 << low;
        uint mask3 = ~(mask1 | mask2);
        uint a = (uint)index & mask3;

        return (byte)(((index & mask1) >> dif) | ((index & mask2) << dif) | a);
    }

    public static bool GetPIFData(string pngBase64Str, out byte[] paletteBytes, out byte[] pixelBytes)
    {
        paletteBytes = null;
        pixelBytes = null;

        if (string.IsNullOrEmpty(pngBase64Str)) return false;

        var hash = pngBase64Str.GetHashCode();
        if (_cachedBannerImageHash == hash)
        {
            paletteBytes = _cachedBannerImagePalette;
            pixelBytes = _cachedBannerImagePixels;
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(pngBase64Str);
            using (var stream = new MemoryStream(bytes))
            {
                var png = new Png(stream);
                png.Resize(128, 64);
                png.Quantize(256);
                png.GetRawIndexedData(out paletteBytes, out pixelBytes);

                _cachedBannerImageHash = hash;
                _cachedBannerImagePalette = paletteBytes;
                _cachedBannerImagePixels = pixelBytes;
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Host.Log(InternalLogLevel.ERROR, $"Error converting banner image {ex.Message}");
        }

        return false;
    }
}
