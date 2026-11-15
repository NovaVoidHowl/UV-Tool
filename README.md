# UV Tile Scanner

UV Tile Scanner is an Editor-only Unity package for auditing avatar meshes, UV tiles, renderer material assignments, shader-specific UV discard/dissolve settings, and material animation bindings. It can use explicit YAML to partition source triangles into geometry groups, create a non-destructive converted prefab and manifest, and preview configured feature visibility on a disposable clone.

The window uses UI Toolkit and is distributed as four compiled editor DLLs, the pinned YamlDotNet dependency, and UXML/USS assets. Source, documentation, samples, and tests remain available in folders ending in `~`, which Unity ignores when importing the package.

## Capabilities

- Finds `SkinnedMeshRenderer` and `MeshFilter` components below an avatar root, optionally including inactive objects.
- Groups repeated occurrences of the same mesh while retaining each renderer path and material assignment.
- Detects whether each mesh has UV1—the second Unity UV set—and reports its integer tile/UDIM occupancy.
- Lists every material slot and shader, including renderers with multiple materials.
- Maps each material slot to its actual submesh instead of treating every material as if it controlled the whole mesh.
- Discovers modular shader detectors through a guarded Editor-only registry.
- Includes optional, read-only Poiyomi identification, version, lock-state, and tile-feature analysis.
- Correlates shader tile controls with the occupied tiles on the feature's selected UV channel.
- Shows detector evidence, confidence, partial/baked states, diagnostics, and apparent tile mismatches.
- Finds avatar-linked animation clips through controllers, nested state machines, Blend Trees, overrides, and legacy Animation components.
- Extracts material-property and material-swap bindings, resolves renderer/material slots, and correlates recognised shader tile controls with their animation curves.
- Filters cached animation matches into UV controls, all material properties, or other material properties and groups them by clip and renderer/material target.
- Optionally searches every project animation clip, including sub-assets such as FBX clips, without treating unrelated assets as avatar-linked.
- Provides searchable results, a multiple-tiles filter, mesh pinging, and a copyable text report per material.
- Shows the installed tool version in an upper-right badge sourced from Unity Package Manager metadata.
- Exports factual YAML templates from completed scans without inferring semantic avatar features.
- Strictly validates versioned YAML, relative renderers, material slots, UV channels, UDIM selectors, group overlap, feature ownership, states, defaults, and output settings.
- Classifies every triangle from all three UV corners and explicitly reports tile-spanning, unsupported, conflicting, or unassigned geometry.
- Dry-runs expected renderer/group/material mappings and triangle/submesh counts without creating assets.
- Clones meshes and repartitions only their submesh index lists, retaining source vertex attributes, shared vertices, skinning, blendshapes, bounds, winding, and material provenance.
- Creates a cloned avatar prefab, new mesh assets, and a versioned JSON manifest without modifying source assets.
- Loads converted avatars plus YAML/manifest definitions into a disposable feature preview with toggle and exclusive-state controls.
- Recomputes complete renderer material arrays from preserved originals and substitutes only hidden mapped groups with a packaged NoRender material.
- Provides reset-to-defaults, restore-all-materials, and explicit end-preview cleanup actions.

## Compatibility

- Minimum Unity version: Unity 2021.3
- Primary validation target: Unity 2022.3.58f1
- Editor only; none of the assemblies are included in player builds
- Built-in render-pipeline shader inspection and feature preview; no runtime component is installed
- Mesh Read/Write does not need to be enabled because scanning uses Unity's read-only mesh data API

The project retains its `netstandard2.1` build and Unity 2021.3 minimum-package declaration. The current automated compile and Edit Mode validation run uses Unity 2022.3.58f1; the older Editor baseline was not rerun for this release.

## Package layout

