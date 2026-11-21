using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nvh.UvTileScanner.Conversion
{
    public static class GeometryConversionOutput
    {
        public const int ManifestVersion = 1;

        public static GeometryConversionReport Convert(GameObject avatarRoot,
            AvatarGeometryConfiguration configuration, Nvh.UvTileScanner.AvatarUvScanReport scanReport = null,
            bool createFallbackMaterialVariant = false)
        {
            GeometryConversionReport planned = GeometryConversionPlanner.ValidateAndPlan(
                avatarRoot, configuration, scanReport);
            if (planned.HasErrors) return planned;

            var diagnostics = new List<ConversionDiagnostic>(planned.Diagnostics);
            var createdAssets = new List<string>();
            var meshPaths = new List<string>();
            var fallbackMaterialPaths = new List<string>();
            string fallbackPrefabPath = null;
            GameObject avatarCopy = null;
            try
            {
                string outputDirectory = NormalizeDirectory(configuration.Settings.OutputDirectory);
                EnsureAssetFolder(outputDirectory);
                avatarCopy = UnityEngine.Object.Instantiate(avatarRoot);
                avatarCopy.name = SanitizeName(configuration.Avatar.Name) + "_Converted";
                var manifestRenderers = new List<ManifestRenderer>();

                foreach (RendererConversionPlan plan in planned.Renderers)
                {
                    Mesh converted = BuildMesh(plan);
                    string meshPath = AssetDatabase.GenerateUniqueAssetPath(
                        outputDirectory + "/" + SanitizeName(plan.RendererPath) + "_Grouped.asset");
                    AssetDatabase.CreateAsset(converted, meshPath);
                    createdAssets.Add(meshPath);
                    meshPaths.Add(meshPath);
                    AssignConvertedMesh(avatarCopy, plan, converted);
                    manifestRenderers.Add(BuildRendererManifest(plan, meshPath));
                }

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                    outputDirectory + "/" + SanitizeName(configuration.Avatar.Name) + "_Converted.prefab");
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(avatarCopy, prefabPath);
                if (savedPrefab == null)
                    throw new InvalidOperationException("Unity could not save the converted avatar prefab at '" +
                                                        prefabPath + "'.");
                createdAssets.Add(prefabPath);
                AssetDatabase.SaveAssets();

                if (createFallbackMaterialVariant)
                    fallbackPrefabPath = CreateFallbackVariant(avatarCopy, configuration, outputDirectory,
                        createdAssets, fallbackMaterialPaths, diagnostics);

                GeometryConversionManifest manifest = BuildManifest(configuration, avatarRoot, prefabPath,
                    fallbackPrefabPath, fallbackMaterialPaths, manifestRenderers, diagnostics);
                string manifestPath = AssetDatabase.GenerateUniqueAssetPath(
                    outputDirectory + "/" + SanitizeName(configuration.Avatar.Name) + "_ConversionManifest.json");
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absoluteManifestPath = Path.GetFullPath(Path.Combine(projectRoot, manifestPath));
                File.WriteAllText(absoluteManifestPath, GeometryConversionManifestJson.Serialize(manifest));
                AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
                createdAssets.Add(manifestPath);

                var output = new GeometryConversionReport(configuration,
                    new List<RendererConversionPlan>(planned.Renderers), diagnostics)
                {
                    OutputPrefabPath = prefabPath,
                    FallbackPrefabPath = fallbackPrefabPath,
                    ManifestPath = manifestPath,
                    GeneratedMeshPaths = meshPaths.AsReadOnly(),
                    GeneratedFallbackMaterialPaths = fallbackMaterialPaths.AsReadOnly()
                };
                return output;
            }
            catch (Exception exception)
            {
                for (int index = createdAssets.Count - 1; index >= 0; index--)
                    AssetDatabase.DeleteAsset(createdAssets[index]);
                diagnostics.Add(new ConversionDiagnostic(ConversionDiagnosticSeverity.Error,
                    "OUTPUT_FAILED", exception.Message));
                return new GeometryConversionReport(configuration,
                    new List<RendererConversionPlan>(planned.Renderers), diagnostics);
            }
            finally
            {
                if (avatarCopy != null) UnityEngine.Object.DestroyImmediate(avatarCopy);
            }
        }

        public static Mesh BuildMesh(RendererConversionPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Mesh clone = UnityEngine.Object.Instantiate(plan.SourceMesh);
            clone.name = plan.SourceMesh.name + " (UV Tile Groups)";
            Bounds originalBounds = plan.SourceMesh.bounds;
            clone.subMeshCount = plan.OutputSubMeshes.Count;
            foreach (OutputSubMeshPlan output in plan.OutputSubMeshes)
                clone.SetIndices(new List<int>(output.Indices), MeshTopology.Triangles,
                    output.OutputSubMesh, false);
            clone.bounds = originalBounds;
            return clone;
        }

        private static void AssignConvertedMesh(GameObject copyRoot, RendererConversionPlan plan, Mesh mesh)
        {
            Transform target = string.IsNullOrEmpty(plan.RendererPath)
                ? copyRoot.transform : copyRoot.transform.Find(plan.RendererPath);
            if (target == null) throw new InvalidOperationException(
                "Converted avatar is missing renderer path '" + plan.RendererPath + "'.");
            Renderer renderer;
            SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                skinned.sharedMesh = mesh;
                renderer = skinned;
            }
            else
            {
                MeshFilter filter = target.GetComponent<MeshFilter>();
                renderer = target.GetComponent<MeshRenderer>();
                if (filter == null || renderer == null)
                    throw new InvalidOperationException("Converted renderer components are missing at '" + plan.RendererPath + "'.");
                filter.sharedMesh = mesh;
            }
            Material[] sourceMaterials = plan.Renderer.sharedMaterials;
            var outputMaterials = new Material[plan.OutputSubMeshes.Count];
            for (int index = 0; index < outputMaterials.Length; index++)
            {
                int sourceSlot = plan.OutputSubMeshes[index].SourceMaterialSlot;
                outputMaterials[index] = sourceSlot >= 0 && sourceSlot < sourceMaterials.Length
                    ? sourceMaterials[sourceSlot] : null;
            }
            renderer.sharedMaterials = outputMaterials;
        }

        private static ManifestRenderer BuildRendererManifest(RendererConversionPlan plan, string meshPath)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(plan.SourceMesh,
                out string sourceGuid, out long sourceLocalId);
            var mappings = new List<ManifestSubMeshMapping>();
            foreach (OutputSubMeshPlan output in plan.OutputSubMeshes)
            {
                mappings.Add(new ManifestSubMeshMapping
                {
                    outputSubMesh = output.OutputSubMesh,
                    sourceSubMesh = output.SourceSubMesh,
                    sourceMaterialSlot = output.SourceMaterialSlot,
                    geometryGroup = output.GroupId ?? string.Empty,
                    unassigned = output.IsUnassigned,
                    triangleCount = output.TriangleCount
                });
            }
            return new ManifestRenderer
            {
                rendererPath = plan.RendererPath,
                rendererType = plan.Renderer.GetType().Name,
                sourceMeshName = plan.SourceMesh.name,
                sourceMeshAssetPath = AssetDatabase.GetAssetPath(plan.SourceMesh),
                sourceMeshGuid = sourceGuid ?? string.Empty,
                sourceMeshLocalId = sourceLocalId.ToString(),
                outputMeshAssetPath = meshPath,
                outputMeshGuid = AssetDatabase.AssetPathToGUID(meshPath),
                sourceTriangleCount = plan.TriangleCount,
                unassignedTriangleCount = plan.UnassignedTriangleCount,
                subMeshMappings = mappings.ToArray()
            };
        }

        private static GeometryConversionManifest BuildManifest(AvatarGeometryConfiguration configuration,
            GameObject sourceRoot, string prefabPath, string fallbackPrefabPath,
            List<string> fallbackMaterialPaths, List<ManifestRenderer> renderers,
            List<ConversionDiagnostic> diagnostics)
        {
            var features = new List<ManifestFeature>();
            foreach (FeatureConfiguration feature in configuration.Features)
            {
                var states = new List<ManifestFeatureState>();
                foreach (FeatureStateConfiguration state in feature.States)
                    states.Add(new ManifestFeatureState
                    {
                        id = state.Id,
                        visibleGroups = state.VisibleGroups.ToArray()
                    });
                features.Add(new ManifestFeature
                {
                    id = feature.Id,
                    type = feature.Type == GeometryFeatureType.Toggle ? "toggle" : "exclusive",
                    defaultState = feature.DefaultState,
                    ownedGroups = feature.OwnedGroups.ToArray(),
                    states = states.ToArray()
                });
            }
            var manifestDiagnostics = new List<ManifestDiagnostic>();
            foreach (ConversionDiagnostic diagnostic in diagnostics)
                manifestDiagnostics.Add(new ManifestDiagnostic
                {
                    severity = diagnostic.Severity.ToString(),
                    code = diagnostic.Code,
                    message = diagnostic.Message
                });
            string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceRoot);
            return new GeometryConversionManifest
            {
                manifestVersion = ManifestVersion,
                configurationSchemaVersion = configuration.SchemaVersion,
                sourceAvatarName = sourceRoot.name,
                configuredAvatarName = configuration.Avatar.Name,
                sourceAvatarAssetPath = sourcePath ?? string.Empty,
                sourceAvatarGuid = string.IsNullOrEmpty(sourcePath) ? string.Empty : AssetDatabase.AssetPathToGUID(sourcePath),
                outputPrefabAssetPath = prefabPath,
                outputPrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                fallbackPrefabAssetPath = fallbackPrefabPath ?? string.Empty,
                fallbackPrefabGuid = string.IsNullOrEmpty(fallbackPrefabPath)
                    ? string.Empty : AssetDatabase.AssetPathToGUID(fallbackPrefabPath),
                fallbackMaterialAssetPaths = fallbackMaterialPaths.ToArray(),
                renderers = renderers.ToArray(),
                features = features.ToArray(),
                diagnostics = manifestDiagnostics.ToArray()
            };
        }

        private static string CreateFallbackVariant(GameObject avatarCopy,
            AvatarGeometryConfiguration configuration, string outputDirectory,
            List<string> createdAssets, List<string> materialPaths,
            List<ConversionDiagnostic> diagnostics)
        {
            string avatarName = SanitizeName(configuration.Avatar.Name);
            string materialFolder = AssetDatabase.GenerateUniqueAssetPath(
                outputDirectory + "/" + avatarName + "_FallbackMaterials");
            string folderGuid = AssetDatabase.CreateFolder(outputDirectory, Path.GetFileName(materialFolder));
            if (string.IsNullOrEmpty(folderGuid))
                throw new InvalidOperationException("Unity could not create fallback material folder '" +
                                                    materialFolder + "'.");
            createdAssets.Add(materialFolder);

            var persistentBySource = new Dictionary<Material, Material>();
            Material missingSource = null;
            using (var cache = new PreviewMaterialCache())
            {
                foreach (Renderer renderer in avatarCopy.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] originals = renderer.sharedMaterials;
                    var fallbacks = new Material[originals.Length];
                    for (int slot = 0; slot < originals.Length; slot++)
                    {
                        Material source = originals[slot];
                        Material persistent;
                        if (source == null)
                        {
                            if (missingSource == null)
                                missingSource = CreatePersistentFallback(cache.Fallback(null),
                                    "Missing_Source_Fallback", materialFolder, createdAssets, materialPaths);
                            persistent = missingSource;
                        }
                        else if (!persistentBySource.TryGetValue(source, out persistent))
                        {
                            persistent = CreatePersistentFallback(cache.Fallback(source),
                                SanitizeName(source.name) + "_Fallback", materialFolder,
                                createdAssets, materialPaths);
                            persistentBySource.Add(source, persistent);
                        }
                        fallbacks[slot] = persistent;
                    }
                    renderer.sharedMaterials = fallbacks;
                }

                foreach (string warning in cache.Warnings)
                    diagnostics.Add(new ConversionDiagnostic(ConversionDiagnosticSeverity.Warning,
                        "FALLBACK_TRANSLATION_LIMITATION", warning));
            }

            avatarCopy.name = avatarName + "_Converted_Fallback";
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                outputDirectory + "/" + avatarName + "_Converted_Fallback.prefab");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(avatarCopy, prefabPath);
            if (saved == null)
                throw new InvalidOperationException("Unity could not save the fallback avatar prefab at '" +
                                                    prefabPath + "'.");
            createdAssets.Add(prefabPath);
            AssetDatabase.SaveAssets();
            return prefabPath;
        }

        private static Material CreatePersistentFallback(Material temporary, string preferredName,
            string materialFolder, List<string> createdAssets, List<string> materialPaths)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(
                materialFolder + "/" + preferredName + ".mat");
            var persistent = new Material(temporary)
            {
                name = Path.GetFileNameWithoutExtension(path),
                hideFlags = HideFlags.None
            };
            AssetDatabase.CreateAsset(persistent, path);
            createdAssets.Add(path);
            materialPaths.Add(path);
            return persistent;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string NormalizeDirectory(string path) => path.Replace('\\', '/').TrimEnd('/');
        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Converted";
            var result = new char[value.Length];
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                result[index] = char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character : '_';
            }
            return new string(result).Trim('_');
        }

    }
}
