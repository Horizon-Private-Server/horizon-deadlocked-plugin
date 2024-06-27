using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

// Copied from github and modified slightly to support indexed palettes.
// https://github.com/bacowan/cSharpColourQuantization/blob/master/ColourQuantization/Octree.cs

class Octree
{
    //public static void QuantizeRaw(Bitmap bitmap, int colourCount, out byte[] palette, out byte[] pixels)
    //{
    //    palette = new byte[colourCount * 4];
    //    pixels = new byte[bitmap.Width * bitmap.Height];
    //    List<Color> paletteColors = new List<PngColor>();

    //    var quantizer = new PaletteQuantizer();
    //    for (int x = 0; x < bitmap.Width; x++)
    //    {
    //        for (int y = 0; y < bitmap.Height; y++)
    //        {
    //            var colour = bitmap.GetPixel(x, y);
    //            quantizer.AddColour(colour);
    //        }
    //    }
    //    quantizer.Quantize(colourCount);

    //    int totalIndex = 0;
    //    for (int y = 0; y < bitmap.Height; y++)
    //    {
    //        for (int x = 0; x < bitmap.Width; x++)
    //        {
    //            var colour = quantizer.GetQuantizedColour(bitmap.GetPixel(x, y));
    //            int index = 0;
    //            if (paletteColors.Contains(colour))
    //            {
    //                index = paletteColors.IndexOf(colour);
    //                pixels[totalIndex] = TextureHelpers.DecodePaletteIndex((byte)index);
    //            }
    //            else
    //            {
    //                index = (byte)paletteColors.Count;
    //                pixels[totalIndex] = TextureHelpers.DecodePaletteIndex((byte)index); 
    //                paletteColors.Add(colour);
    //            }
    //            totalIndex++;
    //        }
    //    }

    //    while (paletteColors.Count < colourCount)
    //    {
    //        paletteColors.Add(Color.FromArgb(0, 0, 0, 0));
    //    }

    //    //palette = paletteColors.SelectMany((color) => new byte[4] { color.R, color.G, color.B, (byte)(color.A > 0 ? MathF.Ceiling(color.A / 2f) : 0) }).ToArray();
    //    palette = paletteColors.SelectMany((color) => new byte[4] { color.R, color.G, color.B, color.A }).ToArray();
    //}

    //public static Bitmap Quantize(Bitmap bitmap, int colourCount)
    //{
    //    byte[] paletteData = new byte[colourCount];
    //    byte[] pixels = new byte[bitmap.Width * bitmap.Height];
         
    //    var quantizer = new PaletteQuantizer();
    //    for (int x = 0; x < bitmap.Width; x++)
    //    {
    //        for (int y = 0; y < bitmap.Height; y++)
    //        {
    //            var colour = bitmap.GetPixel(x, y);
    //            quantizer.AddColour(colour);
    //        }
    //    }
    //    quantizer.Quantize(colourCount);
    //    var ret = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
    //    ColorPalette colorPalette = ret.Palette;

    //    // Flush the palette
    //    for(int i = 0; i < colorPalette.Entries.Length; i++) {
    //        colorPalette.Entries[i] = Color.FromArgb(0, 0, 0, 0);
    //    }

    //    int totalIndex = 0;
    //    for (int y = 0; y < bitmap.Height; y++)
    //    {
    //        for (int x = 0; x < bitmap.Width; x++)
    //        {
    //            var colour = quantizer.GetQuantizedColour(bitmap.GetPixel(x, y));
    //            int index = 0;
    //            if(colorPalette.Entries.Contains(colour))
    //            {
    //                index = Array.IndexOf(colorPalette.Entries, colour);
    //                pixels[totalIndex] = (byte)index;
    //            }
    //            else
    //            {
    //                // Find the next color entry that is empty
    //                index = Array.IndexOf(colorPalette.Entries, Color.FromArgb(0,0,0,0));
    //                colorPalette.Entries[index] = colour;
    //                pixels[totalIndex] = (byte)index;
    //            }
    //            totalIndex++;
    //        }
    //    }
    //    //setup images
    //    ret.Palette = colorPalette;
    //    BitmapData data = ret.LockBits(new Rectangle(0, 0, ret.Width, ret.Height),
    //        ImageLockMode.ReadWrite, ret.PixelFormat);

