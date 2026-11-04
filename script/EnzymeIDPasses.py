#! python 3
# r: numpy
"""
Enzyme ID Passes - export Material / Object / Layer ID passes for saved Named Views.

Rhino 8+ (ScriptEditor, Python 3). Run with _ScriptEditor or _RunPythonScript.

How it works
- Builds a flat-colour mesh for every visible object (blocks are exploded, nested too).
- A temporary display override hides the real objects and draws the ID colours unlit.
  Nothing in the document is modified.
- Each selected Named View is restored, captured, edge-snapped and saved as PNG.
- A legend (CSV + JSON) maps every colour to its material / object / layer.
"""

import os, json, csv, ctypes, colorsys, hashlib
import Rhino
import System
import scriptcontext as sc
import rhinoscriptsyntax as rs
from Rhino.DocObjects import ObjectMaterialSource, ObjectType
from Rhino.Geometry import Mesh, MeshingParameters, BoundingBox, Transform
from System.Drawing import Bitmap, Graphics, Color, Rectangle
from System.Drawing.Imaging import ImageLockMode, PixelFormat, ImageFormat

try:
    import numpy as np
except Exception:
    np = None

BACKGROUND = (0, 0, 0)
MAT_INDEX = {}  # material key -> doc material index (used by the legend sheet)


# ----------------------------------------------------------------------------- keys
def material_key(doc, attrs, parent_attrs=None):
    """Return (key, display_name) for the render material resolved from attributes."""
    src = attrs.MaterialSource
    if src == ObjectMaterialSource.MaterialFromParent and parent_attrs is not None:
        return material_key(doc, parent_attrs, None)
    if src == ObjectMaterialSource.MaterialFromObject:
        idx = attrs.MaterialIndex
    else:  # by layer (or by parent with no parent)
        layer_attrs = parent_attrs if (src == ObjectMaterialSource.MaterialFromParent and parent_attrs) else attrs
        idx = doc.Layers[layer_attrs.LayerIndex].RenderMaterialIndex
    if idx < 0 or idx >= doc.Materials.Count:
        return ("__default__", "Default material")
    mat = doc.Materials[idx]
    rid = mat.RenderMaterialInstanceId
    name = mat.Name or "Unnamed material"
    rm = mat.RenderMaterial
    if rm is not None and rm.Name:
        name = rm.Name
    key = str(rid) if rid != System.Guid.Empty else "idx:%d:%s" % (idx, name)
    MAT_INDEX[key] = idx
    return (key, name)


def object_key(doc, obj, attrs, top_obj):
    o = top_obj or obj
    nm = o.Attributes.Name or ("%s %s" % (o.ObjectType, str(o.Id)[:8]))
    return (str(o.Id), nm)


def layer_key(doc, attrs, parent_attrs=None):
    a = attrs
    if parent_attrs is not None and doc.Layers[attrs.LayerIndex].Name == "Default":
        a = parent_attrs
    layer = doc.Layers[a.LayerIndex]
    return (str(layer.Id), layer.FullPath)


