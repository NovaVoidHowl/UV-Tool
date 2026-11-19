# Feature visibility preview

Version 1.7 adds a disposable Editor preview for the feature ownership and visibility states defined by schema-v1 YAML.

## Starting a preview

The **Feature visibility preview** panel is available even when no scanner report is open. Assign the converted avatar prefab or scene object, its YAML configuration, and its generated conversion manifest JSON, then select **Start feature preview**.

The tool validates both documents, checks the manifest's renderer/submesh mappings against the selected converted avatar, creates a disposable scene clone, captures complete copies of every mapped renderer's original `sharedMaterials` array, and applies each feature's YAML `defaultState`. Starting immediately after conversion is quicker because the output prefab and manifest fields are filled automatically.

Toggle features with exactly two states use a UI Toolkit toggle. The checked state is the state exposing more owned groups; ties use the configured default as checked. Exclusive features, and toggles with any other number of states, use a named-state dropdown. State IDs and `visibleGroups` always come from YAML rather than animation or naming inference.

## Visibility application

The manifest resolves each geometry group ID to a converted renderer path and output submesh/material-slot index. An owned group absent from the active state's `visibleGroups` receives the packaged **UV Tile Scanner NoRender** material in that output material slot. Visible and unowned geometry receives the selected preview-mode material. Original mode uses the material stored when the session started.

Every change begins with fresh clones of the complete stored material arrays and then applies the current state of every feature. Changes never accumulate from the renderer's current array, so switching several features cannot lose an original reference or leave a stale hidden slot.

- **Reset to YAML defaults** resets every active state and recomputes all slots.
- **Restore all materials** immediately writes the captured arrays back. Active states remain selected; changing a state reapplies the whole preview.
- **End preview** destroys the disposable clone and releases generated materials.

Changing any selected input or closing/reloading the window also ends the current preview. Manually deleting the clone is tolerated by cleanup.

When the selected converted avatar is already a scene object, its preview clone is placed to the right of the source avatar so the untouched source cannot visually cover hidden geometry. Prefab-asset previews retain the prefab's authored transform. Preview clones use `DontSaveInEditor` and are not included when the scene is saved.

## Safety and diagnostics

The selected prefab or scene object is never modified. Only its clone receives temporary `sharedMaterials` arrays. Meshes, source FBXs, prefab assets, material assets, UVs, skinning, bones, blendshapes, animations, and the conversion manifest remain unchanged.

Preview start is blocked for malformed documents, unsupported manifest/schema versions, missing renderers or meshes, invalid output submesh/material indices, conflicting mappings, invalid feature states, conflicting feature ownership, a missing NoRender asset, or a Scriptable Render Pipeline. Missing group mappings, renderer-type mismatches, null original materials, and materials whose shader is unsupported by the current Editor are reported as warnings; usable mappings remain previewable, and unrelated slots remain visible using the selected mode. Original material assets are never edited.

## NoRender shader limitations

`Hidden/NVH/UVTileScanner/NoRender` is intentionally limited to the Built-in Render Pipeline. Its only pass uses `ColorMask 0`, `ZWrite Off`, unconditional fragment discard, no `ShadowCaster` pass, `ForceNoShadowCasting=True`, and `Fallback Off`. The supplied material is used by reference and is never modified.

This prevents normal supported Built-in colour, depth, and shadow output. It is not a universal renderer-disable mechanism: custom camera replacement shaders, custom command buffers that override materials, Scene view selection outlines or wireframes, and SRP renderer features can still visualise or process geometry. URP and HDRP are rejected rather than presented as supported. The preview does not split renderers and does not create ProtoFlux, ResoniteLink data, or persistent converted materials.

## Preview material modes (1.8)

The material selector defaults to **Fallback**. Switching modes reapplies current YAML states without resetting toggles or exclusive selections. NoRender always takes precedence for hidden groups.

- **Original** uses converted prefab material references. Shader-level UV discard/dissolve may still hide geometry marked visible by YAML.
- **Fallback** uses temporary Built-in Standard materials, shared by source material reference. Missing source materials or sources with no reliable base properties produce a named magenta fallback and a warning.
- **Diagnostic** uses untextured Unlit/Color materials, cached by geometry-group ID. Stable ordinal identifiers are hashed with an avalanche step to separate similar IDs; colours remain consistent across sessions. The expandable legend shows colour, group, renderer, original material slot, YAML UDIM tiles (or unknown), and manifest output submesh. Unmapped/pass-through geometry is neutral grey and stays visible.

Fallback translation validates property presence and type before reading. Supported mappings are `_MainTex`/`_BaseMap` (including tiling and offset), `_Color`/`_BaseColor` (including alpha), `_BumpMap`, `_BumpScale`, `_Metallic`, `_MetallicGlossMap`, `_Glossiness`/`_Smoothness`, `_EmissionColor`, and `_EmissionMap`. Normal and metallic keywords follow their mapped textures. Emission preserves the source material’s GI flags and normalizes them with `MaterialEditor.FixupEmissiveFlag`; `_EMISSION` follows `EmissiveIsBlack`, as Standard validation requires. Stored emission maps/colours alone do not enable a disabled emission effect. A warning explains omitted custom/animated enablement and masks. Standard `_Mode` and `_Cutoff` preserve opaque, cutout, fade and premultiplied transparency with corresponding blend/depth settings. Custom shader transparency modes are deliberately not inferred; those materials default to opaque.

Locked shaders expose only what remains in their shader property table. Missing/baked values cannot be recovered. Non-Standard sources receive a translation warning; missing optional properties use Standard defaults. Matcaps, outlines, hue shifts, custom lighting, UV discard/dissolve, packed custom maps and other advanced properties are not translated. This is a geometry verification tool rather than a full appearance converter. Hash colours provide practical distinction, not a guarantee of perceptual separation for arbitrary group sets or colour vision differences.

Generated materials use `HideAndDontSave`, never AssetDatabase writes. The session releases them on End Preview, failed startup/mode changes, window disable, assembly reload and Editor quit. A new session instantiates a fresh clone. No source/prefab materials or mesh assets are changed. Original restores remain available as an explicit debugging action on the disposable instance.

### Validation in this checkout

Synthetic Edit Mode tests cover all three modes, shared sources, deterministic colours, conservative/missing shader properties, null materials, Standard appearance and cutout translation, remapped manifest ordering, toggles and exclusive states, unowned geometry, mode/state persistence, reuse, explicit/window cleanup and prefab/mesh/source non-mutation. Assembly-reload cleanup uses the same tested EndPreview method; an actual domain reload and visual appearance require Unity validation.

### Development tracing

Enable **NVH → UV Tile Scanner → Development → Trace Preview Material Events** only when investigating rendering. This session-only flag survives assembly reloads and defaults off in a fresh Editor session. It logs `OnPostprocessAllAssets` (including paths and domain-reload flag), `projectChanged`, assembly reloads, preview assignment and cleanup. Full material JSON, keywords, GI flags, texture IDs/paths and ordered renderer assignments go to `Library/UVTool/PreviewMaterialTrace.log`; the Console receives event summaries. Logging never imports assets or changes materials. Disable the menu option after capture. See [the rendering investigation](PreviewRenderingInvestigation.md) for the verified emission-initialization fix and remaining limits.