    //    IntPtr scan0 = data.Scan0;
    //    Marshal.Copy(pixels, 0, scan0, pixels.Length);

    //    ret.UnlockBits(data);
    //    return ret;
    //}

    public class PaletteQuantizer
    {
        private readonly Node Root;
        private IDictionary<int, List<Node>> levelNodes;

        public PaletteQuantizer()
        {
            Root = new Node(this);
            levelNodes = new Dictionary<int, List<Node>>();
            for (int i = 0; i < 8; i++)
            {
                levelNodes[i] = new List<Node>();
            }
        }

        public void AddColour(PngColor colour)
        {
            Root.AddColour(colour, 0);
        }

        public void AddLevelNode(Node node, int level)
        {
            levelNodes[level].Add(node);
        }

        public PngColor GetQuantizedColour(PngColor colour)
        {
            return Root.GetColour(colour, 0);
        }

        public void Quantize(int colourCount)
        {
            var nodesToRemove = levelNodes[7].Count - colourCount + 8;
            int level = 6;
            var toBreak = false;
            while (level >= 0 && nodesToRemove > 0)
            {
                var leaves = levelNodes[level]
                    .Where(n => n.ChildrenCount - 1 <= nodesToRemove)
                    .OrderBy(n => n.ChildrenCount);
                foreach (var leaf in leaves)
                {
                    if (leaf.ChildrenCount > nodesToRemove)
                    {
                        toBreak = true;
                        continue;
                    }
                    nodesToRemove -= (leaf.ChildrenCount - 1);
                    leaf.Merge();
                    if (nodesToRemove <= 0)
                    {
                        break;
                    }
                }
                levelNodes.Remove(level + 1);
                level--;
                if (toBreak)
                {
                    break;
                }
            }
        }
    }

    public class Node
    {
        private readonly PaletteQuantizer parent;
        private Node[] Children = new Node[16];
        private PngColor Colour { get; set; }
        private int Count { get; set; }

        public int ChildrenCount => Children.Count(c => c != null);

        public Node(PaletteQuantizer parent)
        {
            this.parent = parent;
        }

        public void AddColour(PngColor colour, int level)
        {
            if (level < 8)
            {
                var index = GetIndex(colour, level);
                if (Children[index] == null)
                {
                    var newNode = new Node(parent);
                    Children[index] = newNode;
                    parent.AddLevelNode(newNode, level);
                }
                Children[index].AddColour(colour, level + 1);
            }
            else
            {
                Colour = colour;
                Count++;
            }
        }

        public PngColor GetColour(PngColor colour, int level)
        {
            if (ChildrenCount == 0)
            {
                return Colour;
            }
            else
            {
                var index = GetIndex(colour, level);
                return Children[index].GetColour(colour, level + 1);
            }
        }

        private byte GetIndex(PngColor colour, int level)
        {
            byte ret = 0;
            var mask = Convert.ToByte(0b10000000 >> level);
            if ((colour.A & mask) != 0)
            {
                ret |= 0b1000;
            }
            if ((colour.R & mask) != 0)
            {
                ret |= 0b0100;
            }
            if ((colour.G & mask) != 0)
            {
                ret |= 0b0010;
            }
            if ((colour.B & mask) != 0)
            {
                ret |= 0b0001;
            }
            return ret;
        }

        public void Merge()
        {
            Colour = Average(Children.Where(c => c != null).Select(c => new Tuple<PngColor, int>(c.Colour, c.Count)));
            Count = Children.Sum(c => c?.Count ?? 0);
            Children = new Node[16];
        }

        private static PngColor Average(IEnumerable<Tuple<PngColor, int>> colours)
        {
            var totals = colours.Sum(c => c.Item2);
            if (totals == 0) return PngColor.Empty;
            return new PngColor(
                a: (byte)(colours.Sum(c => c.Item1.A * c.Item2) / totals),
                r: (byte)(colours.Sum(c => c.Item1.R * c.Item2) / totals),
                g: (byte)(colours.Sum(c => c.Item1.G * c.Item2) / totals),
                b: (byte)(colours.Sum(c => c.Item1.B * c.Item2) / totals));
        }
    }
}