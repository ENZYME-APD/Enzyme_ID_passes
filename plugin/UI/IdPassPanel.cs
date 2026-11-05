using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using EnzymeIDPasses.Core;

namespace EnzymeIDPasses.UI
{
  [Guid("6F0B6C3E-6E0E-4C52-9B7D-3A1E2E7A9C41")]
  public class IdPassPanel : Panel, IPanel
  {
    readonly StackLayout _viewList = new StackLayout { Spacing = 2, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly List<CheckBox> _viewChecks = new List<CheckBox>();
    readonly CheckBox _mat = new CheckBox { Text = "Material ID" };
    readonly CheckBox _obj = new CheckBox { Text = "Object ID" };
    readonly CheckBox _lay = new CheckBox { Text = "Layer ID" };
    readonly CheckBox _nrm = new CheckBox { Text = "Normals" };
    readonly CheckBox _vpt = new CheckBox { Text = "Viewport (as displayed)" };
    readonly NumericStepper _w = new NumericStepper { MinValue = 16, MaxValue = 16000, DecimalPlaces = 0 };
    readonly NumericStepper _h = new NumericStepper { MinValue = 16, MaxValue = 16000, DecimalPlaces = 0 };
    readonly TextBox _folder = new TextBox();
    readonly CheckBox _snap = new CheckBox { Text = "Snap edges (remove anti-aliasing)" };
    readonly CheckBox _legend = new CheckBox { Text = "Write legend (PNG sheet + CSV/JSON)" };
    readonly CheckBox _preview = new CheckBox { Text = "Preview in viewports" };
    readonly DropDown _previewMode = new DropDown { DataStore = Enum.GetNames(typeof(PassMode)) };
    readonly GridView _legendGrid;
    readonly ObservableCollection<LegendRow> _legendRows = new ObservableCollection<LegendRow>();
    readonly Label _status = new Label { Text = "Ready", Wrap = WrapMode.Word };
    readonly Button _export = new Button { Text = "Export ID passes" };

    IdOverrideConduit _conduit;
    uint _docSerial;

    public IdPassPanel(uint documentSerialNumber)
    {
      _docSerial = documentSerialNumber;
      var s = EnzymeIDPassesPlugin.Instance.LoadSettings();
      _mat.Checked = s.Modes.Contains(PassMode.Material);
      _obj.Checked = s.Modes.Contains(PassMode.Object);
      _lay.Checked = s.Modes.Contains(PassMode.Layer);
      _nrm.Checked = s.Modes.Contains(PassMode.Normal);
      _vpt.Checked = s.ExportViewport;
      _w.Value = s.Width; _h.Value = s.Height;
      _folder.Text = s.Folder;
      _snap.Checked = s.SnapEdges;
      _legend.Checked = s.WriteLegend;
      _previewMode.SelectedIndex = 0;

      _legendGrid = new GridView { DataStore = _legendRows, Height = 160, ShowHeader = true, GridLines = GridLines.Horizontal };
      var swatchCell = new DrawableCell();
      swatchCell.Paint += (o, e) => { if (e.Item is LegendRow r) e.Graphics.FillRectangle(r.Swatch, e.ClipRectangle); };
      _legendGrid.Columns.Add(new GridColumn { HeaderText = "", Width = 24, DataCell = swatchCell });
      _legendGrid.Columns.Add(new GridColumn { HeaderText = "Name", DataCell = new TextBoxCell(nameof(LegendRow.Name)), Expand = true });
      _legendGrid.Columns.Add(new GridColumn { HeaderText = "Hex", DataCell = new TextBoxCell(nameof(LegendRow.Hex)) });

      var refresh = new Button { Text = "Refresh" };
      var all = new Button { Text = "All" };
      var none = new Button { Text = "None" };
      refresh.Click += (o, e) => RefreshViews();
      all.Click += (o, e) => _viewChecks.ForEach(c => c.Checked = true);
      none.Click += (o, e) => _viewChecks.ForEach(c => c.Checked = false);

      var browse = new Button { Text = "…", Width = 32 };
      browse.Click += (o, e) =>
      {
        var dlg = new SelectFolderDialog { Title = "Output folder", Directory = _folder.Text };
        if (dlg.ShowDialog(this) == DialogResult.Ok) _folder.Text = dlg.Directory;
      };

      var fromView = new Button { Text = "From viewport" };
      fromView.Click += (o, e) =>
      {
        var v = RhinoDoc.ActiveDoc?.Views.ActiveView;
        if (v == null) return;
        var sz = v.ActiveViewport.Size;
        _w.Value = sz.Width; _h.Value = sz.Height;
      };

      _preview.CheckedChanged += (o, e) => UpdatePreview();
      _previewMode.SelectedIndexChanged += (o, e) => { if (_preview.Checked == true) UpdatePreview(); };
      _export.Click += (o, e) => DoExport();

      // Single-column layout: every row fits in MinWidth, so nothing is clipped when the panel
      // is narrow; below MinWidth the panel scrolls horizontally instead of hiding controls.
      _w.Width = _h.Width = -1;
      _previewMode.Width = -1;
      _status.Width = -1;
      foreach (var b in new[] { refresh, all, none }) b.MinimumSize = new Size(60, 0);

      var layout = new DynamicLayout { Padding = new Padding(10), DefaultSpacing = new Size(6, 6) };

      layout.AddRow(Header("Named views"));
      layout.AddSeparateRow(null, refresh, all, none);
      layout.AddRow(new Scrollable { Content = _viewList, Height = 140, Border = BorderType.Line, ExpandContentWidth = true });

      layout.AddRow(Header("Passes"));
      layout.BeginVertical(new Padding(4, 0, 0, 0), new Size(4, 4));
      layout.AddRow(_vpt);
      layout.AddRow(_mat);
      layout.AddRow(_obj);
      layout.AddRow(_lay);
      layout.AddRow(_nrm);
      layout.EndVertical();

      layout.AddRow(Header("Resolution"));
      layout.BeginVertical(spacing: new Size(6, 6));
      layout.AddRow(FieldLabel("Width"), _w, FieldLabel("px", 20));
      layout.AddRow(FieldLabel("Height"), _h, FieldLabel("px", 20));
      layout.EndVertical();
      layout.AddSeparateRow(null, fromView);

      layout.AddRow(Header("Output"));
      layout.AddRow(new TableLayout(new TableRow(new TableCell(_folder, true), browse)) { Spacing = new Size(4, 0) });
      layout.BeginVertical(new Padding(4, 0, 0, 0), new Size(4, 4));
      layout.AddRow(_snap);
      layout.AddRow(_legend);
      layout.EndVertical();

      layout.AddRow(Header("Preview"));
      layout.BeginVertical(new Padding(4, 0, 0, 0), new Size(6, 6));
      layout.AddRow(_preview);
      layout.EndVertical();
      layout.BeginVertical(spacing: new Size(6, 6));
      layout.AddRow(FieldLabel("Pass"), _previewMode);
      layout.EndVertical();
      layout.AddRow(_legendGrid);

      layout.AddRow(new Label { Height = 4 });
      _export.Height = 30;
      layout.AddRow(_export);
      layout.AddRow(_status);
      layout.Add(null); // push everything to the top

      layout.MinimumSize = new Size(MinWidth, 0);
      Content = new Scrollable { Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = true, Content = layout };

      RhinoDoc.EndOpenDocument += OnDocChanged;
      RhinoDoc.NewDocument += OnDocChanged;
      RhinoDoc.CloseDocument += OnDocClosed;
      RefreshViews();
    }

    const int MinWidth = 280;

    static Label Header(string t) => new Label { Text = t, Font = SystemFonts.Bold(), VerticalAlignment = VerticalAlignment.Bottom, Height = 24 };

    static Label FieldLabel(string t, int width = 52) =>
      new Label { Text = t, Width = width, VerticalAlignment = VerticalAlignment.Center };

    RhinoDoc Doc => RhinoDoc.FromRuntimeSerialNumber(_docSerial) ?? RhinoDoc.ActiveDoc;

    void RefreshViews()
    {
      var previously = new HashSet<string>(_viewChecks.Where(c => c.Checked != true).Select(c => c.Text));
      _viewChecks.Clear();
      _viewList.Items.Clear();
      var doc = Doc;
      if (doc != null)
      {
        for (int i = 0; i < doc.NamedViews.Count; i++)
        {
          var name = doc.NamedViews[i].Name;
          var cb = new CheckBox { Text = name, Checked = !previously.Contains(name) };
          _viewChecks.Add(cb);
          _viewList.Items.Add(cb);
        }
      }
      if (_viewChecks.Count == 0) _viewList.Items.Add(new Label { Text = "No named views in this file." });
    }

    ExportSettings Gather()
    {
      var s = new ExportSettings
      {
        NamedViews = _viewChecks.Where(c => c.Checked == true).Select(c => c.Text).ToList(),
        Width = (int)_w.Value,
        Height = (int)_h.Value,
        Folder = _folder.Text,
        SnapEdges = _snap.Checked == true,
        WriteLegend = _legend.Checked == true,
        ExportViewport = _vpt.Checked == true,
        Modes = new List<PassMode>()
      };
      if (_mat.Checked == true) s.Modes.Add(PassMode.Material);
      if (_obj.Checked == true) s.Modes.Add(PassMode.Object);
      if (_lay.Checked == true) s.Modes.Add(PassMode.Layer);
      if (_nrm.Checked == true) s.Modes.Add(PassMode.Normal);
      return s;
    }

    void UpdatePreview()
    {
      var doc = Doc;
      if (_preview.Checked != true || doc == null)
      {
        if (_conduit != null) _conduit.Enabled = false;
        _legendRows.Clear();
        doc?.Views.Redraw();
        return;
      }
      var mode = (PassMode)Math.Max(0, _previewMode.SelectedIndex);
      _status.Text = $"Building {mode} preview…";
      var pass = PassBuilder.Build(doc, mode);
      _conduit = _conduit ?? new IdOverrideConduit();
      _conduit.Pass = pass;
      _conduit.Enabled = true;
      _legendRows.Clear();
      foreach (var l in pass.Legend) _legendRows.Add(new LegendRow(l));
      _status.Text = mode == PassMode.Normal
        ? $"Preview: normals, {pass.Meshes.Count} meshes"
        : $"Preview: {pass.Legend.Count} {mode.ToString().ToLower()}(s), {pass.Meshes.Count} meshes";
      doc.Views.Redraw();
    }

    void DoExport()
    {
      var doc = Doc;
      if (doc == null) return;
      var s = Gather();
      if (s.NamedViews.Count == 0) { _status.Text = "Select at least one named view."; return; }
      if (s.Modes.Count == 0 && !s.ExportViewport) { _status.Text = "Select at least one pass."; return; }
      if (string.IsNullOrWhiteSpace(s.Folder)) { _status.Text = "Choose an output folder."; return; }
      EnzymeIDPassesPlugin.Instance.SaveSettings(s);

      bool wasPreview = _preview.Checked == true;
      if (_conduit != null) _conduit.Enabled = false;   // exporter uses its own override
      _export.Enabled = false;
      try
      {
        var files = Exporter.Run(doc, s, msg => { _status.Text = msg; RhinoApp.SetCommandPrompt(msg); Application.Instance.RunIteration(); });
        _status.Text = $"Done – {files.Count} file(s) written to {s.Folder}";
        RhinoApp.WriteLine("Enzyme ID Passes: " + _status.Text);
      }
      catch (Exception ex)
      {
        _status.Text = "Export failed: " + ex.Message;
        RhinoApp.WriteLine("Enzyme ID Passes: " + ex);
      }
      finally
      {
        _export.Enabled = true;
        if (wasPreview && _conduit != null) { _conduit.Enabled = true; doc.Views.Redraw(); }
      }
    }

    void OnDocChanged(object sender, DocumentEventArgs e)
    {
      _docSerial = e.Document.RuntimeSerialNumber;
      Application.Instance.AsyncInvoke(() => { _preview.Checked = false; RefreshViews(); });
    }

    void OnDocClosed(object sender, DocumentEventArgs e)
    {
      if (_conduit != null) _conduit.Enabled = false;
    }

    // ------------------------------------------------------------------ IPanel
    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
      _docSerial = documentSerialNumber;
      RefreshViews();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
    {
      if (_preview.Checked == true) _preview.Checked = false; // never leave the override on invisibly
    }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
      if (_conduit != null) _conduit.Enabled = false;
      RhinoDoc.EndOpenDocument -= OnDocChanged;
      RhinoDoc.NewDocument -= OnDocChanged;
      RhinoDoc.CloseDocument -= OnDocClosed;
    }

    sealed class LegendRow
    {
      public LegendRow(LegendEntry l)
      {
        Name = l.Name; Hex = l.Hex;
        Swatch = Color.FromArgb(l.Color.R, l.Color.G, l.Color.B);
      }
      public string Name { get; }
      public string Hex { get; }
      public Color Swatch { get; }
    }
  }
}
