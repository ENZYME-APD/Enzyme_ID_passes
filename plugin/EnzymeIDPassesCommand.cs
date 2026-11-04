using System;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using EnzymeIDPasses.Core;
using EnzymeIDPasses.UI;

namespace EnzymeIDPasses
{
  /// <summary>Opens the Enzyme ID Passes panel.</summary>
  public class EnzymeIDPassesCommand : Command
  {
    public EnzymeIDPassesCommand() { Instance = this; }
    public static EnzymeIDPassesCommand Instance { get; private set; }

    public override string EnglishName => "EnzymeIDPasses";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      Rhino.UI.Panels.OpenPanel(typeof(IdPassPanel).GUID);
      return Result.Success;
    }
  }

  /// <summary>
  /// Scriptable export using the panel's saved settings, for all Named Views.
  /// Usage: -EnzymeIDPassesExport [Material=Yes Object=No Layer=No] Enter
  /// </summary>
  public class EnzymeIDPassesExportCommand : Command
  {
    public override string EnglishName => "EnzymeIDPassesExport";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var plug = EnzymeIDPassesPlugin.Instance;
      var s = plug.LoadSettings();

      var mat = new OptionToggle(s.Modes.Contains(PassMode.Material), "No", "Yes");
      var obj = new OptionToggle(s.Modes.Contains(PassMode.Object), "No", "Yes");
      var lay = new OptionToggle(s.Modes.Contains(PassMode.Layer), "No", "Yes");
      var nrm = new OptionToggle(s.Modes.Contains(PassMode.Normal), "No", "Yes");
      var vpt = new OptionToggle(s.ExportViewport, "No", "Yes");
      var w = new OptionInteger(s.Width, 16, 16000);
      var h = new OptionInteger(s.Height, 16, 16000);

      var go = new GetOption();
      go.SetCommandPrompt($"Export ID passes for {doc.NamedViews.Count} named view(s) to {s.Folder}");
      go.AcceptNothing(true);
      go.AddOptionToggle("Viewport", ref vpt);
      go.AddOptionToggle("Material", ref mat);
      go.AddOptionToggle("Object", ref obj);
      go.AddOptionToggle("Layer", ref lay);
      go.AddOptionToggle("Normals", ref nrm);
      go.AddOptionInteger("Width", ref w);
      go.AddOptionInteger("Height", ref h);
      while (true)
      {
        var r = go.Get();
        if (r == GetResult.Option) continue;
        if (r == GetResult.Nothing) break;
        return Result.Cancel;
      }

      s.Modes.Clear();
      if (mat.CurrentValue) s.Modes.Add(PassMode.Material);
      if (obj.CurrentValue) s.Modes.Add(PassMode.Object);
      if (lay.CurrentValue) s.Modes.Add(PassMode.Layer);
      if (nrm.CurrentValue) s.Modes.Add(PassMode.Normal);
      s.ExportViewport = vpt.CurrentValue;
      s.Width = w.CurrentValue; s.Height = h.CurrentValue;
      if (s.Modes.Count == 0 && !s.ExportViewport) { RhinoApp.WriteLine("No passes selected."); return Result.Nothing; }

      s.NamedViews = Enumerable.Range(0, doc.NamedViews.Count).Select(i => doc.NamedViews[i].Name).ToList();
      if (s.NamedViews.Count == 0) { RhinoApp.WriteLine("No Named Views in this file."); return Result.Nothing; }
      plug.SaveSettings(s);

      try
      {
        var files = Exporter.Run(doc, s, msg => RhinoApp.SetCommandPrompt(msg));
        RhinoApp.WriteLine($"Enzyme ID Passes: wrote {files.Count} file(s) to {s.Folder}");
        return Result.Success;
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Enzyme ID Passes failed: " + ex.Message);
        return Result.Failure;
      }
    }
  }
}
