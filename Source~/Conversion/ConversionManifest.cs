using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nvh.UvTileScanner.Conversion
{
    [Serializable]
    public sealed class GeometryConversionManifest
    {
        public int manifestVersion;
        public int configurationSchemaVersion;
        public string sourceAvatarName;
        public string configuredAvatarName;
        public string sourceAvatarAssetPath;
        public string sourceAvatarGuid;
        public string outputPrefabAssetPath;
        public string outputPrefabGuid;
        public string fallbackPrefabAssetPath;
        public string fallbackPrefabGuid;
        public string[] fallbackMaterialAssetPaths = Array.Empty<string>();
        public ManifestRenderer[] renderers = Array.Empty<ManifestRenderer>();
        public ManifestFeature[] features = Array.Empty<ManifestFeature>();
        public ManifestDiagnostic[] diagnostics = Array.Empty<ManifestDiagnostic>();
    }

    [Serializable]
    public sealed class ManifestRenderer
    {
        public string rendererPath;
        public string rendererType;
        public string sourceMeshName;
        public string sourceMeshAssetPath;
        public string sourceMeshGuid;
        public string sourceMeshLocalId;
        public string outputMeshAssetPath;
        public string outputMeshGuid;
        public int sourceTriangleCount;
        public int unassignedTriangleCount;
        public ManifestSubMeshMapping[] subMeshMappings = Array.Empty<ManifestSubMeshMapping>();
    }

    [Serializable]
    public sealed class ManifestSubMeshMapping
    {
        public int outputSubMesh;
        public int sourceSubMesh;
        public int sourceMaterialSlot;
        public string geometryGroup;
        public bool unassigned;
        public int triangleCount;
    }

    [Serializable]
    public sealed class ManifestFeature
    {
        public string id;
        public string type;
        public string defaultState;
        public string[] ownedGroups = Array.Empty<string>();
        public ManifestFeatureState[] states = Array.Empty<ManifestFeatureState>();
    }

    [Serializable]
    public sealed class ManifestFeatureState
    {
        public string id;
        public string[] visibleGroups = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ManifestDiagnostic
    {
        public string severity;
        public string code;
        public string message;
    }

    public sealed class ManifestParseResult
    {
        internal ManifestParseResult(GeometryConversionManifest manifest,
            List<ConversionDiagnostic> diagnostics)
        {
            Manifest = manifest;
            Diagnostics = diagnostics.AsReadOnly();
        }

        public GeometryConversionManifest Manifest { get; }
        public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
        public bool Success
        {
            get
            {
                if (Manifest == null) return false;
                foreach (ConversionDiagnostic diagnostic in Diagnostics)
                    if (diagnostic.Severity == ConversionDiagnosticSeverity.Error) return false;
                return true;
            }
        }
    }

    public static class GeometryConversionManifestJson
    {
        public static string Serialize(GeometryConversionManifest manifest, bool prettyPrint = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        public static ManifestParseResult Parse(string json)
        {
            var diagnostics = new List<ConversionDiagnostic>();
            if (string.IsNullOrWhiteSpace(json))
            {
                diagnostics.Add(Error("MANIFEST_EMPTY", "The conversion manifest is empty."));
                return new ManifestParseResult(null, diagnostics);
            }

            GeometryConversionManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<GeometryConversionManifest>(json);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("MANIFEST_JSON_INVALID", exception.Message));
                return new ManifestParseResult(null, diagnostics);
            }
            if (manifest == null)
            {
                diagnostics.Add(Error("MANIFEST_JSON_INVALID", "Unity could not deserialize the conversion manifest."));
                return new ManifestParseResult(null, diagnostics);
            }
            if (manifest.manifestVersion != GeometryConversionOutput.ManifestVersion)
                diagnostics.Add(Error("MANIFEST_VERSION_UNSUPPORTED",
                    $"Manifest version {manifest.manifestVersion} is not supported; expected {GeometryConversionOutput.ManifestVersion}."));
            if (manifest.configurationSchemaVersion != GeometryConfigurationYaml.SupportedSchemaVersion)
                diagnostics.Add(Error("MANIFEST_SCHEMA_UNSUPPORTED",
                    $"Configuration schema version {manifest.configurationSchemaVersion} is not supported."));
            if (manifest.renderers == null)
                diagnostics.Add(Error("MANIFEST_RENDERERS_MISSING", "The manifest has no renderer mapping array."));
            if (manifest.features == null)
                diagnostics.Add(Error("MANIFEST_FEATURES_MISSING", "The manifest has no feature definition array."));

            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestRenderer renderer in manifest.renderers ?? Array.Empty<ManifestRenderer>())
            {
                if (renderer == null || renderer.rendererPath == null)
                {
                    diagnostics.Add(Error("MANIFEST_RENDERER_INVALID", "A manifest renderer has a null relative path."));
                    continue;
                }
                if (renderer.subMeshMappings == null)
                {
                    diagnostics.Add(Error("MANIFEST_MAPPINGS_MISSING",
                        $"Renderer '{renderer.rendererPath}' has no submesh mapping array."));
                    continue;
                }
                foreach (ManifestSubMeshMapping mapping in renderer.subMeshMappings)
                {
                    if (mapping == null || mapping.outputSubMesh < 0)
                    {
                        diagnostics.Add(Error("MANIFEST_MAPPING_INVALID",
                            $"Renderer '{renderer.rendererPath}' contains an invalid output submesh mapping."));
                        continue;
                    }
                    string slotKey = renderer.rendererPath + "|" + mapping.outputSubMesh;
                    if (!slots.Add(slotKey))
                        diagnostics.Add(Error("MANIFEST_MAPPING_DUPLICATE",
                            $"Renderer '{renderer.rendererPath}' maps output submesh {mapping.outputSubMesh} more than once."));
                    if (!mapping.unassigned && string.IsNullOrWhiteSpace(mapping.geometryGroup))
                        diagnostics.Add(Error("MANIFEST_GROUP_MISSING",
                            $"Renderer '{renderer.rendererPath}' output submesh {mapping.outputSubMesh} has no geometry group ID."));
                }
            }

            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestFeature feature in manifest.features ?? Array.Empty<ManifestFeature>())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.id) || !featureIds.Add(feature.id))
                    diagnostics.Add(Error("MANIFEST_FEATURE_INVALID", "A manifest feature ID is empty or duplicated."));
            }
            return new ManifestParseResult(manifest, diagnostics);
        }

        private static ConversionDiagnostic Error(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Error, code, message);
    }
}
