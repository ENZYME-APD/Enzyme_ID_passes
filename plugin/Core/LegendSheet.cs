using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Rhino;
using Rhino.DocObjects;
using Font = System.Drawing.Font;

namespace EnzymeIDPasses.Core
{
  /// <summary>PNG legend: ID colour | material texture or colour (Material pass) | name + hex.</summary>
  public static class LegendSheet
  {
    const int Row = 72, Pad = 16, Sw = 56, Width = 760;

    public static string Write(RhinoDoc doc, IdPass pass, string path)
    {
      bool hasRef = pass.Mode == PassMode.Material;
      int height = Pad * 2 + 48 + Row * Math.Max(1, pass.Legend.Count);

      using (var bmp = new Bitmap(Width, height, PixelFormat.Format32bppArgb))
      using (var g = Graphics.FromImage(bmp))
      using (var titleF = new Font("Arial", 18f, FontStyle.Bold))
      using (var nameF = new Font("Arial", 13f, FontStyle.Bold))
      using (var hexF = new Font("Arial", 11f))
      using (var dark = new SolidBrush(Color.FromArgb(30, 30, 30)))
      using (var grey = new SolidBrush(Color.FromArgb(120, 120, 120)))
      using (var line = new Pen(Color.FromArgb(225, 225, 225)))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.White);
        g.DrawString($"{pass.Mode} ID legend  –  {doc.Name ?? "Untitled"}", titleF, dark, Pad, Pad);

        int y = Pad + 48;
        foreach (var l in pass.Legend)
        {
          int x = Pad;
          using (var b = new SolidBrush(l.Color)) g.FillRectangle(b, x, y + (Row - Sw) / 2, Sw, Sw);
          x += Sw + 12;

          if (hasRef)
          {
            var rect = new Rectangle(x, y + (Row - Sw) / 2, Sw, Sw);
            var tex = TextureBitmap(doc, l.MaterialIndex);
            if (tex != null)
            {
              using (tex)
              {
                int side = Math.Min(tex.Width, tex.Height);
                var src = new Rectangle((tex.Width - side) / 2, (tex.Height - side) / 2, side, side);
                g.DrawImage(tex, rect, src, GraphicsUnit.Pixel);
              }
            }
            else
            {
              using (var b = new SolidBrush(MaterialColor(doc, l.MaterialIndex))) g.FillRectangle(b, rect);
            }
            g.DrawRectangle(line, rect);
            x += Sw + 16;
          }

          g.DrawString(l.Name, nameF, dark, new RectangleF(x, y + 14, Width - x - Pad, 22));
          g.DrawString($"{l.Hex}   RGB {l.Color.R}, {l.Color.G}, {l.Color.B}", hexF, grey, x, y + 38);
          y += Row;
          g.DrawLine(line, Pad, y, Width - Pad, y);
        }
        bmp.Save(path, ImageFormat.Png);
      }
      return path;
    }

    static Bitmap TextureBitmap(RhinoDoc doc, int idx)
    {
      if (idx < 0 || idx >= doc.Materials.Count) return null;
      var mat = doc.Materials[idx];
      foreach (var tt in new[] { TextureType.Bitmap, TextureType.PBR_BaseColor })
      {
        Texture tex = null;
        try { tex = mat.GetTexture(tt); } catch { }
        if (tex == null) continue;
        string p = null;
        try { p = tex.FileReference?.FullPath; } catch { }
        if (string.IsNullOrEmpty(p)) p = tex.FileName;
        if (!string.IsNullOrEmpty(p) && File.Exists(p))
        {
          try { return new Bitmap(p); } catch { }
        }
      }
      return null;
    }

    static Color MaterialColor(RhinoDoc doc, int idx)
    {
      if (idx < 0 || idx >= doc.Materials.Count) return Color.FromArgb(200, 200, 200);
      var mat = doc.Materials[idx];
      try { if (mat.IsPhysicallyBased) return mat.PhysicallyBased.BaseColor.AsSystemColor(); } catch { }
      return mat.DiffuseColor;
    }
  }
}
