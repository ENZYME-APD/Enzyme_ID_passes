using System;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.PlugIns;
using EnzymeIDPasses.Core;
using EnzymeIDPasses.UI;

namespace EnzymeIDPasses
{
  public class EnzymeIDPassesPlugin : PlugIn
  {
    public EnzymeIDPassesPlugin() { Instance = this; }

    public static EnzymeIDPassesPlugin Instance { get; private set; }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
      Icon icon = null;
      try
      {
        using (var s = GetType().Assembly.GetManifestResourceStream("EnzymeIDPasses.EmbeddedResources.plugin-utility.ico"))
          if (s != null) icon = new Icon(s);
      }
      catch { /* icon is optional */ }
      Rhino.UI.Panels.RegisterPanel(this, typeof(IdPassPanel), "Enzyme ID Passes", icon ?? SystemIcons.Application);
      return LoadReturnCode.Success;
    }

    // ------------------------------------------------------------------ persisted settings
    public ExportSettings LoadSettings()
    {
      var st = Settings;
      var s = new ExportSettings
      {
        Width = st.GetInteger("Width", 3000),
        Height = st.GetInteger("Height", 2000),
        Folder = st.GetString("Folder", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        SnapEdges = st.GetBool("SnapEdges", true),
        WriteLegend = st.GetBool("WriteLegend", true),
        ExportViewport = st.GetBool("ExportViewport", true)
      };
      var modes = st.GetString("Modes", "Material");
      s.Modes = modes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(m => Enum.TryParse(m.Trim(), out PassMode pm) ? pm : PassMode.Material)
                     .Distinct().ToList();
      return s;
    }

    public void SaveSettings(ExportSettings s)
    {
      var st = Settings;
      st.SetInteger("Width", s.Width);
      st.SetInteger("Height", s.Height);
      st.SetString("Folder", s.Folder ?? "");
      st.SetBool("SnapEdges", s.SnapEdges);
      st.SetBool("WriteLegend", s.WriteLegend);
      st.SetBool("ExportViewport", s.ExportViewport);
      st.SetString("Modes", string.Join(",", s.Modes));
    }
  }
}
