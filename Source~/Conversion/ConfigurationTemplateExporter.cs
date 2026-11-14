using System;
using System.Collections.Generic;
using System.Text;
using Nvh.UvTileScanner;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nvh.UvTileScanner.Conversion
{
    public static class ConfigurationTemplateExporter
    {
        public static string Export(AvatarUvScanReport report)
        {
            if (report == null || report.Root == null)
                throw new ArgumentException("A completed avatar scan report is required.", nameof(report));
            var text = new StringBuilder();
            text.AppendLine("# UV Tile Scanner geometry conversion configuration template.");
            text.AppendLine("# Geometry groups below are factual renderer/material-slot/tile observations.");
            text.AppendLine("# Rename or combine groups deliberately, then define feature ownership yourself.");
            text.AppendLine("# No feature meaning is inferred from animation clips, shaders, or object names.");
            text.AppendLine("schemaVersion: 1");
            text.AppendLine();
            text.AppendLine("avatar:");
            text.AppendLine("  name: " + Quote(report.Root.name));
            text.AppendLine("  # description: \"Optional notes\"");
            text.AppendLine();

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var groupsText = new StringBuilder();
            int groupCount = 0;
            foreach (MeshUvScanResult meshResult in report.Meshes)
            {
                foreach (MeshUsageInfo usage in meshResult.UsageDetails)
                {
                    if (usage.Renderer == null) continue;
                    string path = RelativePath(report.Root, usage.Renderer.transform);
                    List<int> channels = AvailableUvChannels(meshResult.Mesh);
                    int selectedChannel = channels.Contains(1) ? 1 : channels.Count > 0 ? channels[0] : -1;
                    groupsText.AppendLine("  # " + path + " available UV channels: [" + string.Join(", ", channels) + "]");
                    if (selectedChannel < 0)
                    {
                        groupsText.AppendLine("  # No UV channels were available; add geometry groups manually after correcting the mesh.");
                        continue;
                    }
                    Dictionary<int, HashSet<int>> tilesBySubMesh = CollectTiles(meshResult.Mesh,
                        selectedChannel, out int ambiguousTriangles);
                    if (ambiguousTriangles > 0)
                        groupsText.AppendLine("  # Warning: " + ambiguousTriangles +
                                              " triangle(s) crossed tile boundaries and were not suggested.");
                    var materialSlots = new List<int>();
                    foreach (MeshMaterialSlot materialSlot in usage.MaterialSlots)
                        if (materialSlot.SlotIndex >= 0 && materialSlot.SlotIndex < meshResult.Mesh.subMeshCount &&
                            !materialSlots.Contains(materialSlot.SlotIndex))
                            materialSlots.Add(materialSlot.SlotIndex);
                    materialSlots.Sort();
                    foreach (int subMesh in materialSlots)
                    {
                        if (!tilesBySubMesh.TryGetValue(subMesh, out HashSet<int> tiles) || tiles.Count == 0)
                            continue;
                        var sortedTiles = new List<int>(tiles);
                        sortedTiles.Sort();
                        foreach (int udim in sortedTiles)
                        {
                            string id = UniqueId(Slug(path) + "-slot-" + subMesh + "-udim-" + udim, usedIds);
                            groupsText.AppendLine("  - id: " + Quote(id));
                            groupsText.AppendLine("    renderer: " + Quote(path));
                            groupsText.AppendLine("    materialSlots: [" + subMesh + "]");
                            groupsText.AppendLine("    uvChannel: " + selectedChannel);
                            groupsText.AppendLine("    tiles: [" + udim + "]");
                            groupsText.AppendLine("    description: " + Quote("Unassigned scanned geometry"));
                            groupCount++;
                        }
                    }
                }
            }
            text.AppendLine(groupCount == 0 ? "geometryGroups: []" : "geometryGroups:");
            text.Append(groupsText);
            text.AppendLine();
            text.AppendLine("# Features are intentionally empty. Add explicit ownership and visibility states.");
            text.AppendLine("features: []");
            text.AppendLine("# Example only:");
            text.AppendLine("# features:");
            text.AppendLine("#   - id: \"example-toggle\"");
            text.AppendLine("#     type: toggle");
            text.AppendLine("#     defaultState: \"on\"");
            text.AppendLine("#     ownedGroups: [\"replace-with-a-group-id\"]");
            text.AppendLine("#     states:");
            text.AppendLine("#       - id: \"on\"");
            text.AppendLine("#         visibleGroups: [\"replace-with-a-group-id\"]");
            text.AppendLine("#       - id: \"off\"");
            text.AppendLine("#         visibleGroups: []");
            text.AppendLine();
            text.AppendLine("settings:");
            text.AppendLine("  outputDirectory: \"Assets/UVTileScannerOutput\"");
            text.AppendLine("  generatePreview: false");
            text.AppendLine("  unassignedGeometry: preserve");
            text.AppendLine("  triangleBoundaries: reject");
            return text.ToString();
        }

        private static Dictionary<int, HashSet<int>> CollectTiles(Mesh mesh, int channel, out int ambiguous)
        {
            var result = new Dictionary<int, HashSet<int>>();
            ambiguous = 0;
            var uvs = new List<Vector2>();
            mesh.GetUVs(channel, uvs);
            if (uvs.Count != mesh.vertexCount) return result;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
                int[] indices = mesh.GetIndices(subMesh);
                var tiles = new HashSet<int>();
                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    if (!GeometryConversionPlanner.TryClassifyTriangleTile(
                            uvs[indices[offset]], uvs[indices[offset + 1]], uvs[indices[offset + 2]],
                            out UvTile tile, out _))
                    {
                        ambiguous++;
                        continue;
                    }
                    if (GeometryConversionPlanner.TryTileToUdim(tile.U, tile.V, out int udim)) tiles.Add(udim);
                }
                result[subMesh] = tiles;
            }
            return result;
        }

        private static List<int> AvailableUvChannels(Mesh mesh)
        {
            var result = new List<int>();
            for (int channel = 0; channel < 8; channel++)
                if (mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                    result.Add(channel);
            return result;
        }

        private static string RelativePath(GameObject root, Transform target)
        {
            if (target == root.transform) return string.Empty;
            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root.transform)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static string Slug(string value)
        {
            var result = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
                else if (result.Length > 0 && result[result.Length - 1] != '-') result.Append('-');
            }
            string slug = result.ToString().Trim('-');
            return string.IsNullOrEmpty(slug) ? "avatar-root" : slug;
        }

        private static string UniqueId(string proposed, HashSet<string> used)
        {
            string candidate = proposed;
            int suffix = 2;
            while (!used.Add(candidate)) candidate = proposed + "-" + suffix++;
            return candidate;
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
