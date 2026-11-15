using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nvh.UvTileScanner.Conversion
{
    public sealed class FeaturePreviewStartResult
    {
        internal FeaturePreviewStartResult(FeatureVisibilityPreviewSession session,
            List<ConversionDiagnostic> diagnostics)
        {
            Session = session;
            Diagnostics = diagnostics.AsReadOnly();
        }

        public FeatureVisibilityPreviewSession Session { get; }
        public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
        public bool Success => Session != null;
    }

    /// <summary>
    /// Owns a disposable scene clone of a converted avatar. Every visibility update starts from
    /// complete snapshots of the clone's original shared-material arrays.
    /// </summary>
    public sealed class FeatureVisibilityPreviewSession : IDisposable
    {
        private readonly AvatarGeometryConfiguration _configuration;
        private readonly Material _noRenderMaterial;
        private readonly Dictionary<Renderer, Material[]> _originalMaterials;
        private readonly Dictionary<string, List<PreviewSlot>> _slotsByGroup;
        private readonly Dictionary<string, string> _activeStates;
        private bool _disposed;

        private FeatureVisibilityPreviewSession(GameObject previewInstance,
            AvatarGeometryConfiguration configuration, Material noRenderMaterial,
            Dictionary<Renderer, Material[]> originalMaterials,
            Dictionary<string, List<PreviewSlot>> slotsByGroup,
            Dictionary<string, string> activeStates)
        {
            PreviewInstance = previewInstance;
            _configuration = configuration;
            _noRenderMaterial = noRenderMaterial;
            _originalMaterials = originalMaterials;
            _slotsByGroup = slotsByGroup;
            _activeStates = activeStates;
        }

        public GameObject PreviewInstance { get; private set; }
        public bool IsActive => !_disposed && PreviewInstance != null;
        public IReadOnlyList<FeatureConfiguration> Features => _configuration.Features.AsReadOnly();

        public static FeaturePreviewStartResult Start(GameObject convertedAvatar,
            AvatarGeometryConfiguration configuration, GeometryConversionManifest manifest,
            Material noRenderMaterial)
        {
            var diagnostics = new List<ConversionDiagnostic>();
            if (convertedAvatar == null)
                diagnostics.Add(Error("PREVIEW_AVATAR_MISSING", "Select a converted avatar prefab or scene object."));
            if (configuration == null)
                diagnostics.Add(Error("PREVIEW_CONFIG_MISSING", "A parsed YAML configuration is required."));
            if (manifest == null)
                diagnostics.Add(Error("PREVIEW_MANIFEST_MISSING", "A parsed conversion manifest is required."));
            if (noRenderMaterial == null || noRenderMaterial.shader == null)
                diagnostics.Add(Error("PREVIEW_NORENDER_MISSING", "The packaged NoRender material or shader could not be loaded."));
            else if (!string.Equals(noRenderMaterial.shader.name,
                         "Hidden/NVH/UVTileScanner/NoRender", StringComparison.Ordinal))
                diagnostics.Add(Error("PREVIEW_NORENDER_INVALID",
                    "The supplied preview material does not use the UV Tile Scanner NoRender shader."));
            if (GraphicsSettings.currentRenderPipeline != null)
                diagnostics.Add(Error("PREVIEW_PIPELINE_UNSUPPORTED",
                    "Feature visibility preview currently supports only the Built-in Render Pipeline."));
            if (HasErrors(diagnostics))
                return new FeaturePreviewStartResult(null, diagnostics);

            GameObject instance = null;
            try
            {
                instance = InstantiatePreview(convertedAvatar);
                instance.name = convertedAvatar.name + " [UV Feature Preview]";
                instance.hideFlags |= HideFlags.DontSaveInEditor;
                if (!EditorUtility.IsPersistent(convertedAvatar))
                {
                    float offset = CalculateScenePreviewOffset(convertedAvatar);
                    instance.transform.position += Vector3.right * offset;
                    diagnostics.Add(Info("PREVIEW_SCENE_CLONE_OFFSET",
                        $"The disposable preview clone was moved {offset:0.##} world units to the right " +
                        "so the untouched source avatar does not obscure hidden geometry."));
                }
                var originals = new Dictionary<Renderer, Material[]>();
                var groups = ResolveMappings(instance, manifest, originals, diagnostics);
                ValidateFeatureDefinitions(configuration, manifest, groups, diagnostics);
                var activeStates = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (FeatureConfiguration feature in configuration.Features ?? new List<FeatureConfiguration>())
                {
                    FeatureStateConfiguration state = FindState(feature, feature.DefaultState);
                    if (state == null)
                    {
                        diagnostics.Add(Error("PREVIEW_DEFAULT_STATE_INVALID",
                            $"Feature '{feature.Id}' default state '{feature.DefaultState}' does not exist."));
                        continue;
                    }
                    activeStates[feature.Id] = state.Id;
                }
                if (HasErrors(diagnostics))
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    return new FeaturePreviewStartResult(null, diagnostics);
                }

                var session = new FeatureVisibilityPreviewSession(instance, configuration,
                    noRenderMaterial, originals, groups, activeStates);
                session.ApplyCurrentStates();
                return new FeaturePreviewStartResult(session, diagnostics);
            }
            catch (Exception exception)
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                diagnostics.Add(Error("PREVIEW_START_FAILED", exception.Message));
                return new FeaturePreviewStartResult(null, diagnostics);
            }
        }

        public string GetActiveState(string featureId)
        {
            ThrowIfDisposed();
            return _activeStates.TryGetValue(featureId ?? string.Empty, out string state) ? state : null;
        }

        public bool SetFeatureState(string featureId, string stateId)
        {
            ThrowIfDisposed();
            FeatureConfiguration feature = FindFeature(featureId);
            if (feature == null || FindState(feature, stateId) == null) return false;
            _activeStates[feature.Id] = stateId;
            ApplyCurrentStates();
            return true;
        }

        public bool IsGroupVisible(string groupId)
        {
            ThrowIfDisposed();
            return !GetHiddenGroups().Contains(groupId ?? string.Empty);
        }

        public void ResetToDefaults()
        {
            ThrowIfDisposed();
            _activeStates.Clear();
            foreach (FeatureConfiguration feature in _configuration.Features)
                _activeStates[feature.Id] = feature.DefaultState;
            ApplyCurrentStates();
        }

        public void RestoreAllMaterials()
        {
            ThrowIfDisposed();
            foreach (KeyValuePair<Renderer, Material[]> entry in _originalMaterials)
                if (entry.Key != null) entry.Key.sharedMaterials = Clone(entry.Value);
        }

        public void ApplyCurrentStates()
        {
            ThrowIfDisposed();
            var recomputed = new Dictionary<Renderer, Material[]>();
            foreach (KeyValuePair<Renderer, Material[]> entry in _originalMaterials)
                if (entry.Key != null) recomputed.Add(entry.Key, Clone(entry.Value));

            HashSet<string> hiddenGroups = GetHiddenGroups();
            foreach (string group in hiddenGroups)
            {
                if (!_slotsByGroup.TryGetValue(group, out List<PreviewSlot> slots)) continue;
                foreach (PreviewSlot slot in slots)
                    if (slot.Renderer != null && recomputed.TryGetValue(slot.Renderer, out Material[] materials) &&
                        slot.MaterialSlot >= 0 && slot.MaterialSlot < materials.Length)
                        materials[slot.MaterialSlot] = _noRenderMaterial;
            }
            foreach (KeyValuePair<Renderer, Material[]> entry in recomputed)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
        }

        public void EndPreview()
        {
            if (_disposed) return;
            foreach (KeyValuePair<Renderer, Material[]> entry in _originalMaterials)
                if (entry.Key != null) entry.Key.sharedMaterials = Clone(entry.Value);
            if (PreviewInstance != null) UnityEngine.Object.DestroyImmediate(PreviewInstance);
            PreviewInstance = null;
            _disposed = true;
        }

        public void Dispose() => EndPreview();

        private HashSet<string> GetHiddenGroups()
        {
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeatureConfiguration feature in _configuration.Features)
            {
                if (!_activeStates.TryGetValue(feature.Id, out string stateId)) continue;
                FeatureStateConfiguration state = FindState(feature, stateId);
                if (state == null) continue;
                var visible = new HashSet<string>(state.VisibleGroups ?? new List<string>(), StringComparer.Ordinal);
                foreach (string ownedGroup in feature.OwnedGroups ?? new List<string>())
                    if (!visible.Contains(ownedGroup)) hidden.Add(ownedGroup);
            }
            return hidden;
        }

        private FeatureConfiguration FindFeature(string featureId)
        {
            foreach (FeatureConfiguration feature in _configuration.Features)
                if (string.Equals(feature.Id, featureId, StringComparison.Ordinal)) return feature;
            return null;
        }

        private static FeatureStateConfiguration FindState(FeatureConfiguration feature, string stateId)
        {
            foreach (FeatureStateConfiguration state in feature.States ?? new List<FeatureStateConfiguration>())
                if (string.Equals(state.Id, stateId, StringComparison.Ordinal)) return state;
            return null;
        }

        private static GameObject InstantiatePreview(GameObject source)
        {
            if (EditorUtility.IsPersistent(source) && PrefabUtility.IsPartOfPrefabAsset(source))
            {
                GameObject prefabInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (prefabInstance != null) return prefabInstance;
            }
            return UnityEngine.Object.Instantiate(source);
        }

        private static float CalculateScenePreviewOffset(GameObject source)
        {
            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else combined.Encapsulate(renderer.bounds);
            }
            return Mathf.Max(hasBounds ? combined.size.x : 1f, 1f) + 0.5f;
        }

        private static Dictionary<string, List<PreviewSlot>> ResolveMappings(GameObject instance,
            GeometryConversionManifest manifest, Dictionary<Renderer, Material[]> originals,
            List<ConversionDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, List<PreviewSlot>>(StringComparer.Ordinal);
            var occupiedSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestRenderer rendererMapping in manifest.renderers ?? Array.Empty<ManifestRenderer>())
            {
                if (rendererMapping == null) continue;
                Renderer renderer = ResolveRenderer(instance, rendererMapping.rendererPath);
                if (renderer == null)
                {
                    diagnostics.Add(Error("PREVIEW_RENDERER_MISSING",
                        $"Converted preview has no renderer at '{DisplayPath(rendererMapping.rendererPath)}'."));
                    continue;
                }
                string actualRendererType = renderer.GetType().Name;
                if (!string.IsNullOrWhiteSpace(rendererMapping.rendererType) &&
                    !string.Equals(rendererMapping.rendererType, actualRendererType, StringComparison.Ordinal))
                    diagnostics.Add(Warning("PREVIEW_RENDERER_TYPE_MISMATCH",
                        $"Manifest renderer '{DisplayPath(rendererMapping.rendererPath)}' is recorded as " +
                        $"'{rendererMapping.rendererType}', but the preview contains '{actualRendererType}'."));
                if (!originals.ContainsKey(renderer)) originals.Add(renderer, Clone(renderer.sharedMaterials));
                Mesh mesh = GetMesh(renderer);
                if (mesh == null)
                {
                    diagnostics.Add(Error("PREVIEW_MESH_MISSING",
                        $"Renderer '{DisplayPath(rendererMapping.rendererPath)}' has no mesh."));
                    continue;
                }
                Material[] materials = originals[renderer];
                foreach (ManifestSubMeshMapping mapping in rendererMapping.subMeshMappings ??
                         Array.Empty<ManifestSubMeshMapping>())
                {
                    if (mapping == null || mapping.unassigned || string.IsNullOrEmpty(mapping.geometryGroup)) continue;
                    if (mapping.outputSubMesh < 0 || mapping.outputSubMesh >= mesh.subMeshCount ||
                        mapping.outputSubMesh >= materials.Length)
                    {
                        diagnostics.Add(Error("PREVIEW_SUBMESH_INVALID",
                            $"Group '{mapping.geometryGroup}' maps to output submesh/material slot " +
                            $"{mapping.outputSubMesh} on '{DisplayPath(rendererMapping.rendererPath)}', " +
                            $"but the preview has {mesh.subMeshCount} submeshes and {materials.Length} materials."));
                        continue;
                    }
                    string slotKey = rendererMapping.rendererPath + "|" + mapping.outputSubMesh;
                    if (!occupiedSlots.Add(slotKey))
                    {
                        diagnostics.Add(Error("PREVIEW_SLOT_CONFLICT",
                            $"Multiple manifest groups map to '{DisplayPath(rendererMapping.rendererPath)}' " +
                            $"material slot {mapping.outputSubMesh}; that slot will not be previewed."));
                        RemoveSlot(result, renderer, mapping.outputSubMesh);
                        continue;
                    }
                    Material originalMaterial = materials[mapping.outputSubMesh];
                    if (originalMaterial == null)
                        diagnostics.Add(Warning("PREVIEW_MATERIAL_MISSING",
                            $"Group '{mapping.geometryGroup}' uses an unassigned material at " +
                            $"'{DisplayPath(rendererMapping.rendererPath)}' slot {mapping.outputSubMesh}; null will be restored when visible."));
                    else if (originalMaterial.shader == null || !originalMaterial.shader.isSupported)
                        diagnostics.Add(Warning("PREVIEW_MATERIAL_UNSUPPORTED",
                            $"Material '{originalMaterial.name}' at '{DisplayPath(rendererMapping.rendererPath)}' " +
                            $"slot {mapping.outputSubMesh} has no shader supported by the current Editor. " +
                            "It can still be hidden and its original reference will be restored, but its visible rendering may be unavailable."));
                    if (!result.TryGetValue(mapping.geometryGroup, out List<PreviewSlot> slots))
                    {
                        slots = new List<PreviewSlot>();
                        result.Add(mapping.geometryGroup, slots);
                    }
                    slots.Add(new PreviewSlot(renderer, mapping.outputSubMesh));
                }
            }
            return result;
        }

        private static void ValidateFeatureDefinitions(AvatarGeometryConfiguration configuration,
            GeometryConversionManifest manifest, Dictionary<string, List<PreviewSlot>> groups,
            List<ConversionDiagnostic> diagnostics)
        {
            var manifestFeatures = new Dictionary<string, ManifestFeature>(StringComparer.Ordinal);
            foreach (ManifestFeature feature in manifest.features ?? Array.Empty<ManifestFeature>())
                if (feature != null && !string.IsNullOrEmpty(feature.id) && !manifestFeatures.ContainsKey(feature.id))
                    manifestFeatures.Add(feature.id, feature);
            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            var groupOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            var configuredGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (GeometryGroupConfiguration group in configuration.GeometryGroups ??
                     new List<GeometryGroupConfiguration>())
                if (group != null && !string.IsNullOrWhiteSpace(group.Id)) configuredGroups.Add(group.Id);
            foreach (FeatureConfiguration feature in configuration.Features ?? new List<FeatureConfiguration>())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.Id) || !featureIds.Add(feature.Id))
                {
                    diagnostics.Add(Error("PREVIEW_FEATURE_INVALID", "A YAML feature ID is empty or duplicated."));
                    continue;
                }
                if (!manifestFeatures.TryGetValue(feature.Id, out ManifestFeature manifestFeature))
                    diagnostics.Add(Warning("PREVIEW_FEATURE_NOT_IN_MANIFEST",
                        $"YAML feature '{feature.Id}' is absent from the conversion manifest; YAML states will be used."));
                else
                {
                    string yamlType = feature.Type == GeometryFeatureType.Toggle ? "toggle" : "exclusive";
                    if (!string.Equals(yamlType, manifestFeature.type, StringComparison.Ordinal) ||
                        !string.Equals(feature.DefaultState, manifestFeature.defaultState, StringComparison.Ordinal) ||
                        !SetEquals(feature.OwnedGroups, manifestFeature.ownedGroups) ||
                        !StatesEqual(feature.States, manifestFeature.states))
                        diagnostics.Add(Warning("PREVIEW_FEATURE_MANIFEST_MISMATCH",
                            $"Feature '{feature.Id}' differs between YAML and manifest; YAML controls the preview state model."));
                }
                var ownedGroups = new HashSet<string>(StringComparer.Ordinal);
                foreach (string group in feature.OwnedGroups ?? new List<string>())
                {
                    if (!ownedGroups.Add(group))
                        diagnostics.Add(Error("PREVIEW_GROUP_OWNERSHIP_DUPLICATE",
                            $"Feature '{feature.Id}' owns group '{group}' more than once."));
                    if (!configuredGroups.Contains(group))
                        diagnostics.Add(Error("PREVIEW_GROUP_NOT_IN_CONFIG",
                            $"Feature '{feature.Id}' owns group '{group}', which is absent from YAML geometryGroups."));
                    if (groupOwners.TryGetValue(group, out string existingOwner) && existingOwner != feature.Id)
                        diagnostics.Add(Error("PREVIEW_GROUP_OWNERSHIP_CONFLICT",
                            $"Group '{group}' is owned by both '{existingOwner}' and '{feature.Id}'."));
                    else groupOwners[group] = feature.Id;
                    if (!groups.ContainsKey(group))
                        diagnostics.Add(Warning("PREVIEW_GROUP_MAPPING_MISSING",
                            $"Feature '{feature.Id}' owns group '{group}', but the manifest has no usable renderer/submesh mapping for it."));
                }
                var stateIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (FeatureStateConfiguration state in feature.States ?? new List<FeatureStateConfiguration>())
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.Id) || !stateIds.Add(state.Id))
                    {
                        diagnostics.Add(Error("PREVIEW_STATE_INVALID",
                            $"Feature '{feature.Id}' has an empty or duplicate state ID."));
                        continue;
                    }
                    var visibleGroups = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string group in state.VisibleGroups ?? new List<string>())
                    {
                        if (!visibleGroups.Add(group))
                            diagnostics.Add(Error("PREVIEW_VISIBLE_GROUP_DUPLICATE",
                                $"Feature '{feature.Id}' state '{state.Id}' lists group '{group}' more than once."));
                        if (!(feature.OwnedGroups ?? new List<string>()).Contains(group))
                            diagnostics.Add(Error("PREVIEW_STATE_GROUP_NOT_OWNED",
                                $"Feature '{feature.Id}' state '{state.Id}' exposes unowned group '{group}'."));
                    }
                }
                if (!stateIds.Contains(feature.DefaultState))
                    diagnostics.Add(Error("PREVIEW_DEFAULT_STATE_INVALID",
                        $"Feature '{feature.Id}' default state '{feature.DefaultState}' does not exist."));
            }
        }

        private static bool StatesEqual(List<FeatureStateConfiguration> yamlStates,
            ManifestFeatureState[] manifestStates)
        {
            yamlStates = yamlStates ?? new List<FeatureStateConfiguration>();
            manifestStates = manifestStates ?? Array.Empty<ManifestFeatureState>();
            if (yamlStates.Count != manifestStates.Length) return false;
            var byId = new Dictionary<string, ManifestFeatureState>(StringComparer.Ordinal);
            foreach (ManifestFeatureState state in manifestStates)
                if (state != null && !string.IsNullOrEmpty(state.id) && !byId.ContainsKey(state.id))
                    byId.Add(state.id, state);
            foreach (FeatureStateConfiguration state in yamlStates)
                if (state == null || !byId.TryGetValue(state.Id, out ManifestFeatureState manifestState) ||
                    !SetEquals(state.VisibleGroups, manifestState.visibleGroups)) return false;
            return true;
        }

        private static bool SetEquals(IEnumerable<string> first, IEnumerable<string> second)
        {
            var left = new HashSet<string>(first ?? Array.Empty<string>(), StringComparer.Ordinal);
            return left.SetEquals(second ?? Array.Empty<string>());
        }

        private static Renderer ResolveRenderer(GameObject root, string relativePath)
        {
            string path = (relativePath ?? string.Empty).Trim('/');
            Transform transform = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
            if (transform == null) return null;
            SkinnedMeshRenderer skinned = transform.GetComponent<SkinnedMeshRenderer>();
            return skinned != null ? skinned : transform.GetComponent<MeshRenderer>();
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static void RemoveSlot(Dictionary<string, List<PreviewSlot>> groups,
            Renderer renderer, int materialSlot)
        {
            foreach (List<PreviewSlot> slots in groups.Values)
                slots.RemoveAll(slot => slot.Renderer == renderer && slot.MaterialSlot == materialSlot);
        }

        private static Material[] Clone(Material[] materials) =>
            materials == null ? Array.Empty<Material>() : (Material[])materials.Clone();

        private static string DisplayPath(string path) => string.IsNullOrEmpty(path) ? "<avatar root>" : path;

        private static bool HasErrors(List<ConversionDiagnostic> diagnostics)
        {
            foreach (ConversionDiagnostic diagnostic in diagnostics)
                if (diagnostic.Severity == ConversionDiagnosticSeverity.Error) return true;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (!IsActive) throw new ObjectDisposedException(nameof(FeatureVisibilityPreviewSession));
        }

        private static ConversionDiagnostic Error(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Error, code, message);
        private static ConversionDiagnostic Warning(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Warning, code, message);
        private static ConversionDiagnostic Info(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Info, code, message);

        private sealed class PreviewSlot
        {
            internal PreviewSlot(Renderer renderer, int materialSlot)
            {
                Renderer = renderer;
                MaterialSlot = materialSlot;
            }
            internal Renderer Renderer { get; }
            internal int MaterialSlot { get; }
        }
    }
}