| Path | Purpose |
| --- | --- |
| `Editor/Nvh.UvTileScanner.Core.Editor.dll` | Mesh scanner, result models, detector contract/registry, caching, and mesh/material correlation |
| `Editor/Nvh.UvTileScanner.Conversion.Editor.dll` | YAML/manifest models, validation, geometry conversion, asset output, and disposable feature-preview session |
| `Editor/Nvh.UvTileScanner.Editor.dll` | UI Toolkit EditorWindow |
| `Editor/Nvh.UvTileScanner.Poiyomi.Editor.dll` | Optional Poiyomi detector with no mandatory Poiyomi dependency |
| `Editor/YamlDotNet.dll` | Pinned YamlDotNet 18.1.0 parser dependency used only by the conversion assembly |
| `Editor/Preview/UVTileScannerNoRender.shader` | Built-in Render Pipeline shader used to suppress hidden preview submeshes |
| `Editor/Preview/UVTileScannerNoRender.mat` | Read-only packaged material referencing the NoRender shader |
| `Editor/UI/UvTileScannerWindow.uxml` | Editable window layout |
| `Editor/UI/UvTileScannerWindow.uss` | Editable window styling |
| `Source~` | C# source and DLL build projects; ignored by Unity package import |
| `Tests~` | Edit Mode tests and synthetic shader fixtures; ignored by default |
| `Documentation~` | Detector-extension and Poiyomi support details |
| `Documentation~/AnimationAnalysis.md` | Animation discovery, binding matching, and generated-controller limitations |
| `Documentation~/GeometryConversion.md` | YAML schema, workflow, classification policies, output manifest, and generated-preview limitation |
| `Documentation~/FeaturePreview.md` | Feature preview workflow, material restoration guarantees, diagnostics, and shader limitations |
| `Samples~/Nawvaro FT/NawvaroFT.yaml` | Manually supplied first-milestone mapping for the Nawvaro FT `Base` renderer |
| `Third Party Notices.md` | YamlDotNet version, source, and MIT licence notice |

The UI DLL depends on the core and conversion DLLs. The conversion DLL depends on core and `YamlDotNet.dll`. The Poiyomi detector DLL can be omitted if Poiyomi analysis is not wanted; scanning, YAML conversion, and the UI continue to work. Keep the UXML and USS assets with the UI DLL—they are package assets, not embedded resources.

## Installation and removal

Install using one of these approaches:

1. In Unity, open **Window > Package Manager**, choose **+ > Add package from git URL**, and enter this repository's Git URL.
2. For a local checkout, choose **+ > Add package from disk** and select this package's `package.json`.
3. Copy the complete package into the project's `Packages` directory, ideally as `Packages/uk.novavoidhowl.dev.uv-tile-scanner`.

Installing the complete package lets the version badge resolve the version directly from `package.json`. If the `Editor` directory is copied loose into `Assets`, scanning can still work, but Unity Package Manager cannot associate the assembly with its package and the badge will show **Version unavailable**.

Do not copy only `Nvh.UvTileScanner.Editor.dll`; it requires the core/conversion DLLs, YamlDotNet, `Editor/UI`, and `Editor/Preview` assets. To remove the tool, remove the package through Package Manager or delete its complete local package directory.

## Using the scanner

1. Open **NVH > UV Tile Scanner**.
2. Assign the avatar's root GameObject to **Avatar root**.
3. Choose whether inactive objects should be included.
4. Enable **Search whole project for animations** only when you also want potential matches from clips outside the avatar's controllers.
5. Select **Scan avatar**.
6. Follow the in-window progress bar throughout renderer/material discovery, mesh UV and shader analysis, then animation discovery and processing. It reports the current phase and clip where applicable; select **Cancel scan** to keep any completed mesh/material results and stop remaining work.
7. Use **Animation analysis filter** to choose **UV Controls** (the default), **All Properties**, or **Other Properties**, and optionally search the cached matches.
8. Expand the avatar-wide **Animation analysis** section, then a clip and renderer/material target, to inspect properties, extracted values, keyframes, confidence, and source details.
9. Expand **Shader analysis** or **Animation analysis** below a material to inspect feature/tile controls and matching curves under the same animation filters.
10. Use **Copy** beside a material for a plain-text shader and animation report.

### YAML geometry conversion

After scanning, expand **YAML geometry conversion**:

1. Select **Export template** and save a new `.yaml` asset. Existing files require explicit replacement confirmation.
2. Review the factual renderer/material-slot/UV/tile groups. Give them deliberate IDs and define feature ownership/states; the exporter never derives meanings from names, shaders, or animations.
3. Assign the YAML asset, then select **Validate** or **Dry run**. Both are read-only and show renderer/material mappings, group triangle counts, unassigned triangles, boundary diagnostics, and expected output submeshes.
4. Select **Convert avatar** only after validation passes. Conversion creates uniquely named cloned meshes, a prefab, and a JSON manifest below `settings.outputDirectory` and pings the prefab.

