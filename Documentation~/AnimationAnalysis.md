# Animation analysis

UV Tile Scanner performs animation analysis while scanning the selected avatar. It reads assets and scene references only; it does not alter clips, controllers, materials, avatars, or build state.

The complete scan yields between work items on Unity Editor updates: renderer/material discovery, mesh UV and shader analysis, then animation discovery and curve processing. The scanner window shows the current phase, percentage, and current clip where applicable, and offers a cancel button. Cancelling preserves any mesh, material, and shader portion already gathered.

## Discovery scopes

Avatar-linked is the default. It gathers clips used by `Animator` components below the selected root, traverses `AnimatorController` layers and nested state machines, follows `BlendTree` children, and records `AnimatorOverrideController` replacements. Legacy `Animation` component states are also included.

When a loaded component whose type name contains `Descriptor` exposes a direct `RuntimeAnimatorController` field or property, that controller is inspected through guarded reflection. Missing scripts, inaccessible members, and controller read errors become diagnostics rather than scan failures.

Whole project adds every `AnimationClip` found by `AssetDatabase.FindAssets("t:AnimationClip")`. The scanner calls `AssetDatabase.LoadAllAssetsAtPath` for each result, so FBX and other sub-assets are included. Project-only clips are potential matches until their paths resolve against the selected avatar.

The named `AvatarAnimationAnalyzer.IgnoredAnimationClipNames` blocklist is applied to every discovery source before bindings are read. It currently excludes Unity's `HumanoidDefault` clip, which does not provide useful avatar-material information. Add future known-unhelpful defaults to this central list.

## Binding and material matching

Float curves come from `AnimationUtility.GetCurveBindings` and object-reference curves from `AnimationUtility.GetObjectReferenceCurveBindings`. Each result retains its clip, asset path, source controller/state location, target path, component type, property name, keyframes, and resolved target objects.

The scanner recognizes indexed renderer material paths such as `materials.Array.data[1]._Property` and `m_Materials.Array.data[1]`. The latter is reported as a material swap when it has object-reference keys. A binding without an indexed slot remains explicitly unresolved; the scanner never chooses slot zero by default.

Confidence describes the material relationship:

- Confirmed: renderer and material slot both resolve to a scanned avatar occurrence.
- Probable: renderer resolves but its slot does not, or a recognised detector property matches without a resolvable renderer.
- Unknown: the relationship cannot be established.

Detector feature controls are matched using their generic `ShaderTileControl.PropertyName`. This lets detector DLLs add semantic animation correlation without changing the core. Poiyomi controls therefore display their recognised feature and UDIM row/column when animated.

## Filtering, grouping, and search

The animation view defaults to **UV Controls**. A binding enters this category when one of its existing `AnimationMaterialMatch` records contains detector-supplied `ShaderFeatureAnalysis` metadata. No Poiyomi property names or other shader-specific rules are built into the filter.

- **UV Controls** shows matched bindings associated with recognised detector features.
- **All Properties** shows every binding correlated with a scanned material slot.
- **Other Properties** shows correlated material bindings without recognised feature metadata, including unknown shader properties.

The animation search works together with the category filter and matches clip name, clip asset path, renderer name/path, material name, property name, detector feature name, and UDIM number. Filtering uses a cached index built from the completed analysis report; it does not query `AssetDatabase`, reread curves, or rerun discovery. Recently used category/search results are cached as well.

Displayed results are grouped by clip asset identity, renderer/material target, and property. Clip identity is not derived from its name, so two separate assets with the same name remain separate. Unresolved renderer or material-slot relationships remain visible when detector metadata provides a probable match, and the UI labels the uncertainty explicitly.

Clip and target binding details are built lazily when their foldouts are expanded. Recognised UV entries retain feature name, UV channel, row, column, UDIM number, raw property name, extracted values or range, keyframes, source controller/state, target references, confidence, evidence, and diagnostics. Per-material animation foldouts use the same cached filter result.

Complementary highlighting is intentionally narrow. When different clips assign constant `0`/`1` or `-1`/`1` values to the same detector feature, material slot, and tile, the UI reports the opposite-value evidence. It does not infer semantic clip names, runtime toggle relationships, or which value means enabled.

## Limitations

The scanner reports authored clips and controller assets that are present at scan time. It does not build avatars, invoke VRCFury or VRChat hooks, unlock shaders, or reconstruct generated controller behavior. A future adapter can inspect a separately built avatar without changing this read-only scan path.

Clip names and values can suggest a relationship, but they do not prove runtime behavior. The scanner records constant settings, transitions, and swaps from keyframes and controller sources without assigning avatar-specific meanings.

The filtered views show material-related bindings that correlate with at least one scanned material slot. Raw project curves with no material correlation remain represented by the overall discovered binding statistics but are not displayed as matched material properties.
