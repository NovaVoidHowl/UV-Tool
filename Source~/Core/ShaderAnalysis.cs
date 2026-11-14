using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nvh.UvTileScanner
{
    public enum AnalysisConfidence { Unknown, Low, Medium, High, Confirmed }
    public enum ShaderFamilyClassification { Confirmed, Probable, Unknown, ConfirmedNonTarget }
    public enum AnalysisTriState { Unknown, No, Yes }
    public enum ShaderVersionKind { Unknown, FamilyOrRange, Exact }
    public enum FeatureReadability { Direct, SerializedOnly, PartialOrBaked, Unknown }
    public enum ShaderFeatureType { UvTileDiscard, UvFaceDiscard, UvTileDissolve, UvTileGeometricDissolve }
    public enum ShaderTileState { Unknown, KeptOrInactive, DiscardedOrActive, PartialDissolve, NegativeValue }
    public enum AnalysisDiagnosticSeverity { Info, Warning, Error }

    public sealed class AnalysisDiagnostic
    {
        public AnalysisDiagnostic(AnalysisDiagnosticSeverity severity, string message, string detectorId = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            DetectorId = detectorId;
        }

        public AnalysisDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public string DetectorId { get; }
    }

    public sealed class ShaderVersionInfo
    {
        public ShaderVersionInfo(string displayValue, ShaderVersionKind kind, string method,
            AnalysisConfidence confidence, IEnumerable<string> evidence = null,
            ShaderVersionFamilyInfo compatibilityFamily = null)
        {
            DisplayValue = string.IsNullOrEmpty(displayValue) ? "Unknown" : displayValue;
            Kind = kind;
            Method = method ?? "No reliable version source";
            Confidence = confidence;
            Evidence = ReadOnly(evidence);
            CompatibilityFamily = compatibilityFamily;
        }

        public string DisplayValue { get; }
        public ShaderVersionKind Kind { get; }
        public string Method { get; }
        public AnalysisConfidence Confidence { get; }
        public IReadOnlyList<string> Evidence { get; }
        /// <summary>The major/minor family used for compatibility decisions, when known.</summary>
        public ShaderVersionFamilyInfo CompatibilityFamily { get; }

        public static ShaderVersionInfo Unknown(string method = null) =>
            new ShaderVersionInfo("Unknown", ShaderVersionKind.Unknown,
                method ?? "No reliable version source", AnalysisConfidence.Unknown);

        private static IReadOnlyList<string> ReadOnly(IEnumerable<string> values) =>
            new List<string>(values ?? Array.Empty<string>()).AsReadOnly();
    }

    public sealed class ShaderVersionFamilyInfo
    {
        public ShaderVersionFamilyInfo(string displayValue, string method, AnalysisConfidence confidence,
            IEnumerable<string> evidence = null)
        {
            DisplayValue = displayValue;
            Method = method ?? "No reliable compatibility-family source";
            Confidence = confidence;
            Evidence = new List<string>(evidence ?? Array.Empty<string>()).AsReadOnly();
        }

        public string DisplayValue { get; }
        public string Method { get; }
        public AnalysisConfidence Confidence { get; }
        public IReadOnlyList<string> Evidence { get; }
    }

    public sealed class ShaderTileControl
    {
        public ShaderTileControl(int row, int column, string propertyName, string propertyType,
            float? value, ShaderTileState state, FeatureReadability readability, bool canAnimate,
            bool? markedAnimated, AnalysisConfidence confidence, IEnumerable<string> evidence = null)
        {
            Row = row;
            Column = column;
            PropertyName = propertyName;
            PropertyType = propertyType;
            Value = value;
            State = state;
            Readability = readability;
            CanAnimate = canAnimate;
            MarkedAnimated = markedAnimated;
            Confidence = confidence;
            Evidence = new List<string>(evidence ?? Array.Empty<string>()).AsReadOnly();
        }

        public int Row { get; }
        public int Column { get; }
        public int UdimNumber => 1001 + Column + (Row * 10);
        public UvTile Tile => new UvTile(Column, Row);
        public string PropertyName { get; }
        public string PropertyType { get; }
        public float? Value { get; }
        public ShaderTileState State { get; }
        public FeatureReadability Readability { get; }
        public bool CanAnimate { get; }
        public bool? MarkedAnimated { get; }
        public AnalysisConfidence Confidence { get; }
        public IReadOnlyList<string> Evidence { get; }
        public bool IsConfigured => Value.HasValue && Math.Abs(Value.Value) > 0.0001f;
    }

    public sealed class ShaderFeatureAnalysis
    {
        public ShaderFeatureAnalysis(ShaderFeatureType type, string displayName, AnalysisTriState enabled,
            int? uvChannel, IEnumerable<ShaderTileControl> controls, FeatureReadability readability,
            AnalysisConfidence confidence, IEnumerable<string> evidence = null,
            IEnumerable<AnalysisDiagnostic> diagnostics = null)
        {
            Type = type;
            DisplayName = displayName;
            Enabled = enabled;
            UvChannel = uvChannel;
            Controls = new List<ShaderTileControl>(controls ?? Array.Empty<ShaderTileControl>()).AsReadOnly();
            Readability = readability;
            Confidence = confidence;
            Evidence = new List<string>(evidence ?? Array.Empty<string>()).AsReadOnly();
            Diagnostics = new List<AnalysisDiagnostic>(diagnostics ?? Array.Empty<AnalysisDiagnostic>()).AsReadOnly();
        }

        public ShaderFeatureType Type { get; }
        public string DisplayName { get; }
        public AnalysisTriState Enabled { get; }
        public int? UvChannel { get; }
        public IReadOnlyList<ShaderTileControl> Controls { get; }
        public FeatureReadability Readability { get; }
        public AnalysisConfidence Confidence { get; }
        public IReadOnlyList<string> Evidence { get; }
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
    }

    public sealed class ShaderAnalysisResult
    {
        public ShaderAnalysisResult(string detectorId, string detectorName, string targetFamily,
            ShaderFamilyClassification classification, AnalysisTriState locked,
            ShaderVersionInfo version, IEnumerable<ShaderFeatureAnalysis> features = null,
            IEnumerable<string> identificationEvidence = null,
            IEnumerable<AnalysisDiagnostic> diagnostics = null)
        {
            DetectorId = detectorId;
            DetectorName = detectorName;
            TargetFamily = targetFamily;
            Classification = classification;
            Locked = locked;
            Version = version ?? ShaderVersionInfo.Unknown();
            Features = new List<ShaderFeatureAnalysis>(features ?? Array.Empty<ShaderFeatureAnalysis>()).AsReadOnly();
            IdentificationEvidence = new List<string>(identificationEvidence ?? Array.Empty<string>()).AsReadOnly();
            Diagnostics = new List<AnalysisDiagnostic>(diagnostics ?? Array.Empty<AnalysisDiagnostic>()).AsReadOnly();
        }

        public string DetectorId { get; }
        public string DetectorName { get; }
        public string TargetFamily { get; }
        public ShaderFamilyClassification Classification { get; }
        public AnalysisTriState Locked { get; }
        public ShaderVersionInfo Version { get; }
        public IReadOnlyList<ShaderFeatureAnalysis> Features { get; }
        public IReadOnlyList<string> IdentificationEvidence { get; }
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
    }

    public sealed class ShaderFeatureMeshCorrelation
    {
        internal ShaderFeatureMeshCorrelation(string detectorId, ShaderFeatureType featureType, int uvChannel,
            List<int> subMeshIndices, List<UvTile> occupiedTiles, List<UvTile> configuredTiles,
            List<UvTile> configuredButUnoccupied, List<UvTile> occupiedButUnconfigured, string diagnostic)
        {
            DetectorId = detectorId;
            FeatureType = featureType;
            UvChannel = uvChannel;
            SubMeshIndices = subMeshIndices.AsReadOnly();
            OccupiedTiles = occupiedTiles.AsReadOnly();
            ConfiguredTiles = configuredTiles.AsReadOnly();
            ConfiguredButUnoccupied = configuredButUnoccupied.AsReadOnly();
            OccupiedButUnconfigured = occupiedButUnconfigured.AsReadOnly();
            Diagnostic = diagnostic;
        }

        public string DetectorId { get; }
        public ShaderFeatureType FeatureType { get; }
        public int UvChannel { get; }
        public IReadOnlyList<int> SubMeshIndices { get; }
        public IReadOnlyList<UvTile> OccupiedTiles { get; }
        public IReadOnlyList<UvTile> ConfiguredTiles { get; }
        public IReadOnlyList<UvTile> ConfiguredButUnoccupied { get; }
        public IReadOnlyList<UvTile> OccupiedButUnconfigured { get; }
        public string Diagnostic { get; }
    }

    public sealed class MaterialShaderAnalysisReport
    {
        internal MaterialShaderAnalysisReport(Material material, List<ShaderAnalysisResult> results,
            List<AnalysisDiagnostic> diagnostics)
        {
            Material = material;
            Results = results.AsReadOnly();
            Diagnostics = diagnostics.AsReadOnly();
        }

        public Material Material { get; }
        public IReadOnlyList<ShaderAnalysisResult> Results { get; }
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
        public bool Unsupported => Material != null && Results.Count == 0;
    }

    public sealed class ShaderAnalysisContext
    {
        public ShaderAnalysisContext() { }
        public string UnityVersion => Application.unityVersion;
    }

    /// <summary>Implement this in an editor-only assembly to add a shader family analyser.</summary>
    public interface IShaderUvTileDetector
    {
        string Id { get; }
        string DisplayName { get; }
        bool CanAnalyze(Material material);
        ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context);
    }

    /// <summary>Discovers detector implementations with Unity TypeCache, without loading arbitrary assemblies.</summary>
    public sealed class ShaderDetectorRegistry
    {
        private readonly List<IShaderUvTileDetector> _detectors;
        private readonly List<AnalysisDiagnostic> _diagnostics;

        public ShaderDetectorRegistry(IEnumerable<IShaderUvTileDetector> detectors)
            : this(detectors, null) { }

        private ShaderDetectorRegistry(IEnumerable<IShaderUvTileDetector> detectors,
            IEnumerable<AnalysisDiagnostic> diagnostics)
        {
            _detectors = new List<IShaderUvTileDetector>(detectors ?? Array.Empty<IShaderUvTileDetector>());
            _diagnostics = new List<AnalysisDiagnostic>(diagnostics ?? Array.Empty<AnalysisDiagnostic>());
        }

        public IReadOnlyList<IShaderUvTileDetector> Detectors => _detectors.AsReadOnly();
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics => _diagnostics.AsReadOnly();

        public static ShaderDetectorRegistry Discover()
        {
            var detectors = new List<IShaderUvTileDetector>();
            var diagnostics = new List<AnalysisDiagnostic>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IShaderUvTileDetector>())
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                    continue;
                try
                {
                    var detector = Activator.CreateInstance(type, true) as IShaderUvTileDetector;
                    if (detector == null)
                        throw new InvalidOperationException("The constructed object did not implement the detector contract.");
                    detectors.Add(detector);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Error,
                        $"Could not create shader detector {type.FullName}: {Unwrap(exception).Message}"));
                }
            }

            detectors.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
            return new ShaderDetectorRegistry(detectors, diagnostics);
        }

        private static Exception Unwrap(Exception exception) =>
            exception is System.Reflection.TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
    }

    internal sealed class ShaderAnalysisSession
    {
        private readonly ShaderDetectorRegistry _registry;
        private readonly Dictionary<Material, MaterialShaderAnalysisReport> _cache =
            new Dictionary<Material, MaterialShaderAnalysisReport>();
        private readonly ShaderAnalysisContext _context = new ShaderAnalysisContext();

        internal ShaderAnalysisSession(ShaderDetectorRegistry registry)
        {
            _registry = registry ?? new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>());
        }

        internal MaterialShaderAnalysisReport Analyze(Material material)
        {
            if (material == null)
                return new MaterialShaderAnalysisReport(null, new List<ShaderAnalysisResult>(),
                    new List<AnalysisDiagnostic>(_registry.Diagnostics));
            if (_cache.TryGetValue(material, out MaterialShaderAnalysisReport cached))
                return cached;

            var results = new List<ShaderAnalysisResult>();
            var diagnostics = new List<AnalysisDiagnostic>(_registry.Diagnostics);
            foreach (IShaderUvTileDetector detector in _registry.Detectors)
            {
                bool canAnalyze;
                try
                {
                    canAnalyze = detector.CanAnalyze(material);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Failure(detector, "capability check", exception));
                    continue;
                }

                if (!canAnalyze)
                    continue;
                try
                {
                    ShaderAnalysisResult result = detector.Analyze(material, _context);
                    if (result != null)
                        results.Add(result);
                    else
                        diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                            $"Detector {detector.DisplayName} returned no result.", detector.Id));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Failure(detector, "analysis", exception));
                }
            }

            var report = new MaterialShaderAnalysisReport(material, results, diagnostics);
            _cache.Add(material, report);
            return report;
        }

        private static AnalysisDiagnostic Failure(IShaderUvTileDetector detector, string operation, Exception exception)
        {
            if (exception is System.Reflection.TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Error,
                $"Detector {detector.DisplayName} failed during {operation}: {exception.Message}", detector.Id);
        }
    }
}
