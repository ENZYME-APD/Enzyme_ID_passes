using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EnzymeIDPasses.Core
{
  public enum PassMode { Material, Object, Layer, Normal }

  /// <summary>One mesh in world space plus the attributes needed to key it.</summary>
  internal sealed class SceneItem
  {
    public Mesh Mesh;
    public RhinoObject Obj;
    public ObjectAttributes Attrs;
    public ObjectAttributes ParentAttrs; // block instance attributes (null if not in a block)
    public RhinoObject TopObj;           // top-level block instance (null if not in a block)
  }

  public sealed class LegendEntry
  {
    public string Key;
    public string Name;
    public int MaterialIndex = -1;
    public Color Color;
    public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
  }

  public sealed class IdPass
  {
    public PassMode Mode;
    public List<Mesh> Meshes = new List<Mesh>();
    public BoundingBox Bounds = BoundingBox.Empty;
    public List<LegendEntry> Legend = new List<LegendEntry>();
  }

  // ===========================================================================================
  internal static class SceneCollector
  {
    static readonly MeshingParameters Mp = MeshingParameters.Default;

    public static List<SceneItem> Collect(RhinoDoc doc)
    {
      var settings = new ObjectEnumeratorSettings
      {
        VisibleFilter = true,
        HiddenObjects = false,
        LockedObjects = true,
        ObjectTypeFilter = ObjectType.Brep | ObjectType.Extrusion | ObjectType.Mesh |
                           ObjectType.SubD | ObjectType.Surface | ObjectType.InstanceReference
      };
      var items = new List<SceneItem>();
      foreach (var obj in doc.Objects.GetObjectList(settings))
      {
        if (obj is InstanceObject inst)
        {
          inst.Explode(true, out RhinoObject[] pieces, out ObjectAttributes[] pAttrs, out Transform[] xforms);
          for (int i = 0; i < pieces.Length; i++)
          {
            if (pieces[i] is InstanceObject) continue;
            foreach (var m in MeshesOf(pieces[i]))
            {
              var mm = m.DuplicateMesh();
              mm.Transform(xforms[i]);
              items.Add(new SceneItem { Mesh = mm, Obj = pieces[i], Attrs = pAttrs[i], ParentAttrs = inst.Attributes, TopObj = inst });
            }
          }
        }
        else
        {
          foreach (var m in MeshesOf(obj))
            items.Add(new SceneItem { Mesh = m.DuplicateMesh(), Obj = obj, Attrs = obj.Attributes });
        }
      }
      return items;
    }

    static IEnumerable<Mesh> MeshesOf(RhinoObject obj)
    {
      var geo = obj.Geometry;
      if (geo is Mesh mesh) return new[] { mesh };

      var meshes = obj.GetMeshes(MeshType.Render);
      if (meshes == null || meshes.Length == 0)
      {
        try { obj.CreateMeshes(MeshType.Render, Mp, false); meshes = obj.GetMeshes(MeshType.Render); }
        catch { meshes = null; }
      }
      if (meshes != null && meshes.Length > 0) return meshes.Where(m => m != null);

      switch (geo)
      {
        case Brep b: return Mesh.CreateFromBrep(b, Mp) ?? Array.Empty<Mesh>();
        case Extrusion e: return Mesh.CreateFromBrep(e.ToBrep(), Mp) ?? Array.Empty<Mesh>();
        case SubD s: var sm = Mesh.CreateFromSubD(s, 2); return sm != null ? new[] { sm } : Array.Empty<Mesh>();
        case Surface srf: return Mesh.CreateFromBrep(srf.ToBrep(), Mp) ?? Array.Empty<Mesh>();
      }
      return Array.Empty<Mesh>();
    }
  }

  // ===========================================================================================
  internal static class Keys
  {
    public static int MaterialIndexOf(RhinoDoc doc, ObjectAttributes attrs, ObjectAttributes parent)
    {
      if (attrs.MaterialSource == ObjectMaterialSource.MaterialFromParent && parent != null)
        return MaterialIndexOf(doc, parent, null);
      return attrs.MaterialSource == ObjectMaterialSource.MaterialFromObject
        ? attrs.MaterialIndex
        : doc.Layers[attrs.LayerIndex].RenderMaterialIndex;
    }

    public static (string key, string name) For(RhinoDoc doc, SceneItem it, PassMode mode)
    {
      switch (mode)
      {
        case PassMode.Material: return Material(doc, it.Attrs, it.ParentAttrs);
        case PassMode.Object:
          var o = it.TopObj ?? it.Obj;
          var n = string.IsNullOrWhiteSpace(o.Attributes.Name) ? $"{o.ObjectType} {o.Id.ToString().Substring(0, 8)}" : o.Attributes.Name;
          return (o.Id.ToString(), n);
        default:
          var a = it.Attrs;
          if (it.ParentAttrs != null && doc.Layers[a.LayerIndex].Name == "Default") a = it.ParentAttrs;
          var layer = doc.Layers[a.LayerIndex];
          return (layer.Id.ToString(), layer.FullPath);
      }
    }

    static (string, string) Material(RhinoDoc doc, ObjectAttributes attrs, ObjectAttributes parent)
    {
      int idx = MaterialIndexOf(doc, attrs, parent);

      if (idx < 0 || idx >= doc.Materials.Count) return ("__default__", "Default material");
      var mat = doc.Materials[idx];
      var name = mat.RenderMaterial?.Name;
      if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(mat.Name) ? "Unnamed material" : mat.Name;
      var key = mat.RenderMaterialInstanceId != Guid.Empty ? mat.RenderMaterialInstanceId.ToString() : $"idx:{idx}:{name}";
      return (key, name);
    }
  }

  // ===========================================================================================
  internal static class Palette
  {
    /// <summary>Deterministic, well separated colours. Same name => same starting hue in every file.</summary>
    public static Dictionary<string, Color> Build(Dictionary<string, string> entries)
    {
      const double golden = 0.61803398875;
      var levels = new[] { 0.95, 0.75, 0.55 };
      var used = new List<Color>();
      var result = new Dictionary<string, Color>();
      int i = 0;
      foreach (var kv in entries.OrderBy(k => k.Value, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Key))
      {
        double h0 = StableHash(kv.Value);
        Color c = Color.White;
        for (int attempt = 0; attempt < 64; attempt++)
        {
          int s = i + attempt;
          c = Hsv((h0 + s * golden) % 1.0, levels[s % 3], levels[(s / 3) % 3]);
          if (c.R + c.G + c.B > 60 && used.All(u => Math.Abs(u.R - c.R) + Math.Abs(u.G - c.G) + Math.Abs(u.B - c.B) > 60)) break;
        }
        used.Add(c);
        result[kv.Key] = c;
        i++;
      }
      return result;
    }

    static double StableHash(string s)
    {
      unchecked
      {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
      }
    }

    static Color Hsv(double h, double s, double v)
    {
      int hi = (int)Math.Floor(h * 6) % 6;
      double f = h * 6 - Math.Floor(h * 6);
      double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
      double r, g, b;
      switch (hi)
      {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        default: r = v; g = p; b = q; break;
      }
      return Color.FromArgb(255, (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
  }

  // ===========================================================================================
  public static class PassBuilder
  {
    public static IdPass Build(RhinoDoc doc, PassMode mode) => Build(doc, SceneCollector.Collect(doc), mode);

    internal static IdPass Build(RhinoDoc doc, List<SceneItem> items, PassMode mode)
    {
      if (mode == PassMode.Normal) return BuildNormals(items);
      var names = new Dictionary<string, string>();
      var matIdx = new Dictionary<string, int>();
      var keyed = new List<(Mesh, string)>(items.Count);
      foreach (var it in items)
      {
        var (k, n) = Keys.For(doc, it, mode);
        names[k] = n;
        if (mode == PassMode.Material) matIdx[k] = Keys.MaterialIndexOf(doc, it.Attrs, it.ParentAttrs);
        keyed.Add((it.Mesh, k));
      }
      var pal = Palette.Build(names);
      var pass = new IdPass { Mode = mode };
      foreach (var (mesh, k) in keyed)
      {
        var m = mesh.DuplicateMesh();
        m.VertexColors.CreateMonotoneMesh(pal[k]);
        pass.Meshes.Add(m);
        pass.Bounds.Union(m.GetBoundingBox(false));
      }
      pass.Legend = pal.Select(kv => new LegendEntry
                        {
                          Key = kv.Key, Name = names[kv.Key], Color = kv.Value,
                          MaterialIndex = matIdx.TryGetValue(kv.Key, out var mi) ? mi : -1
                        })
                       .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
      return pass;
    }

    /// <summary>World-space normals: RGB = n * 0.5 + 0.5 (red +X, green +Y, blue +Z/up).</summary>
    static IdPass BuildNormals(List<SceneItem> items)
    {
      var pass = new IdPass { Mode = PassMode.Normal };
      foreach (var it in items)
      {
        var m = it.Mesh.DuplicateMesh();
        if (m.Normals.Count != m.Vertices.Count) m.Normals.ComputeNormals();
        m.VertexColors.Clear();
        for (int i = 0; i < m.Vertices.Count; i++)
        {
          var n = m.Normals[i];
          m.VertexColors.Add(Color.FromArgb(255, C(n.X), C(n.Y), C(n.Z)));
        }
        pass.Meshes.Add(m);
        pass.Bounds.Union(m.GetBoundingBox(false));
      }
      return pass;
    }

    static int C(float v) => Math.Max(0, Math.Min(255, (int)Math.Round((v * 0.5 + 0.5) * 255)));
  }

  // ===========================================================================================
  /// <summary>Hides real objects and draws flat, unlit ID colours. The document is never modified.</summary>
  public sealed class IdOverrideConduit : DisplayConduit
  {
    public IdPass Pass { get; set; }

    protected override void ObjectCulling(CullObjectEventArgs e)
    {
      if (Pass != null) e.CullObject = true;
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      if (Pass != null && Pass.Bounds.IsValid) e.IncludeBoundingBox(Pass.Bounds);
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
      if (Pass == null) return;
      foreach (var m in Pass.Meshes) e.Display.DrawMeshFalseColors(m);
    }
  }

  // ===========================================================================================
  public sealed class ExportSettings
  {
    public List<string> NamedViews = new List<string>();
    public List<PassMode> Modes = new List<PassMode> { PassMode.Material };
    public int Width = 3000;
    public int Height = 2000;
    public string Folder = "";
    public bool SnapEdges = true;
    public bool WriteLegend = true;
    public bool ExportViewport = true;
    public Color Background = Color.Black;
  }

  public static class Exporter
  {
    public static List<string> Run(RhinoDoc doc, ExportSettings s, Action<string> progress = null)
    {
      var written = new List<string>();
      if (string.IsNullOrWhiteSpace(s.Folder)) throw new InvalidOperationException("No output folder set.");
      Directory.CreateDirectory(s.Folder);

      var view = doc.Views.ActiveView ?? throw new InvalidOperationException("No active view.");
      var vp = view.ActiveViewport;
      var savedVp = new ViewportInfo(vp);
      var savedMode = vp.DisplayMode;
      var shaded = DisplayModeDescription.FindByName("Shaded");

      progress?.Invoke("Meshing scene…");
      var items = SceneCollector.Collect(doc);
      var conduit = new IdOverrideConduit();
      try
      {
        if (s.ExportViewport)
        {
          foreach (var name in s.NamedViews)
          {
            int idx = doc.NamedViews.FindByName(name);
            if (idx < 0) continue;
            progress?.Invoke($"Viewport – {name}");
            doc.NamedViews.Restore(idx, vp);
            view.Redraw();
            using (var img = Capture(view, s.Width, s.Height, false))
            {
              if (img == null) { progress?.Invoke($"Capture failed: {name}"); continue; }
              var path = Path.Combine(s.Folder, $"{Safe(name)}_Viewport.png");
              img.Save(path, ImageFormat.Png);
              written.Add(path);
            }
          }
        }

        if (shaded != null) vp.DisplayMode = shaded;
        foreach (var mode in s.Modes)
        {
          var pass = PassBuilder.Build(doc, items, mode);
          conduit.Pass = pass;
          conduit.Enabled = true;
          var palette = pass.Legend.Select(l => l.Color).ToArray();

          foreach (var name in s.NamedViews)
          {
            int idx = doc.NamedViews.FindByName(name);
            if (idx < 0) continue;
            progress?.Invoke($"{mode} ID – {name}");
            doc.NamedViews.Restore(idx, vp);
            view.Redraw();

            using (var raw = Capture(view, s.Width, s.Height))
            {
              if (raw == null) { progress?.Invoke($"Capture failed: {name}"); continue; }
              bool snap = s.SnapEdges && mode != PassMode.Normal;
              using (var img = Flatten(raw, s.Background, snap ? palette : null))
              {
                var suffix = mode == PassMode.Normal ? "Normal" : $"{mode}ID";
                var path = Path.Combine(s.Folder, $"{Safe(name)}_{suffix}.png");
                img.Save(path, ImageFormat.Png);
                written.Add(path);
              }
            }
          }
          conduit.Enabled = false;
          if (s.WriteLegend && mode != PassMode.Normal)
          {
            written.AddRange(WriteLegend(s.Folder, pass));
            try { written.Add(LegendSheet.Write(doc, pass, Path.Combine(s.Folder, $"Legend_{mode}ID.png"))); }
            catch (Exception ex) { progress?.Invoke("Legend sheet failed: " + ex.Message); }
          }
        }
      }
      finally
      {
        conduit.Enabled = false;
        vp.SetViewProjection(savedVp, true);
        vp.DisplayMode = savedMode;
        view.Redraw();
      }
      return written;
    }

    static Bitmap Capture(RhinoView view, int w, int h, bool transparent = true)
    {
      var vc = new ViewCapture
      {
        Width = w,
        Height = h,
        ScaleScreenItems = false,
        DrawAxes = false,
        DrawGrid = false,
        DrawGridAxes = false,
        TransparentBackground = transparent
      };
      return vc.CaptureToBitmap(view);
    }

    /// <summary>Composite on background; optionally snap every pixel to the nearest ID colour (kills anti-aliasing).</summary>
    static Bitmap Flatten(Bitmap src, Color bg, Color[] palette)
    {
      int w = src.Width, h = src.Height;
      var outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(outBmp))
      {
        g.Clear(Color.FromArgb(255, bg));
        g.DrawImage(src, 0, 0, w, h);
      }
      if (palette == null) return outBmp;

      var pal = new[] { bg }.Concat(palette).ToArray();
      var data = outBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
      try
      {
        int bytes = data.Stride * h;
        var buf = new byte[bytes];
        Marshal.Copy(data.Scan0, buf, 0, bytes);
        var cache = new Dictionary<int, int>();
        for (int i = 0; i < bytes; i += 4)
        {
          int b = buf[i], gg = buf[i + 1], r = buf[i + 2];
          int key = (r << 16) | (gg << 8) | b;
          if (!cache.TryGetValue(key, out int best))
          {
            int bestD = int.MaxValue; best = 0;
            for (int p = 0; p < pal.Length; p++)
            {
              int dr = r - pal[p].R, dg = gg - pal[p].G, db = b - pal[p].B;
              int d = dr * dr + dg * dg + db * db;
              if (d < bestD) { bestD = d; best = p; }
            }
            cache[key] = best;
          }
          buf[i] = pal[best].B; buf[i + 1] = pal[best].G; buf[i + 2] = pal[best].R; buf[i + 3] = 255;
        }
        Marshal.Copy(buf, 0, data.Scan0, bytes);
      }
      finally { outBmp.UnlockBits(data); }
      return outBmp;
    }

    static IEnumerable<string> WriteLegend(string folder, IdPass pass)
    {
      var csvPath = Path.Combine(folder, $"Legend_{pass.Mode}ID.csv");
      var sb = new StringBuilder("name,hex,r,g,b\n");
      foreach (var l in pass.Legend)
        sb.AppendLine($"\"{l.Name.Replace("\"", "\"\"")}\",{l.Hex},{l.Color.R},{l.Color.G},{l.Color.B}");
      File.WriteAllText(csvPath, sb.ToString());

      var jsonPath = Path.Combine(folder, $"Legend_{pass.Mode}ID.json");
      var js = new StringBuilder("[\n");
      for (int i = 0; i < pass.Legend.Count; i++)
      {
        var l = pass.Legend[i];
        js.Append($"  {{\"name\": \"{Esc(l.Name)}\", \"key\": \"{Esc(l.Key)}\", \"hex\": \"{l.Hex}\", \"rgb\": [{l.Color.R}, {l.Color.G}, {l.Color.B}]}}");
        js.Append(i < pass.Legend.Count - 1 ? ",\n" : "\n");
      }
      js.Append("]\n");
      File.WriteAllText(jsonPath, js.ToString());
      return new[] { csvPath, jsonPath };
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string Safe(string name)
    {
      var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_').ToArray();
      var s = new string(chars).Trim();
      return s.Length == 0 ? "view" : s;
    }
  }
}
