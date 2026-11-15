# Changelog

## 1.7.0 - 2026-11-15 - (plan H - Imp A)

- Added a YAML/manifest-driven feature visibility preview using a disposable converted-avatar scene clone.
- Added dynamic UI Toolkit toggle and named-state controls with YAML defaults, explicit owned groups, and visible-group rules.
- Added public manifest parsing and renderer/output-submesh mapping validation for preview consumers.
- Added complete original-material-array snapshots and full recomputation on every state change, plus reset, restore, and end-preview actions.
- Added an Editor-only Built-in Render Pipeline NoRender shader/material that writes no colour or depth and provides no shadow-caster pass.
- Added clear diagnostics for missing mappings, invalid indices, ownership conflicts, missing materials, unsupported render pipelines, and manifest mismatches.
- Added Edit Mode coverage for toggles, exclusive states, multiple groups, restoration, failed and explicit cleanup, NoRender state, and prefab/mesh/material non-mutation.
- Matched the NoRender material's internal object name to its asset filename to avoid Unity's main-object-name warning.

## 1.6.0 - 2026-11-14 - (plan H/A - Imp A)

- Added a strict, versioned YAML schema and safe YamlDotNet 18.1.0 representation-model parser with duplicate-key, unknown-field, tag, anchor, and line/column diagnostics.
- Added factual scan-to-YAML template export without semantic feature inference.
- Added renderer/material-slot validation, corner-aware triangle/UDIM classification, explicit boundary and unassigned policies, and exact triangle-membership validation.
- Added non-destructive mesh cloning and submesh partitioning that preserves vertex buffers, skinning, blendshapes, bounds, material provenance, and shared vertices.
- Added cloned-avatar prefab output and a versioned JSON conversion manifest with persistent asset identities, submesh mappings, feature states, defaults, and diagnostics.
- Added UI Toolkit actions for template export, YAML selection, validation, dry run, mapping inspection, and gated conversion.
- Added the manually supplied Nawvaro FT `Base` example, schema/workflow documentation, dependency licence notices, and synthetic Edit Mode coverage.
- Added a dedicated conversion assembly and packaged YamlDotNet dependency; no Poiyomi, VRChat, VRCFury, or Resonite dependency is introduced.

## 1.5.1 - 2026-11-08 - (plan H - Imp A)

- Fixed nested per-material animation clip foldouts being immediately replaced by a bubbled parent foldout change event.
- Kept binding details lazy while building lightweight filtered clip headers without a parent expansion callback.
- Added a regression test covering nested material animation foldout expansion.

## 1.5.0 - 2026-11-08 - (plan H - Imp A)

- Added detector-neutral animation filters for UV controls, all matched material properties, and other material properties, defaulting to UV controls.
- Added cached animation search across clips, renderers, materials, properties, feature names, and UDIM numbers without rescanning assets.
- Grouped animation results by clip and renderer/material target, with lazy binding-detail construction for large whole-project reports.
- Added UV feature/channel/tile/value details, conservative complementary-value evidence, uncertainty labels, and filtered summary counts.
- Added Edit Mode coverage for filtering, grouping, duplicate clip names, multiple detector types, multiple targets, probable matches, cache reuse, and missing references.

## 1.4.2 - 2026-11-08 - (plan H - Imp A)

- Excluded Unity's `HumanoidDefault` clip through a named, future-extensible animation blocklist.
- Extended in-window scan progress and cancellation to renderer/material discovery and mesh UV/shader analysis before animation processing.

## 1.4.1 - 2026-11-08 - (plan H - Imp A)

- Added an in-window animation-analysis progress bar, current work description, and cancellation control.
- Processed whole-project animation discovery and clip analysis incrementally so the Editor window can repaint between work items.

## 1.4.0 - 2026-11-08 - (plan H/A - Imp A)

- Added read-only animation clip discovery from Animator Controllers, nested state machines, Blend Trees, Animator Override Controllers, legacy Animation components, optional descriptor controller references, and whole-project assets.
- Added float and object-reference curve extraction, renderer/material-slot resolution, material-swap reporting, keyframe summaries, and confidence-based matching.
- Added detector-neutral correlation between animation properties and recognised shader feature/tile controls, including Poiyomi UDIM values.
- Added avatar-wide and per-material Animation Analysis UI, copy-report output, synthetic animation fixtures, and Edit Mode coverage.

## 1.3.4 - 2026-11-08 - (plan H - Imp A)

- Added a root Makefile for build, test, verification, package-version, and project-information commands.
- Added an ignored local Make configuration file option for persistent Unity Editor paths.
- Expanded the README with current installation, scanner, detector, versioning, build, and test documentation.

## 1.3.3 - 2026-11-08 - (plan H - Imp H)

- Corrected tool access path for consistency with other packages

## 1.3.2 - 2026-11-08 - (plan H - Imp A/H)

- Added an upper-right version badge populated from Unity Package Manager metadata for the installed scanner assembly.

## 1.3.1 - 2026-11-08 - (plan H - Imp A)

- Preserved exact Poiyomi versions from `shader_master_label` alongside the official API's major/minor compatibility family.
- Added separate version and compatibility-family sources to the UI and copied analysis report.
- Added exact, partial, and unknown version-path coverage, including a guarded official-API test double.

## 1.3.0 - 2026-11-08 - (plan A/H - Imp A)

- Split the editor tool into independent core, UI, and optional Poiyomi detector DLLs.
- Added a TypeCache-based detector contract and registry with per-detector diagnostics and per-material caching.
- Added guarded support for Poiyomi identification, lock state, official version detection, and conservative version-label fallback.
- Added read-only extraction for UDIM discard, face discard, UV tile dissolve, and UV tile geometric dissolve controls.
- Added material-slot-to-submesh correlation using each feature's selected UV channel.
- Added collapsible shader analysis UI, evidence, confidence/readability details, mismatches, and copyable reports.
- Added synthetic detector, locked/legacy Poiyomi, failure isolation, caching, and submesh-correlation tests.

## 1.2.0 - 2026-11-08 - (plan A/H - Imp A)

- Added per-renderer material-slot reporting for every scanned mesh.
- Added shader reporting for each assigned material.
- Added warnings for missing renderers and material/submesh-count mismatches.

## 1.1.0 - 2026-11-08 - (plan H - Imp A)

- Converted the editor window from IMGUI to UI Toolkit.
- Added editable UXML layout and USS styling assets.

## 1.0.0 - 2026-11-08 - - (plan H - Imp A)

- Added avatar hierarchy scanning for skinned and static meshes.
- Added second-UV-set detection and per-mesh UV/UDIM tile reporting.
- Added boundary-crossing warnings, search, filtering, and mesh asset pinging.
- Shipped the tool as an Editor-only compiled assembly.
