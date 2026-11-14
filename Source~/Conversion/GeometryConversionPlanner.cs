using System;
using System.Collections.Generic;
using Nvh.UvTileScanner;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nvh.UvTileScanner.Conversion
{
    public static class GeometryConversionPlanner
    {
        private const float BoundaryEpsilon = 0.00001f;

        public static GeometryConversionReport ValidateAndPlan(GameObject avatarRoot,
            AvatarGeometryConfiguration configuration, AvatarUvScanReport scanReport = null)
        {
            var diagnostics = new List<ConversionDiagnostic>();
            var plans = new List<RendererConversionPlan>();
            ValidateConfiguration(configuration, diagnostics);
            if (avatarRoot == null)
                diagnostics.Add(Error("AVATAR_MISSING", "An avatar root is required."));
            if (scanReport != null && scanReport.Root != avatarRoot)
                diagnostics.Add(Error("SCAN_ROOT_MISMATCH", "The supplied scanner report belongs to a different avatar root."));
            if (configuration == null || avatarRoot == null || HasErrors(diagnostics))
                return new GeometryConversionReport(configuration, plans, diagnostics);

            var groupsByRenderer = new Dictionary<string, List<GeometryGroupConfiguration>>(StringComparer.Ordinal);
            foreach (GeometryGroupConfiguration group in configuration.GeometryGroups)
            {
                string path = NormalizeRendererPath(avatarRoot, group.Renderer);
                if (!groupsByRenderer.TryGetValue(path, out List<GeometryGroupConfiguration> groups))
                {
                    groups = new List<GeometryGroupConfiguration>();
                    groupsByRenderer.Add(path, groups);
                }
                groups.Add(group);
            }

            foreach (KeyValuePair<string, List<GeometryGroupConfiguration>> pair in groupsByRenderer)
            {
                if (!TryResolveRenderer(avatarRoot, pair.Key, out Renderer renderer, out Mesh mesh))
                {
                    diagnostics.Add(Error("RENDERER_NOT_FOUND",
                        $"Renderer '{pair.Key}' could not be resolved below avatar '{avatarRoot.name}'."));
                    continue;
                }
                RendererConversionPlan plan = PlanRenderer(pair.Key, renderer, mesh, pair.Value,
                    configuration.Settings, diagnostics);
                if (plan != null) plans.Add(plan);
            }

            if (configuration.Settings.GeneratePreview)
                diagnostics.Add(new ConversionDiagnostic(ConversionDiagnosticSeverity.Warning, "PREVIEW_NOT_IMPLEMENTED",
                    "Separate toggleable preview renderers are not generated in schema v1. The converted avatar preserves all geometry visibly."));
            return new GeometryConversionReport(configuration, plans, diagnostics);
        }

        public static bool TryUdimToTile(int udim, out UvTile tile)
        {
            if (udim < 1001 || udim > 2000)
            {
                tile = default;
                return false;
            }
            int offset = udim - 1001;
            tile = new UvTile(offset % 10, offset / 10);
            return true;
        }

        public static bool TryTileToUdim(int u, int v, out int udim)
        {
            if (u < 0 || u > 9 || v < 0 || v > 99)
            {
                udim = 0;
                return false;
            }
            udim = 1001 + u + (10 * v);
            return true;
        }

        public static bool TryClassifyTriangleTile(Vector2 first, Vector2 second, Vector2 third,
            out UvTile tile, out string diagnostic)
        {
            HashSet<UvTile> candidates = TileCandidates(first);
            candidates.IntersectWith(TileCandidates(second));
            candidates.IntersectWith(TileCandidates(third));
            if (candidates.Count == 1)
            {
                foreach (UvTile candidate in candidates)
                {
                    tile = candidate;
                    if (candidate.U < 0 || candidate.V < 0)
                    {
                        diagnostic = $"Triangle uses unsupported negative tile coordinates {candidate}.";
                        return false;
                    }
                    if (!TryTileToUdim(candidate.U, candidate.V, out _))
                    {
                        diagnostic = $"Triangle tile {candidate} is outside the supported UDIM range 1001–2000.";
                        return false;
                    }
                    diagnostic = null;
                    return true;
                }
            }
            tile = default;
            diagnostic = candidates.Count == 0
                ? "Triangle corners span multiple UV tiles."
                : "Triangle lies entirely on a UV tile boundary and has multiple valid tile assignments.";
            return false;
        }

        public static bool TryResolveRenderer(GameObject root, string relativePath,
            out Renderer renderer, out Mesh mesh)
        {
            renderer = null;
            mesh = null;
            if (root == null) return false;
            string path = NormalizeRendererPath(root, relativePath);
            Transform target = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
            if (target == null) return false;
            SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                renderer = skinned;
                mesh = skinned.sharedMesh;
                return mesh != null;
            }
            MeshRenderer staticRenderer = target.GetComponent<MeshRenderer>();
            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (staticRenderer == null || filter == null || filter.sharedMesh == null) return false;
            renderer = staticRenderer;
            mesh = filter.sharedMesh;
            return true;
        }

        private static void ValidateConfiguration(AvatarGeometryConfiguration configuration,
            List<ConversionDiagnostic> diagnostics)
        {
            if (configuration == null)
            {
                diagnostics.Add(Error("CONFIG_MISSING", "A parsed configuration is required."));
                return;
            }
            if (configuration.SchemaVersion != GeometryConfigurationYaml.SupportedSchemaVersion)
                diagnostics.Add(Error("SCHEMA_UNSUPPORTED",
                    $"Schema version {configuration.SchemaVersion} is not supported."));
            if (configuration.Avatar == null || string.IsNullOrWhiteSpace(configuration.Avatar.Name))
                diagnostics.Add(Error("AVATAR_NAME_REQUIRED", "avatar.name is required."));
            if (configuration.GeometryGroups == null || configuration.GeometryGroups.Count == 0)
                diagnostics.Add(Error("GROUPS_REQUIRED", "At least one geometry group is required."));
            ConversionSettings settings = configuration.Settings ?? new ConversionSettings();
            if (!IsSafeAssetDirectory(settings.OutputDirectory))
                diagnostics.Add(Error("OUTPUT_DIRECTORY_INVALID",
                    "settings.outputDirectory must be a project-relative directory below Assets and may not contain '..'."));

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var selectors = new HashSet<string>(StringComparer.Ordinal);
            foreach (GeometryGroupConfiguration group in configuration.GeometryGroups ?? new List<GeometryGroupConfiguration>())
            {
                if (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id))
                    diagnostics.Add(Error("GROUP_ID_INVALID", $"Geometry group ID '{group.Id}' is empty or duplicated."));
                if (string.IsNullOrWhiteSpace(group.Renderer))
                    diagnostics.Add(Error("GROUP_RENDERER_REQUIRED", $"Geometry group '{group.Id}' has no renderer path."));
                if (group.UvChannel < 0 || group.UvChannel > 7)
                    diagnostics.Add(Error("UV_CHANNEL_INVALID", $"Geometry group '{group.Id}' uses UV channel {group.UvChannel}; expected 0–7."));
                if (group.MaterialSlots == null || group.MaterialSlots.Count == 0)
                    diagnostics.Add(Error("MATERIAL_SLOTS_REQUIRED", $"Geometry group '{group.Id}' has no material slots."));
                if (group.Tiles == null || group.Tiles.Count == 0)
                    diagnostics.Add(Error("TILES_REQUIRED", $"Geometry group '{group.Id}' has no UDIM tiles."));
                var slots = new HashSet<int>();
                foreach (int slot in group.MaterialSlots ?? new List<int>())
                {
                    if (slot < 0 || !slots.Add(slot))
                        diagnostics.Add(Error("MATERIAL_SLOT_INVALID",
                            $"Geometry group '{group.Id}' contains invalid or duplicate material slot {slot}."));
                }
                var tiles = new HashSet<int>();
                foreach (int udim in group.Tiles ?? new List<int>())
                {
                    if (!TryUdimToTile(udim, out _) || !tiles.Add(udim))
                        diagnostics.Add(Error("UDIM_INVALID",
                            $"Geometry group '{group.Id}' contains unsupported or duplicate UDIM {udim}."));
                    foreach (int slot in slots)
                    {
                        string selector = group.Renderer + "|" + slot + "|" + group.UvChannel + "|" + udim;
                        if (!selectors.Add(selector))
                            diagnostics.Add(Error("GROUP_OVERLAP",
                                $"Geometry group '{group.Id}' overlaps another group at renderer '{group.Renderer}', slot {slot}, UV{group.UvChannel}, UDIM {udim}."));
                    }
                }
            }

            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FeatureConfiguration feature in configuration.Features ?? new List<FeatureConfiguration>())
            {
                if (string.IsNullOrWhiteSpace(feature.Id) || !featureIds.Add(feature.Id))
                    diagnostics.Add(Error("FEATURE_ID_INVALID", $"Feature ID '{feature.Id}' is empty or duplicated."));
                var owned = new HashSet<string>(StringComparer.Ordinal);
                foreach (string group in feature.OwnedGroups ?? new List<string>())
                {
                    if (!groupIds.Contains(group))
                        diagnostics.Add(Error("FEATURE_GROUP_UNKNOWN", $"Feature '{feature.Id}' owns unknown group '{group}'."));
                    if (!owned.Add(group))
                        diagnostics.Add(Error("FEATURE_GROUP_DUPLICATE", $"Feature '{feature.Id}' lists group '{group}' more than once."));
                    if (owners.TryGetValue(group, out string owner) && owner != feature.Id)
                        diagnostics.Add(Error("FEATURE_OWNERSHIP_CONFLICT",
                            $"Geometry group '{group}' is owned by both '{owner}' and '{feature.Id}'."));
                    else owners[group] = feature.Id;
                }
                var states = new HashSet<string>(StringComparer.Ordinal);
                foreach (FeatureStateConfiguration state in feature.States ?? new List<FeatureStateConfiguration>())
                {
                    if (string.IsNullOrWhiteSpace(state.Id) || !states.Add(state.Id))
                        diagnostics.Add(Error("STATE_ID_INVALID",
                            $"Feature '{feature.Id}' has an empty or duplicate state ID '{state.Id}'."));
                    var visible = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string group in state.VisibleGroups ?? new List<string>())
                    {
                        if (!owned.Contains(group))
                            diagnostics.Add(Error("STATE_GROUP_NOT_OWNED",
                                $"State '{feature.Id}/{state.Id}' references group '{group}' not listed in ownedGroups."));
                        if (!visible.Add(group))
                            diagnostics.Add(Error("STATE_GROUP_DUPLICATE",
                                $"State '{feature.Id}/{state.Id}' lists group '{group}' more than once."));
                    }
                }
                if (!states.Contains(feature.DefaultState))
                    diagnostics.Add(Error("DEFAULT_STATE_INVALID",
                        $"Feature '{feature.Id}' default state '{feature.DefaultState}' does not exist."));
            }
        }

        private static RendererConversionPlan PlanRenderer(string path, Renderer renderer, Mesh mesh,
            List<GeometryGroupConfiguration> groups, ConversionSettings settings,
            List<ConversionDiagnostic> diagnostics)
        {
            int initialDiagnosticCount = diagnostics.Count;
            if (mesh == null)
            {
                diagnostics.Add(Error("MESH_MISSING", $"Renderer '{path}' has no mesh."));
                return null;
            }
            foreach (GeometryGroupConfiguration group in groups)
                foreach (int slot in group.MaterialSlots)
                {
                    if (slot >= mesh.subMeshCount || slot >= renderer.sharedMaterials.Length)
                        diagnostics.Add(Error("MATERIAL_SLOT_OUT_OF_RANGE",
                            $"Group '{group.Id}' references material slot {slot} on '{path}', which has {mesh.subMeshCount} submeshes and {renderer.sharedMaterials.Length} materials."));
                }
            if (HasErrorsSince(diagnostics, initialDiagnosticCount)) return null;

            var uvByChannel = new Dictionary<int, List<Vector2>>();
            foreach (GeometryGroupConfiguration group in groups)
            {
                if (uvByChannel.ContainsKey(group.UvChannel)) continue;
                var uvs = new List<Vector2>();
                mesh.GetUVs(group.UvChannel, uvs);
                if (uvs.Count != mesh.vertexCount)
                    diagnostics.Add(Error("UV_CHANNEL_MISSING",
                        $"Renderer '{path}' mesh '{mesh.name}' does not have a complete UV{group.UvChannel} channel."));
                else uvByChannel.Add(group.UvChannel, uvs);
            }
            if (HasErrorsSince(diagnostics, initialDiagnosticCount)) return null;

            var assignments = new List<TriangleAssignment>();
            var partitionIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    diagnostics.Add(Error("TOPOLOGY_UNSUPPORTED",
                        $"Renderer '{path}' submesh {subMesh} uses {mesh.GetTopology(subMesh)} topology; only triangles are supported."));
                    continue;
                }
                int[] indices = mesh.GetIndices(subMesh);
                if (indices.Length % 3 != 0)
                {
                    diagnostics.Add(Error("INDEX_COUNT_INVALID",
                        $"Renderer '{path}' submesh {subMesh} has an index count not divisible by three."));
                    continue;
                }
                var slotGroups = new List<GeometryGroupConfiguration>();
                foreach (GeometryGroupConfiguration group in groups)
                    if (group.MaterialSlots.Contains(subMesh)) slotGroups.Add(group);
                for (int offset = 0; offset < indices.Length; offset += 3)
                {
                    int a = indices[offset], b = indices[offset + 1], c = indices[offset + 2];
                    var matches = new List<Tuple<GeometryGroupConfiguration, int>>();
                    bool ambiguous = false;
                    string ambiguity = null;
                    var tileByChannel = new Dictionary<int, int?>();
                    foreach (GeometryGroupConfiguration group in slotGroups)
                    {
                        if (!tileByChannel.TryGetValue(group.UvChannel, out int? udim))
                        {
                            List<Vector2> uvs = uvByChannel[group.UvChannel];
                            if (!TryClassifyTriangleTile(uvs[a], uvs[b], uvs[c], out UvTile tile, out string issue))
                            {
                                ambiguous = true;
                                ambiguity = issue;
                                tileByChannel[group.UvChannel] = null;
                                continue;
                            }
                            TryTileToUdim(tile.U, tile.V, out int converted);
                            udim = converted;
                            tileByChannel[group.UvChannel] = udim;
                        }
                        if (udim.HasValue && group.Tiles.Contains(udim.Value))
                            matches.Add(Tuple.Create(group, udim.Value));
                    }

                    string groupId = null;
                    int? assignedUdim = null;
                    if (matches.Count > 1)
                        diagnostics.Add(Error("TRIANGLE_GROUP_CONFLICT",
                            $"Renderer '{path}' submesh {subMesh} triangle {offset / 3} matches multiple geometry groups."));
                    else if (ambiguous)
                    {
                        ConversionDiagnosticSeverity severity = settings.TriangleBoundaries == TriangleBoundaryPolicy.Reject
                            ? ConversionDiagnosticSeverity.Error : ConversionDiagnosticSeverity.Warning;
                        diagnostics.Add(new ConversionDiagnostic(severity, "TRIANGLE_TILE_AMBIGUOUS",
                            $"Renderer '{path}' submesh {subMesh} triangle {offset / 3}: {ambiguity}"));
                    }
                    else if (matches.Count == 1)
                    {
                        groupId = matches[0].Item1.Id;
                        assignedUdim = matches[0].Item2;
                    }

                    var assignment = new TriangleAssignment(subMesh, subMesh, offset / 3,
                        a, b, c, groupId, assignedUdim);
                    assignments.Add(assignment);
                    string partition = (groupId ?? string.Empty) + "|" + subMesh;
                    if (!partitionIndices.TryGetValue(partition, out List<int> output))
                    {
                        output = new List<int>();
                        partitionIndices.Add(partition, output);
                    }
                    output.Add(a); output.Add(b); output.Add(c);
                }
            }

            int unassigned = 0;
            foreach (TriangleAssignment assignment in assignments) if (assignment.IsUnassigned) unassigned++;
            if (unassigned > 0)
            {
                ConversionDiagnosticSeverity severity = settings.UnassignedGeometry == UnassignedGeometryHandling.Error
                    ? ConversionDiagnosticSeverity.Error : ConversionDiagnosticSeverity.Info;
                diagnostics.Add(new ConversionDiagnostic(severity, "UNASSIGNED_TRIANGLES",
                    $"Renderer '{path}' has {unassigned} unassigned triangle(s); " +
                    (severity == ConversionDiagnosticSeverity.Error ? "conversion is disabled." : "they will be preserved in pass-through submeshes.")));
            }

            var outputSubMeshes = new List<OutputSubMeshPlan>();
            foreach (GeometryGroupConfiguration group in groups)
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    string key = group.Id + "|" + subMesh;
                    if (partitionIndices.TryGetValue(key, out List<int> indices) && indices.Count > 0)
                        outputSubMeshes.Add(new OutputSubMeshPlan(outputSubMeshes.Count, subMesh, subMesh, group.Id, indices));
                }
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                string key = "|" + subMesh;
                if (partitionIndices.TryGetValue(key, out List<int> indices) && indices.Count > 0)
                    outputSubMeshes.Add(new OutputSubMeshPlan(outputSubMeshes.Count, subMesh, subMesh, null, indices));
            }
            foreach (GeometryGroupConfiguration group in groups)
            {
                int count = 0;
                foreach (OutputSubMeshPlan output in outputSubMeshes)
                    if (output.GroupId == group.Id) count += output.TriangleCount;
                if (count == 0)
                    diagnostics.Add(new ConversionDiagnostic(ConversionDiagnosticSeverity.Warning, "GROUP_EMPTY",
                        $"Geometry group '{group.Id}' matched no triangles on renderer '{path}'."));
            }
            ValidateMembership(path, mesh, assignments, outputSubMeshes, diagnostics);
            return new RendererConversionPlan(path, renderer, mesh, assignments, outputSubMeshes);
        }

        private static void ValidateMembership(string path, Mesh mesh, List<TriangleAssignment> assignments,
            List<OutputSubMeshPlan> outputs, List<ConversionDiagnostic> diagnostics)
        {
            int sourceTriangles = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                    sourceTriangles += mesh.GetIndices(subMesh).Length / 3;
            int outputTriangles = 0;
            foreach (OutputSubMeshPlan output in outputs) outputTriangles += output.TriangleCount;
            if (sourceTriangles != assignments.Count || assignments.Count != outputTriangles)
                diagnostics.Add(Error("TRIANGLE_MEMBERSHIP_INVALID",
                    $"Renderer '{path}' triangle membership changed: source {sourceTriangles}, assigned {assignments.Count}, output {outputTriangles}."));
        }

        private static HashSet<UvTile> TileCandidates(Vector2 uv)
        {
            List<int> us = CoordinateCandidates(uv.x);
            List<int> vs = CoordinateCandidates(uv.y);
            var result = new HashSet<UvTile>();
            foreach (int u in us) foreach (int v in vs) result.Add(new UvTile(u, v));
            return result;
        }

        private static List<int> CoordinateCandidates(float value)
        {
            int floor = Mathf.FloorToInt(value);
            var result = new List<int> { floor };
            float rounded = Mathf.Round(value);
            if (Mathf.Abs(value - rounded) <= BoundaryEpsilon)
            {
                int boundary = Mathf.RoundToInt(rounded);
                if (!result.Contains(boundary)) result.Add(boundary);
                if (!result.Contains(boundary - 1)) result.Add(boundary - 1);
            }
            return result;
        }

        private static string NormalizeRendererPath(GameObject root, string path)
        {
            path = (path ?? string.Empty).Trim('/');
            if (root != null && string.Equals(path, root.name, StringComparison.Ordinal)) return string.Empty;
            string prefix = root == null ? null : root.name + "/";
            if (!string.IsNullOrEmpty(prefix) && path.StartsWith(prefix, StringComparison.Ordinal))
                return path.Substring(prefix.Length);
            return path;
        }

        private static bool IsSafeAssetDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            return (normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)) &&
                   normalized.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        private static bool HasErrors(List<ConversionDiagnostic> diagnostics)
        {
            foreach (ConversionDiagnostic diagnostic in diagnostics)
                if (diagnostic.Severity == ConversionDiagnosticSeverity.Error) return true;
            return false;
        }

        private static bool HasErrorsSince(List<ConversionDiagnostic> diagnostics, int startingIndex)
        {
            for (int index = startingIndex; index < diagnostics.Count; index++)
                if (diagnostics[index].Severity == ConversionDiagnosticSeverity.Error) return true;
            return false;
        }

        private static ConversionDiagnostic Error(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Error, code, message);
    }
}
