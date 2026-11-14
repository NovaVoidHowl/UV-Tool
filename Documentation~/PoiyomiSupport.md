# Poiyomi support and limitations

The Poiyomi detector is optional and has no direct reference to Poiyomi or Thry assemblies. All inspection is read-only.

## Identification

The result is deliberately graded:

- **Confirmed**: the official version detector accepts the material, the effective shader uses the `.poiyomi/` namespace, or locked metadata resolves/names an official `.poiyomi/` shader.
- **Probable**: Poiyomi naming/path or locked metadata is paired with recognised Poiyomi properties.
- **Unknown**: the evidence is insufficient. A failed official check alone does not prove a legacy or locked shader is unrelated.
- **Confirmed non-target**: a recognised built-in shader namespace has no Poiyomi metadata/signatures.

Lock state uses `Hidden/Locked/`, `OriginalShader`, and `OriginalShaderGUID` evidence. A locked shader may have baked away inspector properties. Missing values are reported as partial/unknown, never reconstructed from the locked shader name or hash.

## Version detection

When present, the detector reflectively calls the official methods on `Poi.Tools.ShaderTranslator.VersionUpgrade.PoiyomiVersionDetector`:

- `IsPoiyomiShader(Material)`
- `TryGetVersion(Material, out Version)`

The method signatures are validated before invocation and every failure becomes a visible diagnostic. The current official implementation returns major/minor only because its parser constructs a two-component `System.Version`. That result is retained as the compatibility family, such as `9.3.x`.

Independently, a semantic version in `shader_master_label` is read from the current shader first, followed by resolvable original-shader metadata. This mirrors the label rendered by ThryEditor and lets a normal locked 9.3.64 shader report exact version `9.3.64` alongside compatibility family `9.3.x`. Three label components are exact; two components are a family/range. If the exact label and official family disagree, both values and their sources are retained with a diagnostic. Hashes and generated locked names are never treated as versions.

If locking or a missing shader removed both the label and the resolvable original shader, only the official family—or unknown—is reported. The scanner cannot reproduce an Inspector version that is not present in readable shader metadata.

`ShaderVersionInfo` always describes the analysed shader/material. The detector implementation's own compatibility/format coverage is documented below and is never presented as the shader version.

## Verified property formats

The following formats were verified against official Poiyomi sources for current 10.x development and, where noted, the 9.3.64 shader source:

| Feature | Enable/channel properties | Per-tile property |
| --- | --- | --- |
| UDIM tile discard | `_EnableUDIMDiscardOptions`, `_UDIMDiscardUV` | `_UDIMDiscardRow{row}_{column}` |
| Face tile discard | `_EnableUDIMFaceDiscardOptions`, `_UDIMFaceDiscardUV` | `_UDIMFaceDiscardRow{row}_{column}` |
| UV tile dissolve | `_EnableDissolve`, `_UVTileDissolveEnabled`, `_UVTileDissolveUV` | `_UVTileDissolveAlpha_Row{row}_{column}` |
| UV tile geometric dissolve | `_GeoDissolveAlpha`, `_UVTileGeoDissolveEnabled`, `_UVTileGeoDissolveUV` | `_UVTileGeoDissolveAlpha_Row{row}_{column}` |

Rows and columns are limited to the supported 4×4 grid (`0`–`3`). A property value of zero is inactive. Discard toggles at one are active/discarded. Dissolve controls preserve negative, partial positive, and maximum states instead of flattening them to a boolean. The material tag `<PropertyName>Animated` is shown when available.

The UDIM and regular dissolve formats are present in Poiyomi 9.3.64 and current 10.x source. Geometric UV tile dissolve was verified against the current official template. No broad compatibility claim is made for older releases whose property layouts have not been verified. Synthetic shaders are included for automated tests; real-world locked materials can still be only partially readable.

Official references:

- [Poiyomi UV Tile Discard documentation](https://www.poiyomi.com/10.0/special-fx/uv-tile-discard)
- [Poiyomi Dissolve documentation](https://www.poiyomi.com/10.0/special-fx/dissolve)
- [Poiyomi Toon Shader source](https://github.com/poiyomi/PoiyomiToonShader)
- [ThryEditor source](https://github.com/poiyomi/ThryEditor)