# ----------------------------------------------------------------------------- palette
def build_palette(entries):
    """entries: dict key->name. Deterministic, well separated colours sorted by name."""
    items = sorted(entries.items(), key=lambda kv: (kv[1].lower(), kv[0]))
    golden = 0.61803398875
    # start hue from file-independent hash so colours stay stable between runs
    out = {}
    used = []
    for i, (k, name) in enumerate(items):
        h0 = int(hashlib.md5(name.encode("utf-8")).hexdigest()[:6], 16) / float(0xFFFFFF)
        for attempt in range(64):
            h = (h0 + (i + attempt) * golden) % 1.0
            s = (0.95, 0.75, 0.55)[(i + attempt) % 3]
            v = (0.95, 0.75, 0.55)[((i + attempt) // 3) % 3]
            r, g, b = [int(round(c * 255)) for c in colorsys.hsv_to_rgb(h, s, v)]
            if all(abs(r - u[0]) + abs(g - u[1]) + abs(b - u[2]) > 60 for u in used) and (r + g + b) > 60:
                break
        used.append((r, g, b))
        out[k] = (r, g, b)
    return out


# ----------------------------------------------------------------------------- geometry
_mp = MeshingParameters.Default


def object_meshes(obj):
    geo = obj.Geometry
    if isinstance(geo, Mesh):
        return [geo]
    meshes = list(obj.GetMeshes(Rhino.Geometry.MeshType.Render) or [])
    if not meshes:
        try:
            obj.CreateMeshes(Rhino.Geometry.MeshType.Render, _mp, False)
            meshes = list(obj.GetMeshes(Rhino.Geometry.MeshType.Render) or [])
        except Exception:
            pass
    if not meshes:
        brep = None
        if isinstance(geo, Rhino.Geometry.Brep):
            brep = geo
        elif isinstance(geo, Rhino.Geometry.Extrusion):
            brep = geo.ToBrep()
        elif isinstance(geo, Rhino.Geometry.SubD):
            m = Mesh.CreateFromSubD(geo, 2)
            return [m] if m else []
        if brep:
            meshes = list(Mesh.CreateFromBrep(brep, _mp) or [])
    return meshes


def collect(doc):
    """Return list of (mesh_in_world, obj, attrs, parent_attrs, top_obj)."""
    settings = Rhino.DocObjects.ObjectEnumeratorSettings()
    settings.VisibleFilter = True
    settings.HiddenObjects = False
    settings.LockedObjects = True
    settings.ObjectTypeFilter = (ObjectType.Brep | ObjectType.Extrusion | ObjectType.Mesh |
                                 ObjectType.SubD | ObjectType.Surface | ObjectType.InstanceReference)
    items = []
    for obj in doc.Objects.GetObjectList(settings):
        if obj.ObjectType == ObjectType.InstanceReference:
            res = tuple(obj.Explode(True))
            if len(res) == 4:  # pythonnet may prepend the (void) return value
                res = res[1:]
            pieces, pattrs, pxf = res
            for p, a, xf in zip(pieces, pattrs, pxf):
                if p.ObjectType == ObjectType.InstanceReference:
                    continue
                for m in object_meshes(p):
                    mm = m.DuplicateMesh()
                    mm.Transform(xf)
                    items.append((mm, p, a, obj.Attributes, obj))
        else:
            for m in object_meshes(obj):
                items.append((m.DuplicateMesh(), obj, obj.Attributes, None, None))
    return items


def build_normal_pass(items):
    """World-space normals: RGB = normal * 0.5 + 0.5 (red +X, green +Y, blue +Z / up)."""
    draw, bbox = [], BoundingBox.Empty
    for mesh, obj, attrs, parent, top in items:
        m = mesh.DuplicateMesh()
        if m.Normals.Count != m.Vertices.Count:
            m.Normals.ComputeNormals()
        m.VertexColors.Clear()
        for i in range(m.Vertices.Count):
            n = m.Normals[i]
            m.VertexColors.Add(Color.FromArgb(255,
                int(max(0, min(255, (n.X * 0.5 + 0.5) * 255))),
                int(max(0, min(255, (n.Y * 0.5 + 0.5) * 255))),
                int(max(0, min(255, (n.Z * 0.5 + 0.5) * 255)))))
        draw.append(m)
        bbox.Union(m.GetBoundingBox(False))
    return draw, bbox, []


def build_pass(doc, items, mode):
    if mode == "Normal":
        return build_normal_pass(items)
    entries, keyed = {}, []
    for mesh, obj, attrs, parent, top in items:
        if mode == "Material":
            k, n = material_key(doc, attrs, parent)
        elif mode == "Object":
            k, n = object_key(doc, obj, attrs, top)
        else:
            k, n = layer_key(doc, attrs, parent)
        entries[k] = n
        keyed.append((mesh, k))
    palette = build_palette(entries)
    draw, bbox = [], BoundingBox.Empty
    for mesh, k in keyed:
        r, g, b = palette[k]
        m = mesh.DuplicateMesh()
        m.VertexColors.CreateMonotoneMesh(Color.FromArgb(255, r, g, b))
        draw.append(m)
        bbox.Union(m.GetBoundingBox(False))
    legend = [{"key": k, "name": entries[k], "rgb": palette[k],
               "hex": "#%02X%02X%02X" % palette[k]} for k in palette]
    legend.sort(key=lambda d: d["name"].lower())
    return draw, bbox, legend


# ----------------------------------------------------------------------------- override
class Override(object):
    """Temporary display override using static pipeline events (no doc changes)."""

    def __init__(self, meshes, bbox):
        self.meshes, self.bbox, self.on = meshes, bbox, False

    def _cull(self, sender, e):
        e.CullObject = True

    def _bbox(self, sender, e):
        e.IncludeBoundingBox(self.bbox)

    def _draw(self, sender, e):
        for m in self.meshes:
            e.Display.DrawMeshFalseColors(m)

    def enable(self):
        if self.on: return
        Rhino.Display.DisplayPipeline.ObjectCulling += self._cull
        Rhino.Display.DisplayPipeline.CalculateBoundingBox += self._bbox
        Rhino.Display.DisplayPipeline.PostDrawObjects += self._draw
        self.on = True

    def disable(self):
        if not self.on: return
        Rhino.Display.DisplayPipeline.ObjectCulling -= self._cull
        Rhino.Display.DisplayPipeline.CalculateBoundingBox -= self._bbox
        Rhino.Display.DisplayPipeline.PostDrawObjects -= self._draw
        self.on = False


# ----------------------------------------------------------------------------- capture
def capture(view, width, height, transparent=True):
    vc = Rhino.Display.ViewCapture()
    vc.Width, vc.Height = width, height
    vc.ScaleScreenItems = False
    vc.DrawAxes = False
    vc.DrawGrid = False
    vc.DrawGridAxes = False
    vc.TransparentBackground = transparent
    return vc.CaptureToBitmap(view)


def snap_and_flatten(bmp, palette_rgb):
    """Remove anti-aliasing: every pixel becomes the nearest ID colour or background."""
    w, h = bmp.Width, bmp.Height
    out = Bitmap(w, h, PixelFormat.Format32bppArgb)
    g = Graphics.FromImage(out)
    g.Clear(Color.FromArgb(255, *BACKGROUND))
    g.DrawImage(bmp, 0, 0, w, h)
    g.Dispose()
    if np is None or palette_rgb is None:
        return out
    rect = Rectangle(0, 0, w, h)
    bd = out.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
    try:
        size = bd.Stride * h
        ptr = bd.Scan0.ToInt64()
        buf = np.frombuffer(ctypes.string_at(ptr, size), dtype=np.uint8).copy()
        px = buf.reshape(-1, 4)  # BGRA
        pal = np.array([BACKGROUND] + list(palette_rgb), dtype=np.int32)[:, ::-1]  # -> BGR
        chunk = 200000
        for s in range(0, px.shape[0], chunk):
            blk = px[s:s + chunk, :3].astype(np.int32)
            d = ((blk[:, None, :] - pal[None, :, :]) ** 2).sum(axis=2)
            px[s:s + chunk, :3] = pal[d.argmin(axis=1)].astype(np.uint8)
        px[:, 3] = 255
        ctypes.memmove(ptr, px.tobytes(), size)
    finally:
        out.UnlockBits(bd)
    return out


def material_reference(doc, key):
    """Bitmap of the material's diffuse / base-colour texture, or its plain colour."""
    idx = MAT_INDEX.get(key, -1)
    if idx < 0 or idx >= doc.Materials.Count:
        return Color.FromArgb(255, 200, 200, 200)
    mat = doc.Materials[idx]
    tex_types = [Rhino.DocObjects.TextureType.Bitmap]
    try:
        tex_types.append(Rhino.DocObjects.TextureType.PBR_BaseColor)
    except Exception:
        pass
    for tt in tex_types:
        try:
            tex = mat.GetTexture(tt)
        except Exception:
            tex = None
        if tex is None:
            continue
        path = None
        try:
            if tex.FileReference is not None:
                path = tex.FileReference.FullPath
        except Exception:
            pass
        path = path or tex.FileName
        if path and os.path.exists(path):
            try:
                return Bitmap(path)
            except Exception:
                pass
    try:
        if mat.IsPhysicallyBased:
            return mat.PhysicallyBased.BaseColor.AsSystemColor()
    except Exception:
        pass
    return mat.DiffuseColor


def write_legend_sheet(doc, legend, mode, path):
    """PNG legend: ID colour | texture or colour (materials) | name + hex."""
    from System.Drawing import Font, FontStyle, SolidBrush, Pen, StringFormat, RectangleF
    from System.Drawing.Drawing2D import InterpolationMode, SmoothingMode
    row, pad, sw = 72, 16, 56
    has_ref = (mode == "Material")
    width = 760
    height = pad * 2 + 48 + row * max(1, len(legend))
    bmp = Bitmap(width, height, PixelFormat.Format32bppArgb)
    g = Graphics.FromImage(bmp)
    g.SmoothingMode = SmoothingMode.AntiAlias
    g.InterpolationMode = InterpolationMode.HighQualityBicubic
    g.Clear(Color.White)
    title_f = Font("Arial", 18.0, FontStyle.Bold)
    name_f = Font("Arial", 13.0, FontStyle.Bold)
    hex_f = Font("Arial", 11.0)
    dark = SolidBrush(Color.FromArgb(255, 30, 30, 30))
    grey = SolidBrush(Color.FromArgb(255, 120, 120, 120))
    line = Pen(Color.FromArgb(255, 225, 225, 225))
    g.DrawString("%s ID legend  -  %s" % (mode, doc.Name or "Untitled"), title_f, dark, float(pad), float(pad))
    y = pad + 48
    for d in legend:
        r, gg, b = d["rgb"]
        x = pad
        g.FillRectangle(SolidBrush(Color.FromArgb(255, r, gg, b)), x, y + (row - sw) // 2, sw, sw)
        x += sw + 12
        if has_ref:
            ref = material_reference(doc, d["key"])
            rect = Rectangle(x, y + (row - sw) // 2, sw, sw)
            if isinstance(ref, Bitmap):
                side = min(ref.Width, ref.Height)  # centre square crop
                src = Rectangle((ref.Width - side) // 2, (ref.Height - side) // 2, side, side)
                g.DrawImage(ref, rect, src, System.Drawing.GraphicsUnit.Pixel)
                ref.Dispose()
            else:
                g.FillRectangle(SolidBrush(ref), rect)
            g.DrawRectangle(line, rect)
            x += sw + 16
        g.DrawString(d["name"], name_f, dark, RectangleF(float(x), float(y + 14), float(width - x - pad), 22.0))
        g.DrawString("%s   RGB %d, %d, %d" % (d["hex"], r, gg, b), hex_f, grey, float(x), float(y + 38))
        y += row
        g.DrawLine(line, pad, y, width - pad, y)
    g.Dispose()
    bmp.Save(path, ImageFormat.Png)
    bmp.Dispose()


def safe(name):
    return "".join(c if c.isalnum() or c in "-_ " else "_" for c in name).strip() or "view"


# ----------------------------------------------------------------------------- main
def run():
    doc = sc.doc
    nv = doc.NamedViews
    names = [nv[i].Name for i in range(nv.Count)]
    if not names:
        print("No Named Views in this file.")
        return
    picked = rs.CheckListBox([(n, True) for n in names], "Named views to export", "Enzyme ID Passes")
    if not picked:
        return
    views = [n for n, on in picked if on]
    passes = rs.CheckListBox([("Viewport (as displayed)", True), ("Material", True),
                              ("Object", False), ("Layer", False), ("Normal", False)],
                             "Passes", "Enzyme ID Passes")
    if not passes:
        return
    export_viewport = any(on for m, on in passes if m.startswith("Viewport"))
    modes = [m for m, on in passes if on and not m.startswith("Viewport")]
    w = rs.GetInteger("Width (px)", 3000, 16, 16000)
    if not w: return
    h = rs.GetInteger("Height (px)", 2000, 16, 16000)
    if not h: return
    folder = rs.BrowseForFolder(message="Output folder")
    if not folder: return

    view = doc.Views.ActiveView
    vp = view.ActiveViewport
    saved_vp = Rhino.DocObjects.ViewportInfo(vp)
    saved_mode = vp.DisplayMode
    shaded = Rhino.Display.DisplayModeDescription.FindByName("Shaded")

    rs.Prompt("Meshing scene...")
    items = collect(doc)
    print("Enzyme ID Passes: %d meshes collected" % len(items))
    if np is None:
        print("numpy not available - edge snapping skipped (turn off anti-aliasing in Options > View > OpenGL).")

    ov = None
    try:
        # Normal capture of each view, in the viewport's current display mode (e.g. Rendered)
        if export_viewport:
            for name in views:
                nv.Restore(nv.FindByName(name), vp)
                view.Redraw()
                rs.Prompt("Capturing viewport - %s..." % name)
                img = capture(view, w, h, transparent=False)
                if img is None:
                    print("  capture failed: %s" % name); continue
                path = os.path.join(folder, "%s_Viewport.png" % safe(name))
                img.Save(path, ImageFormat.Png)
                img.Dispose()
                print("  saved %s" % path)

        if shaded: vp.DisplayMode = shaded
        for mode in modes:
            draw, bbox, legend = build_pass(doc, items, mode)
            ov = Override(draw, bbox)
            ov.enable()
            pal = [tuple(d["rgb"]) for d in legend]
            for name in views:
                idx = nv.FindByName(name)
                nv.Restore(idx, vp)
                view.Redraw()
                rs.Prompt("Capturing %s - %s..." % (mode, name))
                raw = capture(view, w, h)
                if raw is None:
                    print("  capture failed: %s" % name); continue
                img = snap_and_flatten(raw, pal if mode != "Normal" else None)
                suffix = "Normal" if mode == "Normal" else "%sID" % mode
                path = os.path.join(folder, "%s_%s.png" % (safe(name), suffix))
                img.Save(path, ImageFormat.Png)
                raw.Dispose(); img.Dispose()
                print("  saved %s" % path)
            ov.disable(); ov = None
            if mode == "Normal":
                continue
            base = os.path.join(folder, "Legend_%sID" % mode)
            try:
                write_legend_sheet(doc, legend, mode, base + ".png")
                print("  saved %s.png" % base)
            except Exception as ex:
                print("  legend sheet failed: %s" % ex)
            with open(base + ".json", "w") as f:
                json.dump(legend, f, indent=2)
            with open(base + ".csv", "w", newline="") as f:
                wr = csv.writer(f)
                wr.writerow(["name", "hex", "r", "g", "b"])
                for d in legend:
                    wr.writerow([d["name"], d["hex"]] + list(d["rgb"]))
    finally:
        if ov: ov.disable()
        vp.SetViewProjection(saved_vp, True)
        vp.DisplayMode = saved_mode
        view.Redraw()
    print("Enzyme ID Passes: done -> %s" % folder)


if __name__ == "__main__":
    run()
