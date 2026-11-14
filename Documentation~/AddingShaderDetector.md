# Adding a shader detector

Shader-family support is discovered by the core DLL through Unity's `TypeCache`. A detector can live in its own editor-only assembly and should reference only `Nvh.UvTileScanner.Core.Editor.dll` plus Unity editor/runtime assemblies.

Implement `IShaderUvTileDetector` with a parameterless constructor:

```csharp
using Nvh.UvTileScanner;
using UnityEngine;

public sealed class MyShaderDetector : IShaderUvTileDetector
{
    public string Id => "example.my-shader";
    public string DisplayName => "My shader analyser";

    public bool CanAnalyze(Material material)
    {
        return material != null && material.shader != null &&
               material.shader.name.StartsWith("MyShaders/");
    }

    public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context)
    {
        // Read the material and return evidence-backed immutable result objects.
        // Never change material values, keywords, tags, assets, or importer state.
        return new ShaderAnalysisResult(
            Id, DisplayName, "My Shader", ShaderFamilyClassification.Confirmed,
            AnalysisTriState.No, ShaderVersionInfo.Unknown());
    }
}
```

## Contract expectations

- Keep `CanAnalyze` cheap and read-only. Return false when the detector has no useful statement about the material.
- Return one `ShaderAnalysisResult` containing identification classification/evidence, lock state, version method/confidence, feature results, and diagnostics.
- Use an exact property naming pattern. Do not infer settings from unrelated float properties.
- Set `UvChannel` only when the material provides a valid, readable channel. The core then correlates that feature to the material slot's mapped submesh.
- Treat missing or baked properties as unknown/partial. Do not substitute shader defaults unless those defaults are directly readable and identified as such.
- Catch expected reflection/property-format differences locally and return a diagnostic. The registry also catches detector-wide failures, so one integration cannot stop the scan.

Discovery uses `TypeCache.GetTypesDerivedFrom<IShaderUvTileDetector>()` and instantiates only the returned types. It does not scan or load arbitrary DLLs. Multiple detectors may analyse the same material. Each material is analysed once per scan and its immutable report is shared by all of its renderer slots.

For isolated tests, pass an explicit `ShaderDetectorRegistry` to `AvatarUvScanner.Scan(root, includeInactive, registry)`. This also makes it straightforward to test the core with no detector assemblies installed.
