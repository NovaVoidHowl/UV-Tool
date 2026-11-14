using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nvh.UvTileScanner
{
    /// <summary>An integer UV tile coordinate. Tile (0, 0) is UDIM 1001.</summary>
    public struct UvTile : IEquatable<UvTile>, IComparable<UvTile>
    {
        public UvTile(int u, int v)
        {
            U = u;
            V = v;
        }

        public int U { get; }
        public int V { get; }
        public bool HasStandardUdimNumber => U >= 0 && U <= 9 && V >= 0;
        public int UdimNumber => 1001 + U + (V * 10);

        public int CompareTo(UvTile other)
        {
            int vertical = V.CompareTo(other.V);
            return vertical != 0 ? vertical : U.CompareTo(other.U);
        }

        public bool Equals(UvTile other) => U == other.U && V == other.V;
        public override bool Equals(object obj) => obj is UvTile other && Equals(other);
        public override int GetHashCode() => unchecked((U * 397) ^ V);
        public override string ToString() => HasStandardUdimNumber
            ? $"({U}, {V}) / UDIM {UdimNumber}"
            : $"({U}, {V})";
    }

    public enum UvTileScanStatus
    {
        NoSecondUvSet,
        EmptySecondUvSet,
        SingleTile,
        MultipleTiles,
        Error
    }

    /// <summary>One material slot captured from a renderer using a scanned mesh.</summary>
    public sealed class MeshMaterialSlot
    {
        internal MeshMaterialSlot(int slotIndex, Material material, ShaderAnalysisSession analysisSession)
        {
            SlotIndex = slotIndex;
            Material = material;
            Shader = material != null ? material.shader : null;
            ShaderAnalysis = analysisSession.Analyze(material);
            FeatureCorrelations = new List<ShaderFeatureMeshCorrelation>().AsReadOnly();
            AnimationBindings = new List<AnimationBindingAnalysis>().AsReadOnly();
        }

        public int SlotIndex { get; }
        public Material Material { get; }
        public Shader Shader { get; }
        public MaterialShaderAnalysisReport ShaderAnalysis { get; }
        public IReadOnlyList<ShaderFeatureMeshCorrelation> FeatureCorrelations { get; internal set; }
        /// <summary>Read-only animation bindings correlated during the avatar scan.</summary>
        public IReadOnlyList<AnimationBindingAnalysis> AnimationBindings { get; internal set; }
    }

    /// <summary>One hierarchy occurrence of a mesh and the materials assigned to its renderer.</summary>
    public sealed class MeshUsageInfo
    {
        internal MeshUsageInfo(string hierarchyPath, string componentType, Renderer renderer,
            ShaderAnalysisSession analysisSession)
        {
            HierarchyPath = hierarchyPath;
            ComponentType = componentType;
            Renderer = renderer;

            var materialSlots = new List<MeshMaterialSlot>();
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                    materialSlots.Add(new MeshMaterialSlot(slot, materials[slot], analysisSession));
            }

            MaterialSlots = materialSlots.AsReadOnly();
        }

        public string HierarchyPath { get; }
        public string ComponentType { get; }
        public string DisplayPath => $"{HierarchyPath} ({ComponentType})";
        public Renderer Renderer { get; }
        public IReadOnlyList<MeshMaterialSlot> MaterialSlots { get; }
    }

    /// <summary>The result for one unique Mesh, including every occurrence below the avatar root.</summary>
    public sealed class MeshUvScanResult
    {
        internal MeshUvScanResult(
            Mesh mesh,
            List<MeshUsageInfo> usageDetails,
            UvTileScanStatus status,
            List<UvTile> tiles,
            int crossedTilePrimitiveCount,
            string error)
        {
            Mesh = mesh;
            UsageDetails = usageDetails.AsReadOnly();
            var usagePaths = new List<string>(usageDetails.Count);
            foreach (MeshUsageInfo usage in usageDetails)
                usagePaths.Add(usage.DisplayPath);
            Usages = usagePaths.AsReadOnly();
            Status = status;
            Tiles = tiles.AsReadOnly();
            CrossedTilePrimitiveCount = crossedTilePrimitiveCount;
            Error = error;
        }

        public Mesh Mesh { get; }
        public IReadOnlyList<MeshUsageInfo> UsageDetails { get; }
        /// <summary>Display paths retained for compatibility; use UsageDetails for material information.</summary>
        public IReadOnlyList<string> Usages { get; }
        public UvTileScanStatus Status { get; }
        public IReadOnlyList<UvTile> Tiles { get; }
        public int CrossedTilePrimitiveCount { get; }
        public string Error { get; }
        public bool HasSecondUvSet => Status != UvTileScanStatus.NoSecondUvSet && Status != UvTileScanStatus.Error;
    }

    /// <summary>A complete snapshot of all unique meshes found below an avatar root.</summary>
    public sealed class AvatarUvScanReport
    {
        internal AvatarUvScanReport(GameObject root, List<MeshUvScanResult> meshes, int missingMeshReferences)
        {
            Root = root;
            Meshes = meshes.AsReadOnly();
            MissingMeshReferences = missingMeshReferences;
            var uniqueMaterials = new HashSet<Material>();
            var uniqueShaders = new HashSet<Shader>();

            foreach (MeshUvScanResult result in meshes)
            {
                switch (result.Status)
                {
                    case UvTileScanStatus.NoSecondUvSet:
                        WithoutSecondUvSet++;
                        break;
                    case UvTileScanStatus.SingleTile:
                        SingleTile++;
                        break;
                    case UvTileScanStatus.MultipleTiles:
                        MultipleTiles++;
                        break;
                    default:
                        Issues++;
                        break;
                }

                foreach (MeshUsageInfo usage in result.UsageDetails)
                {
                    foreach (MeshMaterialSlot slot in usage.MaterialSlots)
                    {
                        MaterialSlotCount++;
                        if (slot.Material != null)
                            uniqueMaterials.Add(slot.Material);
                        if (slot.Shader != null)
                            uniqueShaders.Add(slot.Shader);
                    }
                }
            }


            UniqueMaterialCount = uniqueMaterials.Count;
            UniqueShaderCount = uniqueShaders.Count;
            AnimationAnalysis = new AvatarAnimationAnalysisReport(AnimationScanScope.AvatarLinked,
                new List<AnimationClipAnalysis>(), new List<AnalysisDiagnostic>());
        }

        public GameObject Root { get; }
        public IReadOnlyList<MeshUvScanResult> Meshes { get; }
        public int MissingMeshReferences { get; }
        public int WithoutSecondUvSet { get; private set; }
        public int SingleTile { get; private set; }
        public int MultipleTiles { get; private set; }
        public int Issues { get; private set; }
        public int MaterialSlotCount { get; private set; }
        public int UniqueMaterialCount { get; }
        public int UniqueShaderCount { get; }
        public AvatarAnimationAnalysisReport AnimationAnalysis { get; internal set; }

        /// <summary>Attaches a completed, read-only animation analysis to this immutable mesh scan snapshot.</summary>
        public void SetAnimationAnalysis(AvatarAnimationAnalysisReport animationAnalysis)
        {
            AnimationAnalysis = animationAnalysis ?? new AvatarAnimationAnalysisReport(AnimationScanScope.AvatarLinked,
                new List<AnimationClipAnalysis>(), new List<AnalysisDiagnostic>());
        }
    }

    /// <summary>Public scanner API used by the editor window and available to other editor tooling.</summary>
    public static class AvatarUvScanner
    {
        private const float TileBoundaryEpsilon = 0.00001f;

        public static AvatarUvScanReport Scan(GameObject avatarRoot, bool includeInactive = true)
        {
            return Scan(avatarRoot, includeInactive, ShaderDetectorRegistry.Discover(), AnimationScanScope.AvatarLinked);
        }

        public static AvatarUvScanReport Scan(GameObject avatarRoot, bool includeInactive,
            AnimationScanScope animationScope)
        {
            return Scan(avatarRoot, includeInactive, ShaderDetectorRegistry.Discover(), animationScope);
        }

        /// <summary>Scans with an explicit detector registry; useful for extensions and isolated tests.</summary>
        public static AvatarUvScanReport Scan(GameObject avatarRoot, bool includeInactive,
            ShaderDetectorRegistry detectorRegistry)
        {
            return Scan(avatarRoot, includeInactive, detectorRegistry, AnimationScanScope.AvatarLinked);
        }

        /// <summary>Scans meshes, detector results, and read-only animation bindings with explicit options.</summary>
        public static AvatarUvScanReport Scan(GameObject avatarRoot, bool includeInactive,
            ShaderDetectorRegistry detectorRegistry, AnimationScanScope animationScope)
        {
            return ScanInternal(avatarRoot, includeInactive, detectorRegistry, animationScope, true);
        }

        /// <summary>Builds the mesh/material report first so an editor window can run animation analysis incrementally.</summary>
        public static AvatarUvScanReport ScanWithoutAnimationAnalysis(GameObject avatarRoot, bool includeInactive,
            ShaderDetectorRegistry detectorRegistry, AnimationScanScope animationScope)
        {
            return ScanInternal(avatarRoot, includeInactive, detectorRegistry, animationScope, false);
        }

        private static AvatarUvScanReport ScanInternal(GameObject avatarRoot, bool includeInactive,
            ShaderDetectorRegistry detectorRegistry, AnimationScanScope animationScope, bool analyzeAnimations)
        {
            AvatarUvScanReport report = null;
            IEnumerator operation = ScanMeshAndShaderIncremental(avatarRoot, includeInactive, detectorRegistry,
                null, value => report = value);
            while (operation.MoveNext()) { }

            if (analyzeAnimations)
                report.SetAnimationAnalysis(AvatarAnimationAnalyzer.Analyze(report, animationScope));
            return report;
        }

        /// <summary>
        /// Builds the mesh, material, shader, and UV report over resumable editor-update work items.
        /// The callback receives a completed report only after all mesh analysis has finished.
        /// </summary>
        public static IEnumerator ScanMeshAndShaderIncremental(GameObject avatarRoot, bool includeInactive,
            ShaderDetectorRegistry detectorRegistry, Action<float, string> progress,
            Action<AvatarUvScanReport> completed)
        {
            if (avatarRoot == null)
                throw new ArgumentNullException(nameof(avatarRoot));

            ReportProgress(progress, 0f, "Finding renderers, meshes, and materials");
            yield return null;

            var analysisSession = new ShaderAnalysisSession(detectorRegistry);
            var usagesByMesh = new Dictionary<Mesh, List<MeshUsageInfo>>();
            SkinnedMeshRenderer[] skinnedRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
            MeshFilter[] meshFilters = avatarRoot.GetComponentsInChildren<MeshFilter>(includeInactive);
            int rendererCount = skinnedRenderers.Length + meshFilters.Length;
            int processedRenderers = 0;
            int missingMeshes = 0;

            foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
            {
                if (renderer.sharedMesh == null)
                    missingMeshes++;
                else
                    AddUsage(usagesByMesh, renderer.sharedMesh, new MeshUsageInfo(
                        GetRelativePath(avatarRoot.transform, renderer.transform),
                        nameof(SkinnedMeshRenderer), renderer, analysisSession));

                processedRenderers++;
                if (processedRenderers % 8 == 0 || processedRenderers == rendererCount)
                {
                    ReportProgress(progress, GetRendererProgress(processedRenderers, rendererCount),
                        "Finding renderers, meshes, and materials");
                    yield return null;
                }
            }

            foreach (MeshFilter filter in meshFilters)
            {
                if (filter.sharedMesh == null)
                    missingMeshes++;
                else
                    AddUsage(usagesByMesh, filter.sharedMesh, new MeshUsageInfo(
                        GetRelativePath(avatarRoot.transform, filter.transform),
                        nameof(MeshFilter), filter.GetComponent<MeshRenderer>(), analysisSession));

                processedRenderers++;
                if (processedRenderers % 8 == 0 || processedRenderers == rendererCount)
                {
                    ReportProgress(progress, GetRendererProgress(processedRenderers, rendererCount),
                        "Finding renderers, meshes, and materials");
                    yield return null;
                }
            }

            ReportProgress(progress, 0.5f, "Analyzing mesh UVs and shader features");
            yield return null;

            var results = new List<MeshUvScanResult>(usagesByMesh.Count);
            int processedMeshes = 0;
            foreach (KeyValuePair<Mesh, List<MeshUsageInfo>> pair in usagesByMesh)
            {
                results.Add(Analyze(pair.Key, pair.Value));
                processedMeshes++;
                float portion = usagesByMesh.Count == 0 ? 1f : processedMeshes / (float)usagesByMesh.Count;
                ReportProgress(progress, 0.5f + (portion * 0.5f), "Analyzing mesh UVs and shader features");
                yield return null;
            }

            results.Sort(CompareResults);
            var report = new AvatarUvScanReport(avatarRoot, results, missingMeshes);
            ReportProgress(progress, 1f, "Mesh, material, and shader scan complete");
            completed?.Invoke(report);
        }

        private static float GetRendererProgress(int processedRenderers, int rendererCount)
        {
            float portion = rendererCount == 0 ? 1f : processedRenderers / (float)rendererCount;
            return 0.5f * portion;
        }

        private static void ReportProgress(Action<float, string> progress, float value, string message)
        {
            progress?.Invoke(Mathf.Clamp01(value), message ?? string.Empty);
        }

        private static void AddUsage(
            Dictionary<Mesh, List<MeshUsageInfo>> usagesByMesh,
            Mesh mesh,
            MeshUsageInfo usage)
        {
            if (!usagesByMesh.TryGetValue(mesh, out List<MeshUsageInfo> usages))
            {
                usages = new List<MeshUsageInfo>();
                usagesByMesh.Add(mesh, usages);
            }

            usages.Add(usage);
        }

        private static MeshUvScanResult Analyze(Mesh mesh, List<MeshUsageInfo> usages)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
            {
                MeshUvScanResult missing = new MeshUvScanResult(mesh, usages, UvTileScanStatus.NoSecondUvSet,
                    new List<UvTile>(), 0, null);
                BuildFeatureCorrelations(mesh, usages);
                return missing;
            }

            try
            {
                UvChannelTileResult channel = AnalyzeUvChannel(mesh, 1, null);
                UvTileScanStatus status = channel.Tiles.Count == 0
                    ? UvTileScanStatus.EmptySecondUvSet
                    : channel.Tiles.Count == 1 ? UvTileScanStatus.SingleTile : UvTileScanStatus.MultipleTiles;
                var result = new MeshUvScanResult(mesh, usages, status, channel.Tiles,
                    channel.CrossedTilePrimitiveCount, null);
                BuildFeatureCorrelations(mesh, usages);
                return result;
            }
            catch (Exception exception)
            {
                BuildFeatureCorrelations(mesh, usages);
                return new MeshUvScanResult(mesh, usages, UvTileScanStatus.Error,
                    new List<UvTile>(), 0, exception.Message);
            }
        }

        private static UvChannelTileResult AnalyzeUvChannel(Mesh mesh, int channel, HashSet<int> subMeshes)
        {
            if (channel < 0 || channel > 7 ||
                !mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                return new UvChannelTileResult(new List<UvTile>(), 0, $"UV{channel} is not present on this mesh.");

            using (Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData data = dataArray[0];
                if (data.vertexCount == 0)
                    return new UvChannelTileResult(new List<UvTile>(), 0, null);

                using (var uv = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp,
                           NativeArrayOptions.UninitializedMemory))
                {
                    data.GetUVs(channel, uv);
                    var tiles = new HashSet<UvTile>();
                    int crossedTiles = 0;
                    bool foundPrimitive = false;
                    for (int subMesh = 0; subMesh < data.subMeshCount; subMesh++)
                    {
                        if (subMeshes != null && !subMeshes.Contains(subMesh))
                            continue;
                        SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        int primitiveSize = GetPrimitiveSize(descriptor.topology);
                        if (primitiveSize == 0 || descriptor.indexCount < primitiveSize)
                            continue;

                        foundPrimitive = true;
                        using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp,
                                   NativeArrayOptions.UninitializedMemory))
                        {
                            data.GetIndices(indices, subMesh, true);
                            for (int start = 0; start + primitiveSize <= indices.Length; start += primitiveSize)
                            {
                                Vector2 centroid = Vector2.zero;
                                Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                                Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                                for (int offset = 0; offset < primitiveSize; offset++)
                                {
                                    int vertex = indices[start + offset];
                                    if (vertex < 0 || vertex >= uv.Length)
                                        throw new InvalidOperationException($"Mesh contains an invalid vertex index ({vertex}).");
                                    Vector2 coordinate = new Vector2(uv[vertex].x, uv[vertex].y);
                                    centroid += coordinate;
                                    minimum = Vector2.Min(minimum, coordinate);
                                    maximum = Vector2.Max(maximum, coordinate);
                                }
                                centroid /= primitiveSize;
                                tiles.Add(ToTile(centroid));
                                if (PrimitiveCrossesTileBoundary(minimum, maximum))
                                    crossedTiles++;
                            }
                        }
                    }

                    // Only use every vertex when the requested selection has no indexed primitives.
                    if (!foundPrimitive && (subMeshes == null || subMeshes.Count == data.subMeshCount))
                    {
                        for (int vertex = 0; vertex < uv.Length; vertex++)
                            tiles.Add(ToTile(new Vector2(uv[vertex].x, uv[vertex].y)));
                    }
                    var sorted = new List<UvTile>(tiles);
                    sorted.Sort();
                    return new UvChannelTileResult(sorted, crossedTiles, null);
                }
            }
        }

        private static void BuildFeatureCorrelations(Mesh mesh, List<MeshUsageInfo> usages)
        {
            foreach (MeshUsageInfo usage in usages)
            {
                foreach (MeshMaterialSlot slot in usage.MaterialSlots)
                {
                    var correlations = new List<ShaderFeatureMeshCorrelation>();
                    if (mesh.subMeshCount == 0 || slot.ShaderAnalysis == null)
                    {
                        slot.FeatureCorrelations = correlations.AsReadOnly();
                        continue;
                    }

                    var mappedSubMeshes = new List<int> { Math.Min(slot.SlotIndex, mesh.subMeshCount - 1) };
                    var selection = new HashSet<int>(mappedSubMeshes);
                    foreach (ShaderAnalysisResult analysis in slot.ShaderAnalysis.Results)
                    {
                        foreach (ShaderFeatureAnalysis feature in analysis.Features)
                        {
                            if (!feature.UvChannel.HasValue)
                                continue;
                            UvChannelTileResult tileResult;
                            try
                            {
                                tileResult = AnalyzeUvChannel(mesh, feature.UvChannel.Value, selection);
                            }
                            catch (Exception exception)
                            {
                                tileResult = new UvChannelTileResult(new List<UvTile>(), 0, exception.Message);
                            }

                            var configuredSet = new HashSet<UvTile>();
                            foreach (ShaderTileControl control in feature.Controls)
                            {
                                if (control.IsConfigured)
                                    configuredSet.Add(control.Tile);
                            }
                            var configured = new List<UvTile>(configuredSet);
                            configured.Sort();
                            var missing = Difference(configured, tileResult.Tiles);
                            var unconfigured = Difference(tileResult.Tiles, configured);
                            correlations.Add(new ShaderFeatureMeshCorrelation(analysis.DetectorId, feature.Type,
                                feature.UvChannel.Value, mappedSubMeshes, tileResult.Tiles, configured,
                                missing, unconfigured, tileResult.Error));
                        }
                    }
                    slot.FeatureCorrelations = correlations.AsReadOnly();
                }
            }
        }

        private static List<UvTile> Difference(List<UvTile> left, List<UvTile> right)
        {
            var rightSet = new HashSet<UvTile>(right);
            var result = new List<UvTile>();
            foreach (UvTile tile in left)
            {
                if (!rightSet.Contains(tile))
                    result.Add(tile);
            }
            return result;
        }

        private sealed class UvChannelTileResult
        {
            internal UvChannelTileResult(List<UvTile> tiles, int crossedTilePrimitiveCount, string error)
            {
                Tiles = tiles;
                CrossedTilePrimitiveCount = crossedTilePrimitiveCount;
                Error = error;
            }
            internal List<UvTile> Tiles { get; }
            internal int CrossedTilePrimitiveCount { get; }
            internal string Error { get; }
        }

        private static int GetPrimitiveSize(MeshTopology topology)
        {
            switch (topology)
            {
                case MeshTopology.Triangles: return 3;
                case MeshTopology.Quads: return 4;
                case MeshTopology.Lines: return 2;
                case MeshTopology.Points: return 1;
                default: return 0;
            }
        }

        private static UvTile ToTile(Vector2 uv)
        {
            return new UvTile(Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
        }

        private static bool PrimitiveCrossesTileBoundary(Vector2 minimum, Vector2 maximum)
        {
            int minimumU = Mathf.FloorToInt(minimum.x + TileBoundaryEpsilon);
            int maximumU = Mathf.FloorToInt(maximum.x - TileBoundaryEpsilon);
            int minimumV = Mathf.FloorToInt(minimum.y + TileBoundaryEpsilon);
            int maximumV = Mathf.FloorToInt(maximum.y - TileBoundaryEpsilon);
            return minimumU != maximumU || minimumV != maximumV;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
                return root.name;

            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            names.Push(root.name);
            return string.Join("/", names.ToArray());
        }

        private static int CompareResults(MeshUvScanResult left, MeshUvScanResult right)
        {
            int status = StatusOrder(left.Status).CompareTo(StatusOrder(right.Status));
            if (status != 0)
                return status;
            return string.Compare(left.Mesh.name, right.Mesh.name, StringComparison.OrdinalIgnoreCase);
        }

        private static int StatusOrder(UvTileScanStatus status)
        {
            switch (status)
            {
                case UvTileScanStatus.MultipleTiles: return 0;
                case UvTileScanStatus.SingleTile: return 1;
                case UvTileScanStatus.NoSecondUvSet: return 2;
                default: return 3;
            }
        }
    }
}
