using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nvh.UvTileScanner.Conversion
{
    public enum GeometryFeatureType { Toggle, Exclusive }
    public enum UnassignedGeometryHandling { Preserve, Error }
    public enum TriangleBoundaryPolicy { Reject, PreserveAsUnassigned }
    public enum ConversionDiagnosticSeverity { Info, Warning, Error }

    [Serializable]
    public sealed class AvatarGeometryConfiguration
    {
        public int SchemaVersion { get; set; } = 1;
        public AvatarConfiguration Avatar { get; set; } = new AvatarConfiguration();
        public List<GeometryGroupConfiguration> GeometryGroups { get; set; } = new List<GeometryGroupConfiguration>();
        public List<FeatureConfiguration> Features { get; set; } = new List<FeatureConfiguration>();
        public ConversionSettings Settings { get; set; } = new ConversionSettings();
    }

    [Serializable]
    public sealed class AvatarConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
    }

    [Serializable]
    public sealed class GeometryGroupConfiguration
    {
        public string Id { get; set; } = string.Empty;
        public string Renderer { get; set; } = string.Empty;
        public List<int> MaterialSlots { get; set; } = new List<int>();
        public int UvChannel { get; set; } = 1;
        public List<int> Tiles { get; set; } = new List<int>();
        public string Description { get; set; }
    }

    [Serializable]
    public sealed class FeatureConfiguration
    {
        public string Id { get; set; } = string.Empty;
        public GeometryFeatureType Type { get; set; }
        public string DefaultState { get; set; } = string.Empty;
        public List<string> OwnedGroups { get; set; } = new List<string>();
        public List<FeatureStateConfiguration> States { get; set; } = new List<FeatureStateConfiguration>();
    }

    [Serializable]
    public sealed class FeatureStateConfiguration
    {
        public string Id { get; set; } = string.Empty;
        public List<string> VisibleGroups { get; set; } = new List<string>();
    }

    [Serializable]
    public sealed class ConversionSettings
    {
        public string OutputDirectory { get; set; } = "Assets/UVTileScannerOutput";
        public bool GeneratePreview { get; set; }
        public UnassignedGeometryHandling UnassignedGeometry { get; set; } = UnassignedGeometryHandling.Preserve;
        public TriangleBoundaryPolicy TriangleBoundaries { get; set; } = TriangleBoundaryPolicy.Reject;
    }

    public sealed class ConfigurationParseResult
    {
        public ConfigurationParseResult(AvatarGeometryConfiguration configuration,
            List<ConversionDiagnostic> diagnostics)
        {
            Configuration = configuration;
            Diagnostics = diagnostics.AsReadOnly();
        }

        public AvatarGeometryConfiguration Configuration { get; }
        public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
        public bool Success
        {
            get
            {
                if (Configuration == null) return false;
                foreach (ConversionDiagnostic diagnostic in Diagnostics)
                    if (diagnostic.Severity == ConversionDiagnosticSeverity.Error) return false;
                return true;
            }
        }
    }

    [Serializable]
    public sealed class ConversionDiagnostic
    {
        public ConversionDiagnostic(ConversionDiagnosticSeverity severity, string code, string message,
            int line = 0, int column = 0)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Line = line;
            Column = column;
        }

        public ConversionDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }
        public override string ToString() => Line > 0
            ? $"{Severity} {Code} ({Line}:{Column}): {Message}"
            : $"{Severity} {Code}: {Message}";
    }

    public sealed class TriangleAssignment
    {
        internal TriangleAssignment(int sourceSubMesh, int sourceMaterialSlot, int triangleIndex,
            int a, int b, int c, string groupId, int? udim)
        {
            SourceSubMesh = sourceSubMesh;
            SourceMaterialSlot = sourceMaterialSlot;
            TriangleIndex = triangleIndex;
            A = a; B = b; C = c;
            GroupId = groupId;
            Udim = udim;
        }

        public int SourceSubMesh { get; }
        public int SourceMaterialSlot { get; }
        public int TriangleIndex { get; }
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public string GroupId { get; }
        public int? Udim { get; }
        public bool IsUnassigned => string.IsNullOrEmpty(GroupId);
    }

    public sealed class OutputSubMeshPlan
    {
        internal OutputSubMeshPlan(int outputSubMesh, int sourceSubMesh, int sourceMaterialSlot,
            string groupId, List<int> indices)
        {
            OutputSubMesh = outputSubMesh;
            SourceSubMesh = sourceSubMesh;
            SourceMaterialSlot = sourceMaterialSlot;
            GroupId = groupId;
            Indices = indices.AsReadOnly();
        }

        public int OutputSubMesh { get; }
        public int SourceSubMesh { get; }
        public int SourceMaterialSlot { get; }
        public string GroupId { get; }
        public IReadOnlyList<int> Indices { get; }
        public int TriangleCount => Indices.Count / 3;
        public bool IsUnassigned => string.IsNullOrEmpty(GroupId);
    }

    public sealed class RendererConversionPlan
    {
        internal RendererConversionPlan(string rendererPath, Renderer renderer, Mesh sourceMesh,
            List<TriangleAssignment> assignments, List<OutputSubMeshPlan> outputSubMeshes)
        {
            RendererPath = rendererPath;
            Renderer = renderer;
            SourceMesh = sourceMesh;
            Assignments = assignments.AsReadOnly();
            OutputSubMeshes = outputSubMeshes.AsReadOnly();
        }

        public string RendererPath { get; }
        public Renderer Renderer { get; }
        public Mesh SourceMesh { get; }
        public IReadOnlyList<TriangleAssignment> Assignments { get; }
        public IReadOnlyList<OutputSubMeshPlan> OutputSubMeshes { get; }
        public int TriangleCount => Assignments.Count;
        public int UnassignedTriangleCount
        {
            get
            {
                int count = 0;
                foreach (TriangleAssignment assignment in Assignments)
                    if (assignment.IsUnassigned) count++;
                return count;
            }
        }
    }

    public sealed class GeometryConversionReport
    {
        internal GeometryConversionReport(AvatarGeometryConfiguration configuration,
            List<RendererConversionPlan> renderers, List<ConversionDiagnostic> diagnostics)
        {
            Configuration = configuration;
            Renderers = renderers.AsReadOnly();
            Diagnostics = diagnostics.AsReadOnly();
        }

        public AvatarGeometryConfiguration Configuration { get; }
        public IReadOnlyList<RendererConversionPlan> Renderers { get; }
        public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
        public string OutputPrefabPath { get; internal set; }
        public string FallbackPrefabPath { get; internal set; }
        public string ManifestPath { get; internal set; }
        public IReadOnlyList<string> GeneratedMeshPaths { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> GeneratedFallbackMaterialPaths { get; internal set; } = Array.Empty<string>();
        public bool HasErrors
        {
            get
            {
                foreach (ConversionDiagnostic diagnostic in Diagnostics)
                    if (diagnostic.Severity == ConversionDiagnosticSeverity.Error) return true;
                return false;
            }
        }
        public int TotalTriangleCount
        {
            get
            {
                int total = 0;
                foreach (RendererConversionPlan renderer in Renderers) total += renderer.TriangleCount;
                return total;
            }
        }
        public int UnassignedTriangleCount
        {
            get
            {
                int total = 0;
                foreach (RendererConversionPlan renderer in Renderers) total += renderer.UnassignedTriangleCount;
                return total;
            }
        }
    }
}