The schema uses zero-based Unity UV channels and standard UDIM mapping. Triangle classification considers all three corners, preserves shared vertices and material boundaries, and never silently deletes or duplicates source triangles. Unsupported topology, negative/out-of-range tiles, overlapping groups, conflicting feature ownership, invalid slots, and unsafe YAML are errors. Unmatched or boundary-ambiguous triangles follow the explicit YAML policies.

See [YAML geometry conversion](Documentation~/GeometryConversion.md) for the complete schema, safety rules, and manifest. The included Nawvaro FT sample retains the supplied `Base` mappings; dry-run it against the actual selected asset before conversion. No additional avatar mappings are assumed.

The Nawvaro file can also be imported through Package Manager's **Samples** section for this package, then selected as a `TextAsset` in the conversion UI.

### Feature visibility preview

The always-available **Feature visibility preview** panel accepts a converted avatar prefab or scene object, the YAML configuration, and its generated manifest. Successful conversion fills the output prefab and manifest fields automatically.

Starting preview creates a disposable scene clone and applies YAML default states. Two-state toggle features use a toggle control; exclusive and other named-state features use dropdowns. Hidden owned groups are resolved through the manifest to output material slots and receive the NoRender material. Visible groups, unowned groups, and unrelated slots retain their captured originals.

Each change rebuilds every mapped renderer's entire material array from its initial snapshot before applying all active states. **Reset to YAML defaults**, **Restore all materials**, and **End preview** provide explicit recovery and cleanup. Closing the window or changing an input also destroys the clone. Source prefabs, FBXs, meshes, and materials are not edited.

See [Feature visibility preview](Documentation~/FeaturePreview.md) for validation behavior and Built-in-pipeline limitations.

You can also right-click a selected hierarchy object and choose **Scan second UV tiles**.

The mesh search field matches mesh names, renderer paths, material names, shader names, detector families, and detected feature names. **Multiple tiles only** restricts the list to meshes whose second UV set occupies more than one tile. The separate animation search matches clip names, renderer names or paths, material names, animation property names, feature names, and UDIM numbers.

## Animation analysis

The complete scan is read-only and runs over Editor updates: renderer/material discovery, mesh UV and shader analysis, then animation discovery and clips. The window stays responsive and shows its current phase, percentage, and clip where applicable. Cancelling preserves any mesh, material, and shader results already gathered. **Avatar-linked** scope, the default, gathers clips reachable from `Animator` controllers below the selected root, including nested state machines, Blend Trees, `AnimatorOverrideController` replacements, and legacy `Animation` components. It also reads direct `RuntimeAnimatorController` references exposed by loaded descriptor components through guarded reflection.

**Whole project** adds clips returned by `AssetDatabase.FindAssets("t:AnimationClip")`; all assets at each path are inspected so imported sub-assets such as FBX clips are included. A whole-project clip is still shown as a potential match until its binding resolves against the selected avatar.

Known Unity default clips with no useful avatar-material information are excluded before curve analysis. The central `AvatarAnimationAnalyzer.IgnoredAnimationClipNames` blocklist currently contains `HumanoidDefault`, and is the single place to add future exclusions.

For each float or object-reference curve the scanner retains the asset path, source controller/state, relative binding path, component type, property name, keyframes, target GameObject and renderer when resolvable, material slot, and material replacement value. Renderer plus material-slot resolution is **Confirmed**. A renderer with no resolvable slot, or a recognised shader property with no resolvable renderer, is **Probable**. Everything else remains **Unknown**.

Material bindings support Unity renderer `material`/`materials` paths and indexed `m_Materials.Array.data[n]` paths. Slot zero is never assumed when an animation binding does not specify a slot. Object-reference curves on indexed material arrays are reported as material swaps. Known detector controls are matched through each feature's existing generic `ShaderTileControl` data, so the core has no Poiyomi property-name dependency. For example, an animated Poiyomi UV Tile Dissolve row/column control is shown with its feature and UDIM number.

