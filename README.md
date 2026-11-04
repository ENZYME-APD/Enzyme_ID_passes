# Enzyme ID Passes for Rhino

Export **Material, Object and Layer ID passes**, a **surface normals pass** and a **standard viewport capture** for every saved Named View in Rhino 8+.

ID colours are drawn through a temporary display override: your document, materials and layers are never modified. Each pass lines up pixel for pixel with renders of the same view at the same resolution, ready for selection masks in Photoshop or any compositor.

<p align="center"><img src="assets/icon.png" width="96"></p>

## Features
- **Viewport (as displayed)**: a standard capture of each named view in the current display mode (e.g. Rendered).
- **Material / Object / Layer ID**: a flat, unlit, stable colour per material, object or layer. Blocks are supported, including nested ones.
- **Normals**: world-space normals (red = +X, green = +Y, blue = up), useful for relighting and for selecting surfaces by orientation.
- **Clean edges**: snaps anti-aliased pixels to the exact ID colours, so magic-wand selections are perfect.
- **Legend sheet**: a PNG showing each ID colour next to its material texture and name, plus CSV/JSON versions.
- **Live preview**: toggles the ID override in the viewports from the panel.
- **Scriptable**: `-EnzymeIDPassesExport` batch-exports using the panel's saved settings.

## Install
**Package Manager (recommended):** in Rhino, run `_PackageManager`, search for **EnzymeIDPasses** and click Install.

**Manual:** download the `.rhp` from [Releases](../../releases) and drag it onto Rhino.
- `EnzymeIDPasses-net7.0.rhp`: Rhino 8/9 on Mac, and on Windows (default runtime)
- `EnzymeIDPasses-net48.rhp`: Windows with Rhino set to the .NET Framework runtime

**Script only:** open `script/EnzymeIDPasses.py` in `_ScriptEditor` and run it. No install is needed; numpy is installed automatically on the first run.

## Usage
1. Save your camera views as Named Views.
2. Run `EnzymeIDPasses` to open the panel.
3. Tick the views and passes, set the resolution to match your renders, and choose an output folder.
4. Click **Export ID passes**.

Output files:
```
<View>_Viewport.png   <View>_MaterialID.png   <View>_ObjectID.png
<View>_LayerID.png    <View>_Normal.png
Legend_MaterialID.png / .csv / .json
```

## Known limitations
- Per-face (sub-object) materials take the object's main material.
- Clipping planes and section views are not handled yet.
- Legend thumbnails come from the diffuse / PBR base-colour bitmap; procedural textures show their base colour.

## Build from source
Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).
```bash
dotnet build plugin/EnzymeIDPasses.csproj -c Release   # builds net48 + net7.0 .rhp files
./yak/package.sh                                        # also builds the Yak package (needs Rhino's yak)
```

### Releasing
Push a version tag (`git tag v0.2.0 && git push --tags`). The GitHub Action builds the plugin, attaches the `.rhp` files, the `.yak` and the script to a GitHub Release, and publishes to the Rhino package server if the `YAK_TOKEN` repository secret is set (create it with `yak login --ci`).

## Contributing
Issues and pull requests are welcome.

## License
[MIT](LICENSE) © Enzyme APD. Developed by [Enzyme Digital Solutions](https://github.com/ENZYME-APD).
