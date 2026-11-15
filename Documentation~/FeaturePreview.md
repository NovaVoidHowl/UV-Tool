# Feature visibility preview

Version 1.7 adds a disposable Editor preview for the feature ownership and visibility states defined by schema-v1 YAML.

## Starting a preview

The **Feature visibility preview** panel is available even when no scanner report is open. Assign the converted avatar prefab or scene object, its YAML configuration, and its generated conversion manifest JSON, then select **Start feature preview**.

The tool validates both documents, checks the manifest's renderer/submesh mappings against the selected converted avatar, creates a disposable scene clone, captures complete copies of every mapped renderer's original `sharedMaterials` array, and applies each feature's YAML `defaultState`. Starting immediately after conversion is quicker because the output prefab and manifest fields are filled automatically.

Toggle features with exactly two states use a UI Toolkit toggle. The checked state is the state exposing more owned groups; ties use the configured default as checked. Exclusive features, and toggles with any other number of states, use a named-state dropdown. State IDs and `visibleGroups` always come from YAML rather than animation or naming inference.

## Visibility application

The manifest resolves each geometry group ID to a converted renderer path and output submesh/material-slot index. An owned group absent from the active state's `visibleGroups` receives the packaged **UV Tile Scanner NoRender** material in that output material slot. Visible and unowned groups receive the original material stored when the session started.

Every change begins with fresh clones of the complete stored material arrays and then applies the current state of every feature. Changes never accumulate from the renderer's current array, so switching several features cannot lose an original reference or leave a stale hidden slot.

- **Reset to YAML defaults** resets every active state and recomputes all slots.
- **Restore all materials** immediately writes the captured arrays back. Active states remain selected; changing a state reapplies the whole preview.
- **End preview** restores the captured arrays and destroys the disposable clone.

Changing any selected input or closing/reloading the window also ends the current preview. Manually deleting the clone is tolerated by cleanup.

When the selected converted avatar is already a scene object, its preview clone is placed to the right of the source avatar so the untouched source cannot visually cover hidden geometry. Prefab-asset previews retain the prefab's authored transform. Preview clones use `DontSaveInEditor` and are not included when the scene is saved.

## Safety and diagnostics

The selected prefab or scene object is never modified. Only its clone receives temporary `sharedMaterials` arrays. Meshes, source FBXs, prefab assets, material assets, UVs, skinning, bones, blendshapes, animations, and the conversion manifest remain unchanged.

Preview start is blocked for malformed documents, unsupported manifest/schema versions, missing renderers or meshes, invalid output submesh/material indices, conflicting mappings, invalid feature states, conflicting feature ownership, a missing NoRender asset, or a Scriptable Render Pipeline. Missing group mappings, renderer-type mismatches, null original materials, and materials whose shader is unsupported by the current Editor are reported as warnings; usable mappings remain previewable, and unrelated slots remain untouched. The swap itself is material-type agnostic and never converts or edits the original material.

## NoRender shader limitations

`Hidden/NVH/UVTileScanner/NoRender` is intentionally limited to the Built-in Render Pipeline. Its only pass uses `ColorMask 0`, `ZWrite Off`, unconditional fragment discard, no `ShadowCaster` pass, `ForceNoShadowCasting=True`, and `Fallback Off`. The supplied material is used by reference and is never modified.

This prevents normal supported Built-in colour, depth, and shadow output. It is not a universal renderer-disable mechanism: custom camera replacement shaders, custom command buffers that override materials, Scene view selection outlines or wireframes, and SRP renderer features can still visualise or process geometry. URP and HDRP are rejected rather than presented as supported. The preview does not split renderers and does not create ProtoFlux, ResoniteLink data, or converted materials.