The displayed animation view defaults to **UV Controls**, which includes only bindings associated with feature metadata supplied by an installed shader detector. **Other Properties** contains matched material properties without recognised UV feature metadata, including unknown shader properties. **All Properties** combines both categories. These filters operate on the completed in-memory report and never repeat animation discovery or AssetDatabase searches.

Results are grouped by animation clip asset identity, then renderer/material target, then animated property. Identically named clip assets remain separate. Clip and target details are created only when their foldouts are expanded, keeping whole-project result sets manageable. Summary counts distinguish all matched material bindings, UV feature bindings, other material bindings, and currently visible bindings.

For recognised controls the UI shows feature name, UV channel, row, column, UDIM number, property name, actual extracted values/range, target objects, confidence, and keyframes. If separate clips set the same detector feature, material slot, and tile to constant `0`/`1` or `-1`/`1` values, the scanner highlights that value evidence without assigning on/off semantics or inferring a toggle relationship.

Clip behavior is reported conservatively as a constant state-setting animation, animated transition, material swap, or unknown from the actual keyframes. The scanner does not assign semantic avatar labels such as “human” or “furry” from values alone.

See [Animation analysis details and limitations](Documentation~/AnimationAnalysis.md) for discovery coverage, source behavior, and generated-controller limits.

### UV channel terminology

Unity's low-level mesh API calls its channels UV0, UV1, UV2, and UV3. The scanner's primary mesh summary examines **UV1**, which is the second UV set and corresponds to `Mesh.uv2`.

Shader features may select a different channel. Their correlations are calculated independently on the selected channel and never merged merely because two meshes or channels share the same UDIM number.

### Material and submesh mapping

Material results are retained per renderer occurrence. If the same mesh is reused with different materials elsewhere in the avatar, every assignment remains visible.

A material slot normally maps to the submesh with the same index. When Unity has more materials than submeshes, each extra material renders the final submesh again; the scanner uses the same mapping. A mismatch warning is shown whenever material and submesh counts differ.

## How UV tiles are determined

Each triangle or supported primitive is assigned to the integer tile containing its UV centroid. This avoids treating vertices exactly on the upper edge of the 0–1 square as belonging to another tile. If a primitive crosses an integer tile boundary, the result shows a warning so it can be inspected manually.

For meshes without usable indexed primitives, the scanner falls back to their vertex UV coordinates.

Tile `(0, 0)` is UDIM 1001, `(1, 0)` is 1002, and `(0, 1)` is 1011. Negative coordinates are reported as tile coordinates without a standard UDIM number. Shader control row/column coordinates and calculated UDIM numbers remain separately visible.

## Poiyomi analysis

The Poiyomi detector is included by default but has no compile-time dependency on Poiyomi or ThryEditor. It remains loadable when no Poiyomi package is installed.

### Identification and locked shaders

Identification is classified as confirmed Poiyomi, probable Poiyomi, unknown, or confirmed non-Poiyomi. Evidence can include:

- the current shader name;
- locked `Hidden/Locked/` naming;
- `OriginalShader` and `OriginalShaderGUID` material tags;
- resolvable original shader assets and paths;
- recognised shader properties and serialized material floats;
- the optional official Poiyomi detector API.

Inspection is read-only. The scanner never unlocks a material, changes keywords or properties, or derives a version from a generated shader hash. Optimized shaders can bake away non-animated properties; unrecoverable settings are reported as partial or unknown rather than replaced with assumed defaults.

### Exact version and compatibility family

The version display intentionally separates two pieces of information:

- **Version** is the exact version from `shader_master_label` when a three-component label is readable, such as `9.3.64`.
- **Compatibility family** is the major/minor result used for compatibility decisions, such as `9.3.x`.

When installed, `Poi.Tools.ShaderTranslator.VersionUpgrade.PoiyomiVersionDetector` is called through guarded reflection. Its current `TryGetVersion` implementation returns only major/minor, so the result is preserved as the compatibility family. The scanner independently examines the current shader's full label—matching the label shown by ThryEditor—and then resolvable original-shader metadata. Thus a normal locked 9.3.64 material can show exact version `9.3.64` alongside official compatibility family `9.3.x`.

If the exact label and official family disagree, both values and sources remain visible with a diagnostic. If neither exact metadata nor a family can be established, the version is **Unknown**. Exact values are never inferred from locked shader hashes.

### Supported Poiyomi feature formats

