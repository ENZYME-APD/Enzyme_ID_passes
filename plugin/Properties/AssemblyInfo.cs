using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Plug-in Description Attributes - all of these are optional.
// These will show in Rhino's option dialog, in the tab Plug-ins.
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Enzyme Digital Solutions")] // Sets the Yak url Attribute (Overrides <Company> in .csproj)
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "https://github.com/ENZYME-APD/Enzyme_ID_passes/releases")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://github.com/ENZYME-APD/Enzyme_ID_passes")] // Sets the Yak url Attribute

// Icons should be Windows .ico files and contain 32-bit images in the following sizes: 16, 24, 32, 48, and 256.
[assembly: PlugInDescription(DescriptionType.Icon, "EnzymeIDPasses.EmbeddedResources.plugin-utility.ico")]

// The following GUID is for the ID of the typelib if this project is exposed to COM
// This will also be the Guid of the Rhino plug-in
[assembly: Guid("de10317c-cb0f-43c5-8883-53d4d788bf4d")] 
