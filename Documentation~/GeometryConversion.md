# YAML geometry conversion

Version 1.6 adds an Editor-only, non-destructive geometry partitioning pipeline. It uses factual mesh data from the scanner and explicit feature intent from a user-authored YAML file. It does not infer feature ownership from shaders, animations, object names, VRChat tooling, or VRCFury.

## Workflow

1. Select and scan the avatar in **NVH > UV Tile Scanner > Scanner**.
2. Expand **YAML geometry conversion** and select **Export template**.
3. Edit the template. Rename or combine factual groups and add explicit feature ownership/states.
4. Assign the `.yaml` file in the window.
5. Select **Validate** or **Dry run**. Both parse, resolve, and classify without creating assets.
6. Review renderer, source material slot, group, triangle, output-submesh, unassigned, warning, and error entries.
7. Select **Convert avatar** after validation succeeds.

Conversion writes cloned mesh assets, a cloned avatar prefab, and a JSON manifest below `settings.outputDirectory`. Existing filenames are not overwritten: Unity generates unique output paths. The source hierarchy, mesh, materials, animations, imported model, and shaders are never edited.

## Schema version 1

Top-level fields are strictly limited to `schemaVersion`, `avatar`, `geometryGroups`, `features`, and `settings`. Unknown fields, duplicate keys, custom YAML tags, anchors/aliases, missing required fields, and unsupported schema versions are errors. Diagnostics include YAML line and column where available.

```yaml
schemaVersion: 1
avatar:
  name: "Example Avatar"
  description: "Optional"
geometryGroups:
  - id: "body-tile"
    renderer: "Body"
    materialSlots: [0]
    uvChannel: 1
    tiles: [1002]
    description: "Optional"
features:
  - id: "body-mode"
    type: exclusive
    defaultState: "shown"
    ownedGroups: ["body-tile"]
    states:
      - id: "shown"
        visibleGroups: ["body-tile"]
      - id: "hidden"
        visibleGroups: []
settings:
  outputDirectory: "Assets/UVTileScannerOutput"
  generatePreview: false
  unassignedGeometry: preserve
  triangleBoundaries: reject
```

Renderer paths are relative to the selected avatar root. `materialSlots` contains original zero-based renderer material/submesh indices. `uvChannel` uses Unity's zero-based terminology: channel 1 is the second UV set (`Mesh.uv2`). `tiles` uses standard UDIM numbers from 1001 through 2000.

Feature and state IDs are strings. Quote identifiers such as `"on"`, `"off"`, `"yes"`, and `"no"` to keep their meaning unambiguous across YAML tooling. A feature can affect only its `ownedGroups`; every state explicitly lists the owned groups visible in that state. Omitted owned groups are hidden. Unowned groups remain unaffected. Schema v1 rejects ownership of one group by multiple features.

## Classification and safety policies

Each source triangle is classified from all three corners on the selected UV channel. The UDIM mapping is `1001 + floor(U) + 10 * floor(V)`. Negative coordinates and tiles outside the supported range are rejected. Vertices within `0.00001` of an integer boundary are tested against adjacent tiles so a seam vertex shared by triangles on either side can be classified safely.

A triangle genuinely spanning tiles is never assigned from only its first vertex or centroid. With `triangleBoundaries: reject`, ambiguity is a conversion-blocking error. `preserveAsUnassigned` records a warning and retains that triangle in a pass-through submesh.

`unassignedGeometry: preserve` retains unmatched triangles in pass-through output submeshes. `error` makes any unassigned triangle conversion-blocking. Every supported source triangle must occur exactly once across the planned output submeshes. Original index values, winding, and source material-slot provenance are retained.

The output mesh is a clone of the source mesh. Only submesh index lists are repartitioned, so positions, normals, tangents, colours, UV channels, bone weights, bind poses, blendshapes, skinning data, and bounds remain on the clone. Only triangle topology is supported in schema v1; other topology is reported as an error.

## Manifest

The generated JSON manifest has `manifestVersion: 1`. It records source avatar and mesh asset identifiers where available, output prefab and mesh asset paths/GUIDs, source renderer paths, source material slots, original-to-output submesh mappings, group IDs, triangle counts, unassigned flags, feature ownership, states, defaults, and conversion diagnostics. It contains no Poiyomi-, VRChat-, VRCFury-, or Resonite-specific implementation data.

## Generated-preview limitation and interactive preview

`generatePreview` is reserved in schema v1, but separate toggleable preview renderers are not generated. Requesting it produces a warning. The converted prefab retains all partitioned geometry visibly; Unity submeshes are not independently toggleable renderers.

Version 1.7 adds an interactive, Editor-only alternative: feature preview loads the converted prefab, YAML, and manifest, then replaces hidden output material slots with a NoRender material on a disposable clone. It never changes the generated prefab or splits renderers. See [Feature visibility preview](FeaturePreview.md).

The included `Samples~/Nawvaro FT/NawvaroFT.yaml` preserves the manually supplied `Base` renderer mappings. They must be dry-run against the actual selected Nawvaro FT asset; the package does not silently alter them or assume additional `Animal Parts`, `Body`, or `Hair` mappings.