The detector currently recognises:

- UDIM tile discard;
- face tile discard;
- UV tile dissolve;
- UV tile geometric dissolve.

For each recognised feature it reports the enable state, UV channel, tile property name and type, current readable value, inferred state, animation tag, readability, confidence, and evidence. Configured tile coordinates are compared with tiles occupied by the material's mapped submesh on that feature's UV channel.

The regular UDIM and UV tile dissolve formats were verified against Poiyomi 9.3.64 and current 10.x source. Geometric UV tile dissolve was verified against the current official template. This is not a claim of exhaustive compatibility with every historical or third-party Poiyomi variant.

See [Poiyomi support and limitations](Documentation~/PoiyomiSupport.md) for exact property patterns, version behavior, and source references.

## Detector architecture

The core discovers parameterless `IShaderUvTileDetector` implementations through Unity `TypeCache`; it does not load arbitrary assemblies. Multiple detectors may analyse the same material. A detector failure becomes a visible diagnostic and does not stop other detectors or the mesh scan.

Analysis is cached once per unique material during a scan, then shared by each renderer/material-slot occurrence. UI construction consumes the cached report and does not rescan shader properties during repaint.

To add another shader family without modifying the core or UI, implement `IShaderUvTileDetector` in a separate Editor-only assembly. See [Adding a shader detector](Documentation~/AddingShaderDetector.md).

## Version badge

The badge in the upper-right of the window calls `UnityEditor.PackageManager.PackageInfo.FindForAssembly` for the UI assembly and displays the returned `package.json` version. There is no second hardcoded tool-version constant to update. The badge tooltip shows the package display name and version.

If Package Manager cannot resolve the assembly's package metadata, the badge reports **Version unavailable** without preventing the window from loading.

## Development commands

The root `Makefile` provides the routine build, test, and package-version commands. Run `make help` for the same one-line summary in the terminal.

| Command | Purpose |
| --- | --- |
| `make help` | List the available commands with one-line descriptions |
| `make info` | Show the command summary plus resolved package, Unity, test-project, and output configuration |
| `make init-config UNITY_DATA_DIR=/path/to/Editor/Data` | Create an ignored local file that remembers the Unity paths |
| `make version` | Print the current version from `package.json` |
| `make set-version VERSION=1.4.0` | Validate and update the `package.json` version |
| `make build` | Build all four Editor DLLs and copy YamlDotNet into `Editor` |
| `make rebuild` | Clean and rebuild all DLL projects |
| `make clean` | Clean the four .NET build projects |
| `make sync-tests` | Refresh the tests and shader fixtures in the validation project |
| `make test` | Build the DLLs, synchronize tests, and run all Edit Mode tests |
| `make test-only` | Synchronize and run tests without rebuilding the DLLs |
| `make verify` | Run the complete build-and-test verification and print the results path |
| `make list-dlls` | List the distributable DLLs and their sizes |

For one-time setup, create the local build configuration with your Unity `Editor/Data` directory:

```bash
make init-config UNITY_DATA_DIR=/path/to/Unity/Hub/Editor/2022.3.x/Editor/Data
make build
make test
```

This creates `build.config.mk` at the project root. It is deliberately ignored by Git and may contain any of these developer-local settings:

```makefile
UNITY_DATA_DIR := /path/to/Unity/Editor/Data
UNITY_EDITOR_DIR := /path/to/Unity/Editor/Data
UNITY_EXECUTABLE := /path/to/Unity/Editor/Unity
```

Only `UNITY_DATA_DIR` or `UNITY_EDITOR_DIR` is required. `UNITY_EXECUTABLE` is optional because test runs normally derive it from the data directory. `make init-config` will not overwrite an existing config; edit or remove the local file when paths change.

Set `BUILD_CONFIG=/another/path/config.mk` to load or initialize a different file. Command-line values take precedence over values in the config, so a one-off build can still use `make build UNITY_DATA_DIR=/different/Editor/Data`. Environment variables also remain supported when no Makefile or command-line value overrides them.

For test runs, `TEST_PROJECT` can select a different Unity validation project, and `TEST_RESULTS` and `TEST_LOG` can change the output paths. By default, test results and logs go into that project's `Logs` directory so Unity does not import changing output files as package assets or remove them during shutdown. The default test project is the ignored `ValidationProject~` directory and must have the Unity Test Framework installed.

