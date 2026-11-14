using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Nvh.UvTileScanner.Poiyomi
{
    /// <summary>Read-only Poiyomi analyser. It has no compile-time dependency on Poiyomi or Thry.</summary>
    public sealed class PoiyomiShaderDetector : IShaderUvTileDetector
    {
        public const string DetectorId = "n.vh.poiyomi";
        private const string OfficialDetectorType =
            "Poi.Tools.ShaderTranslator.VersionUpgrade.PoiyomiVersionDetector";
        private static readonly Regex VersionRegex =
            new Regex(@"\b(\d+)\.(\d+)(?:\.(\d+))?\b", RegexOptions.Compiled);

        public string Id => DetectorId;
        public string DisplayName => "Poiyomi shader analyser";
        public bool CanAnalyze(Material material) => material != null;

        public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            var diagnostics = new List<AnalysisDiagnostic>();
            var properties = MaterialPropertySnapshot.Read(material, diagnostics);
            OfficialApiResult official = QueryOfficialApi(material, diagnostics);
            Identification identification = Identify(material, properties, official, diagnostics);
            ShaderVersionInfo version = DetectVersion(material, official, diagnostics);
            var features = new List<ShaderFeatureAnalysis>();

            if (identification.Classification == ShaderFamilyClassification.Confirmed ||
                identification.Classification == ShaderFamilyClassification.Probable)
            {
                AddFeature(features, BuildFeature(properties, ShaderFeatureType.UvTileDiscard,
                    "UDIM tile discard", "_EnableUDIMDiscardOptions", "_UDIMDiscardUV",
                    new Regex(@"^_UDIMDiscardRow([0-3])_([0-3])$"), false, null));
                AddFeature(features, BuildFeature(properties, ShaderFeatureType.UvFaceDiscard,
                    "Face tile discard", "_EnableUDIMFaceDiscardOptions", "_UDIMFaceDiscardUV",
                    new Regex(@"^_UDIMFaceDiscardRow([0-3])_([0-3])$"), false, null));
                AddFeature(features, BuildFeature(properties, ShaderFeatureType.UvTileDissolve,
                    "UV tile dissolve", "_UVTileDissolveEnabled", "_UVTileDissolveUV",
                    new Regex(@"^_UVTileDissolveAlpha_Row([0-3])_([0-3])$"), true,
                    "_EnableDissolve"));
                AddFeature(features, BuildFeature(properties, ShaderFeatureType.UvTileGeometricDissolve,
                    "UV tile geometric dissolve", "_UVTileGeoDissolveEnabled", "_UVTileGeoDissolveUV",
                    new Regex(@"^_UVTileGeoDissolveAlpha_Row([0-3])_([0-3])$"), true,
                    null));

                if (identification.Locked == AnalysisTriState.Yes && features.Count == 0)
                {
                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                        "The material appears locked, but no supported tile properties remain readable. " +
                        "Its configuration may have been baked into the generated shader.", Id));
                }
            }

            return new ShaderAnalysisResult(Id, DisplayName, "Poiyomi", identification.Classification,
                identification.Locked, version, features, identification.Evidence, diagnostics);
        }

        private static void AddFeature(List<ShaderFeatureAnalysis> features, ShaderFeatureAnalysis feature)
        {
            if (feature != null)
                features.Add(feature);
        }

        private ShaderFeatureAnalysis BuildFeature(MaterialPropertySnapshot properties,
            ShaderFeatureType type, string displayName, string enableProperty, string uvProperty,
            Regex tilePattern, bool dissolve, string parentEnableProperty)
        {
            var controls = new List<ShaderTileControl>();
            foreach (MaterialPropertyValue value in properties.Values)
            {
                Match match = tilePattern.Match(value.Name);
                if (!match.Success)
                    continue;
                int row = int.Parse(match.Groups[1].Value);
                int column = int.Parse(match.Groups[2].Value);
                ShaderTileState state = ClassifyTile(value.Value, dissolve);
                controls.Add(new ShaderTileControl(row, column, value.Name, value.TypeName,
                    value.Value, state, value.Readability, true, ReadAnimatedTag(properties.Material, value.Name),
                    AnalysisConfidence.Confirmed,
                    new[] { value.IsDirect ? "Read from the current shader property." : "Read from serialized material data." }));
            }
            controls.Sort((left, right) =>
            {
                int row = left.Row.CompareTo(right.Row);
                return row != 0 ? row : left.Column.CompareTo(right.Column);
            });

            bool hasEnable = properties.TryGet(enableProperty, out MaterialPropertyValue enable);
            bool hasUv = properties.TryGet(uvProperty, out MaterialPropertyValue uv);
            MaterialPropertyValue parent = null;
            bool hasParent = !string.IsNullOrEmpty(parentEnableProperty) &&
                             properties.TryGet(parentEnableProperty, out parent);
            if (!hasEnable && !hasUv && controls.Count == 0 && !hasParent)
                return null;

            AnalysisTriState enabled = hasEnable ? ToTriState(enable.Value) : AnalysisTriState.Unknown;
            if (hasParent)
            {
                AnalysisTriState parentState = ToTriState(parent.Value);
                if (parentState == AnalysisTriState.No || enabled == AnalysisTriState.No)
                    enabled = AnalysisTriState.No;
                else if (parentState == AnalysisTriState.Yes && enabled == AnalysisTriState.Yes)
                    enabled = AnalysisTriState.Yes;
                else
                    enabled = AnalysisTriState.Unknown;
            }

            int? uvChannel = null;
            if (hasUv)
            {
                int channel = Mathf.RoundToInt(uv.Value);
                if (channel >= 0 && channel <= 3)
                    uvChannel = channel;
            }

            FeatureReadability readability = FeatureReadability.Unknown;
            if (controls.Count > 0 || hasEnable || hasUv || hasParent)
            {
                bool anySerialized = (hasEnable && !enable.IsDirect) ||
                                     (hasUv && !uv.IsDirect) ||
                                     (hasParent && !parent.IsDirect);
                foreach (ShaderTileControl control in controls)
                    anySerialized |= control.Readability == FeatureReadability.SerializedOnly;
                readability = anySerialized ? FeatureReadability.SerializedOnly : FeatureReadability.Direct;
            }

            var evidence = new List<string>();
            if (hasEnable) evidence.Add($"Found {enableProperty}.");
            if (hasParent) evidence.Add($"Found parent control {parentEnableProperty}.");
            if (hasUv) evidence.Add($"Found {uvProperty}.");
            if (controls.Count > 0) evidence.Add($"Found {controls.Count} recognised tile control(s).");
            var featureDiagnostics = new List<AnalysisDiagnostic>();
            if (!uvChannel.HasValue)
                featureDiagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                    "The feature's UV channel is unavailable or outside UV0-UV3, so mesh correlation is unavailable.", Id));
            if (controls.Count == 0)
                featureDiagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                    "No supported per-tile controls are readable; a locked shader may have baked them away.", Id));

            return new ShaderFeatureAnalysis(type, displayName, enabled, uvChannel, controls,
                readability, controls.Count > 0 ? AnalysisConfidence.Confirmed : AnalysisConfidence.Medium,
                evidence, featureDiagnostics);
        }

        private static ShaderTileState ClassifyTile(float value, bool dissolve)
        {
            if (Mathf.Abs(value) <= 0.0001f)
                return ShaderTileState.KeptOrInactive;
            if (!dissolve || value >= 0.9999f)
                return ShaderTileState.DiscardedOrActive;
            if (value < 0f)
                return ShaderTileState.NegativeValue;
            return ShaderTileState.PartialDissolve;
        }

        private static AnalysisTriState ToTriState(float value) =>
            value >= 0.5f ? AnalysisTriState.Yes : AnalysisTriState.No;

        private static bool? ReadAnimatedTag(Material material, string propertyName)
        {
            string value = material.GetTag(propertyName + "Animated", false, string.Empty);
            if (string.IsNullOrEmpty(value))
                return null;
            return value != "0";
        }

        private static Identification Identify(Material material, MaterialPropertySnapshot properties,
            OfficialApiResult official, List<AnalysisDiagnostic> diagnostics)
        {
            var evidence = new List<string>();
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            string originalShader = material.GetTag("OriginalShader", false, string.Empty);
            string originalGuid = material.GetTag("OriginalShaderGUID", false, string.Empty);
            string assetPath = material.shader != null ? AssetDatabase.GetAssetPath(material.shader) : string.Empty;
            bool lockedName = shaderName.StartsWith("Hidden/Locked/", StringComparison.OrdinalIgnoreCase);
            AnalysisTriState locked = lockedName || !string.IsNullOrEmpty(originalShader) || !string.IsNullOrEmpty(originalGuid)
                ? AnalysisTriState.Yes : AnalysisTriState.Unknown;

            if (official.IsPoiyomi == true)
            {
                evidence.Add("The official PoiyomiVersionDetector identified this material.");
                return new Identification(ShaderFamilyClassification.Confirmed,
                    lockedName ? AnalysisTriState.Yes : AnalysisTriState.No, evidence);
            }
            if (IsOfficialName(StripLockedPrefix(shaderName)))
            {
                evidence.Add($"Shader name uses the official .poiyomi/ namespace: {shaderName}");
                return new Identification(ShaderFamilyClassification.Confirmed,
                    lockedName ? AnalysisTriState.Yes : AnalysisTriState.No, evidence);
            }
            if (IsOfficialName(originalShader))
            {
                evidence.Add($"OriginalShader tag names an official Poiyomi shader: {originalShader}");
                return new Identification(ShaderFamilyClassification.Confirmed, AnalysisTriState.Yes, evidence);
            }
            if (!string.IsNullOrEmpty(originalGuid))
            {
                string originalPath = AssetDatabase.GUIDToAssetPath(originalGuid);
                Shader original = string.IsNullOrEmpty(originalPath)
                    ? null : AssetDatabase.LoadAssetAtPath<Shader>(originalPath);
                if (original != null && IsOfficialName(original.name))
                {
                    evidence.Add($"OriginalShaderGUID resolves to {original.name}.");
                    return new Identification(ShaderFamilyClassification.Confirmed, AnalysisTriState.Yes, evidence);
                }
                if (!string.IsNullOrEmpty(originalPath))
                    evidence.Add($"OriginalShaderGUID resolves to {originalPath}, but its shader family is not definitive.");
            }

            bool hasSignature = properties.Contains("shader_master_label") ||
                                properties.Contains("_EnableUDIMDiscardOptions") ||
                                properties.Contains("_UDIMDiscardUV") ||
                                properties.Contains("_UVTileDissolveEnabled");
            bool poiyomiText = shaderName.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               originalShader.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               assetPath.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;
            if (poiyomiText && hasSignature)
            {
                evidence.Add("Poiyomi naming/path evidence is paired with recognised Poiyomi properties.");
                if (locked == AnalysisTriState.Unknown) locked = AnalysisTriState.No;
                return new Identification(ShaderFamilyClassification.Probable, locked, evidence);
            }
            if (locked == AnalysisTriState.Yes && hasSignature)
            {
                evidence.Add("Locked-shader metadata is paired with recognised Poiyomi properties.");
                return new Identification(ShaderFamilyClassification.Probable, locked, evidence);
            }

            if (IsKnownNonPoiyomi(shaderName) && string.IsNullOrEmpty(originalShader) && !hasSignature)
            {
                evidence.Add($"Known non-Poiyomi shader namespace: {shaderName}");
                return new Identification(ShaderFamilyClassification.ConfirmedNonTarget,
                    AnalysisTriState.No, evidence);
            }
            if (official.Available && official.IsPoiyomi == false)
                evidence.Add("The official detector did not identify this material, but that alone is not conclusive for legacy or locked variants.");
            return new Identification(ShaderFamilyClassification.Unknown, locked, evidence);
        }

        private static bool IsOfficialName(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(".poiyomi/", StringComparison.OrdinalIgnoreCase);

        private static string StripLockedPrefix(string name)
        {
            const string prefix = "Hidden/Locked/";
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name.Substring(prefix.Length) : name;
        }

        private static bool IsKnownNonPoiyomi(string name) =>
            name == "Standard" || name.StartsWith("Unlit/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Hidden/Internal", StringComparison.OrdinalIgnoreCase);

        private static ShaderVersionInfo DetectVersion(Material material, OfficialApiResult official,
            List<AnalysisDiagnostic> diagnostics)
        {
            LabelVersionResult label = DetectLabelVersion(material, diagnostics);
            if (official.Version != null)
            {
                string family = $"{official.Version.Major}.{official.Version.Minor}.x";
                var familyInfo = new ShaderVersionFamilyInfo(family,
                    "Official PoiyomiVersionDetector.TryGetVersion", AnalysisConfidence.Confirmed,
                    new[] { official.Version.Build >= 0
                        ? "The official API returned a complete version; major/minor are used as the compatibility family."
                        : "The official API returned major/minor only; no patch is inferred from it." });

                if (official.Version.Build >= 0)
                {
                    return new ShaderVersionInfo(official.Version.ToString(), ShaderVersionKind.Exact,
                        "Official PoiyomiVersionDetector.TryGetVersion", AnalysisConfidence.Confirmed,
                        new[] { "The official API returned a complete version." }, familyInfo);
                }

                if (label != null && label.Kind == ShaderVersionKind.Exact)
                {
                    if (label.Major == official.Version.Major && label.Minor == official.Version.Minor)
                    {
                        return new ShaderVersionInfo(label.DisplayValue, ShaderVersionKind.Exact,
                            label.Method, AnalysisConfidence.Confirmed,
                            new[]
                            {
                                label.Evidence,
                                $"The exact label agrees with the official {family} compatibility family."
                            }, familyInfo);
                    }

                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                        $"The exact shader label ({label.DisplayValue}) disagrees with the official compatibility " +
                        $"family ({family}); both values are retained with their separate sources.", DetectorId));
                    return new ShaderVersionInfo(label.DisplayValue, ShaderVersionKind.Exact,
                        label.Method, AnalysisConfidence.High,
                        new[] { label.Evidence, "The exact label does not agree with the official compatibility family." },
                        familyInfo);
                }

                return new ShaderVersionInfo($"{family} family", ShaderVersionKind.FamilyOrRange,
                    "Official PoiyomiVersionDetector.TryGetVersion", AnalysisConfidence.Confirmed,
                    new[] { "The official API returned major/minor only; no patch is inferred." }, familyInfo);
            }

            if (label != null)
            {
                string family = $"{label.Major}.{label.Minor}.x";
                var familyInfo = new ShaderVersionFamilyInfo(family,
                    "Major/minor from shader_master_label", AnalysisConfidence.High,
                    new[] { label.Evidence });
                return new ShaderVersionInfo(label.DisplayValue, label.Kind, label.Method,
                    AnalysisConfidence.High, new[] { label.Evidence }, familyInfo);
            }
            return ShaderVersionInfo.Unknown(official.Available
                ? "Official detector returned no version and no readable shader label was found."
                : "Official detector is not installed and no readable shader label was found.");
        }

        private static LabelVersionResult DetectLabelVersion(Material material,
            List<AnalysisDiagnostic> diagnostics)
        {
            var shaders = new List<Shader>();
            // ThryEditor renders the current shader's master label. For a normally generated locked
            // shader this preserves the version that was locked, even if the installed source moves on.
            AddUnique(shaders, material.shader);
            string originalGuid = material.GetTag("OriginalShaderGUID", false, string.Empty);
            if (!string.IsNullOrEmpty(originalGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(originalGuid);
                AddUnique(shaders, string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path));
            }
            string originalName = material.GetTag("OriginalShader", false, string.Empty);
            if (!string.IsNullOrEmpty(originalName))
                AddUnique(shaders, Shader.Find(originalName));

            foreach (Shader shader in shaders)
            {
                try
                {
                    int count = ShaderUtil.GetPropertyCount(shader);
                    for (int index = 0; index < count; index++)
                    {
                        if (ShaderUtil.GetPropertyName(shader, index) != "shader_master_label")
                            continue;
                        string description = ShaderUtil.GetPropertyDescription(shader, index);
                        Match match = VersionRegex.Match(description ?? string.Empty);
                        if (!match.Success)
                            break;
                        int major = int.Parse(match.Groups[1].Value);
                        int minor = int.Parse(match.Groups[2].Value);
                        bool exact = match.Groups[3].Success;
                        string display = exact
                            ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}"
                            : $"{major}.{minor}.x family";
                        return new LabelVersionResult(display,
                            exact ? ShaderVersionKind.Exact : ShaderVersionKind.FamilyOrRange,
                            major, minor, "shader_master_label property description",
                            $"Parsed shader_master_label on {shader.name}: {description}");
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                        $"Could not inspect shader_master_label on {shader.name}: {exception.Message}", DetectorId));
                }
            }
            return null;
        }

        private static void AddUnique(List<Shader> shaders, Shader shader)
        {
            if (shader != null && !shaders.Contains(shader))
                shaders.Add(shader);
        }

        private static OfficialApiResult QueryOfficialApi(Material material, List<AnalysisDiagnostic> diagnostics)
        {
            Type detectorType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                detectorType = assembly.GetType(OfficialDetectorType, false);
                if (detectorType != null) break;
            }
            if (detectorType == null)
                return new OfficialApiResult(false, null, null);

            bool? isPoiyomi = null;
            Version version = null;
            try
            {
                MethodInfo method = detectorType.GetMethod("IsPoiyomiShader", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Material) }, null);
                if (method == null || method.ReturnType != typeof(bool))
                    throw new MissingMethodException(detectorType.FullName, "IsPoiyomiShader(Material)");
                isPoiyomi = (bool)method.Invoke(null, new object[] { material });
            }
            catch (Exception exception)
            {
                diagnostics.Add(ReflectionFailure("IsPoiyomiShader", exception));
            }

            try
            {
                MethodInfo method = detectorType.GetMethod("TryGetVersion", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Material), typeof(Version).MakeByRefType() }, null);
                ParameterInfo[] parameters = method != null ? method.GetParameters() : Array.Empty<ParameterInfo>();
                if (method == null || method.ReturnType != typeof(bool) || parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(Material) ||
                    parameters[1].ParameterType != typeof(Version).MakeByRefType())
                    throw new MissingMethodException(detectorType.FullName, "TryGetVersion(Material, out Version)");
                object[] arguments = { material, null };
                if ((bool)method.Invoke(null, arguments))
                    version = arguments[1] as Version;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ReflectionFailure("TryGetVersion", exception));
            }
            return new OfficialApiResult(true, isPoiyomi, version);
        }

        private static AnalysisDiagnostic ReflectionFailure(string method, Exception exception)
        {
            if (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                $"Official PoiyomiVersionDetector.{method} could not be used: {exception.Message}", DetectorId);
        }

        private sealed class Identification
        {
            internal Identification(ShaderFamilyClassification classification, AnalysisTriState locked,
                List<string> evidence)
            {
                Classification = classification;
                Locked = locked;
                Evidence = evidence;
            }
            internal ShaderFamilyClassification Classification { get; }
            internal AnalysisTriState Locked { get; }
            internal List<string> Evidence { get; }
        }

        private sealed class OfficialApiResult
        {
            internal OfficialApiResult(bool available, bool? isPoiyomi, Version version)
            {
                Available = available;
                IsPoiyomi = isPoiyomi;
                Version = version;
            }
            internal bool Available { get; }
            internal bool? IsPoiyomi { get; }
            internal Version Version { get; }
        }

        private sealed class LabelVersionResult
        {
            internal LabelVersionResult(string displayValue, ShaderVersionKind kind, int major, int minor,
                string method, string evidence)
            {
                DisplayValue = displayValue;
                Kind = kind;
                Major = major;
                Minor = minor;
                Method = method;
                Evidence = evidence;
            }
            internal string DisplayValue { get; }
            internal ShaderVersionKind Kind { get; }
            internal int Major { get; }
            internal int Minor { get; }
            internal string Method { get; }
            internal string Evidence { get; }
        }

        private sealed class MaterialPropertySnapshot
        {
            private readonly Dictionary<string, MaterialPropertyValue> _values =
                new Dictionary<string, MaterialPropertyValue>(StringComparer.Ordinal);

            private MaterialPropertySnapshot(Material material) { Material = material; }
            internal Material Material { get; }
            internal IEnumerable<MaterialPropertyValue> Values => _values.Values;
            internal bool Contains(string name) => _values.ContainsKey(name);
            internal bool TryGet(string name, out MaterialPropertyValue value) => _values.TryGetValue(name, out value);

            internal static MaterialPropertySnapshot Read(Material material, List<AnalysisDiagnostic> diagnostics)
            {
                var snapshot = new MaterialPropertySnapshot(material);
                Shader shader = material.shader;
                if (shader != null)
                {
                    try
                    {
                        int count = ShaderUtil.GetPropertyCount(shader);
                        for (int index = 0; index < count; index++)
                        {
                            string name = ShaderUtil.GetPropertyName(shader, index);
                            ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, index);
                            // ShaderUtil added a named Int member in newer Unity releases. Comparing its
                            // name keeps this detector binary-compatible with the 2021.3 minimum.
                            if (type != ShaderUtil.ShaderPropertyType.Float &&
                                type != ShaderUtil.ShaderPropertyType.Range && type.ToString() != "Int")
                                continue;
                            if (!material.HasProperty(name))
                                continue;
                            snapshot._values[name] = new MaterialPropertyValue(name, material.GetFloat(name),
                                type.ToString(), true);
                        }
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                            $"Could not enumerate current shader properties: {exception.Message}", DetectorId));
                    }
                }

                try
                {
                    var serialized = new SerializedObject(material);
                    SerializedProperty floats = serialized.FindProperty("m_SavedProperties.m_Floats");
                    if (floats != null && floats.isArray)
                    {
                        for (int index = 0; index < floats.arraySize; index++)
                        {
                            SerializedProperty pair = floats.GetArrayElementAtIndex(index);
                            SerializedProperty first = pair.FindPropertyRelative("first");
                            SerializedProperty second = pair.FindPropertyRelative("second");
                            if (first == null || second == null || string.IsNullOrEmpty(first.stringValue))
                                continue;
                            if (!snapshot._values.ContainsKey(first.stringValue))
                            {
                                snapshot._values.Add(first.stringValue,
                                    new MaterialPropertyValue(first.stringValue, second.floatValue,
                                        "Serialized Float", false));
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                        $"Could not inspect serialized material floats: {exception.Message}", DetectorId));
                }
                return snapshot;
            }
        }

        private sealed class MaterialPropertyValue
        {
            internal MaterialPropertyValue(string name, float value, string typeName, bool direct)
            {
                Name = name;
                Value = value;
                TypeName = typeName;
                IsDirect = direct;
            }
            internal string Name { get; }
            internal float Value { get; }
            internal string TypeName { get; }
            internal bool IsDirect { get; }
            internal FeatureReadability Readability =>
                IsDirect ? FeatureReadability.Direct : FeatureReadability.SerializedOnly;
        }
    }
}