`make set-version` deliberately changes only `package.json`. That keeps the window badge in step automatically, while changelog and release-note edits remain explicit release tasks.

### Direct build script

Pass a Unity `Editor/Data` directory to the build script:

```bash
./scripts/build.sh /path/to/Unity/Hub/Editor/2022.3.x/Editor/Data
```

Alternatively, set `UNITY_EDITOR_DIR` and run `./scripts/build.sh` without an argument. The script builds all four projects as deterministic `netstandard2.1` Editor assemblies and copies them plus YamlDotNet 18.1.0 into `Editor`.

The build intentionally references Unity's managed facade assemblies to retain compatibility when types move between Unity modules across LTS versions. The Makefile delegates to this script for `build` and `rebuild`.

## Development tests

`Tests~` currently covers:

- unique-mesh grouping, UV1 presence, boundary handling, and multi-tile detection;
- material slots and shaders across renderer occurrences;
- UXML/USS import and EditorWindow construction;
- version-badge agreement with Unity Package Manager metadata;
- detector discovery, multiple detectors, failure isolation, and no-detector operation;
- duplicate-material analysis caching;
- direct, indexed, nested, shared-material, override, Blend Tree, material-swap, whole-project, unknown-property, and no-controller animation binding paths;
- detector-neutral animation filtering, combined search, clip/target grouping, duplicate clip names, cache reuse, probable matches, and missing references;
- confirmed, probable, locked, unsupported, and missing-property Poiyomi cases;
- exact, family-only, official-partial, official-exact, and unknown version paths;
- separate UV-channel and material/submesh tile correlation.
- strict YAML parsing/serialization and unsafe/unknown/duplicate/schema rejection;
- renderer/UV/material resolution, UDIM conversion, seam and spanning-triangle classification;
- overlaps, feature ownership/defaults, unassigned preservation, exact membership, shared vertices, and multiple materials;
- cloned mesh skinning/blendshape/bounds preservation, non-destructive prefab output, manifest mappings, template export, detector independence, and scanner-report integration.
- feature toggle/exclusive-state switching, multi-group ownership, complete material restoration, cleanup, NoRender configuration, and prefab/mesh/material non-mutation.

The latest Unity 2022.3.58f1 compile succeeds and its Edit Mode run passes all 49 tests. The four projects retain the established `netstandard2.1`/Unity 2021.3-compatible structure, but this release was not independently rerun in a Unity 2021.3 Editor.

To run the suite with the checked-out validation project, use `make test` as described above. To run the tests in another project manually, copy `Tests~` into that project's `Assets` directory or temporarily remove the trailing `~` so Unity imports the test assembly and fixtures.

## Current limitations

- Geometry conversion partitions meshes and emits a neutral manifest; it does not generate Resonite components, ProtoFlux, platform shaders, or publish avatars.
- Schema v1 does not create separate toggleable renderers. `generatePreview: true` still produces a warning; interactive preview instead substitutes materials on a disposable converted-avatar clone.
- NoRender preview supports only the Built-in Render Pipeline. Unsupported or missing source-material shaders are reported but can still have their slot reference hidden/restored. Custom replacement shaders, command buffers, selection outlines, or other render overrides may still process or visualise geometry; URP/HDRP preview is rejected.
- Only triangle submesh topology is converted. Other topology is rejected rather than rewritten.
- Animation filter views contain bindings correlated with at least one scanned material slot. Unmatched raw project curves remain in the overall scan statistics but are not presented as material-property matches.
- The primary mesh summary scans UV1. Other channels are scanned only when a detected shader feature selects them.
- Primitive tile assignment uses UV centroids and reports boundary-crossing primitives for manual review.
- Locked shaders may have irreversibly baked or removed configuration data.
- Poiyomi property support is deliberately limited to verified patterns; unverified variants remain unknown or partial.
- Generated controller changes made by VRCFury, VRChat tooling, or other build pipelines are visible only when their generated controller assets are already accessible. The scanner does not invoke avatar builds or third-party hooks.
- Controller references stored in opaque third-party data structures may be unavailable; reachable source assets and whole-project clips remain separately visible.
- The version badge requires installation as a discoverable Unity package to read `package.json` automatically.
