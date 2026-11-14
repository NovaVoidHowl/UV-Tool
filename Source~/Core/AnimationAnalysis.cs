using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Nvh.UvTileScanner
{
    public enum AnimationScanScope { AvatarLinked, WholeProject }
    public enum AnimationBindingKind { FloatCurve, ObjectReferenceCurve }
    public enum AnimationMatchConfidence { Unknown, Probable, Confirmed }
    public enum AnimationClipBehaviour { Unknown, ConstantStateSetting, AnimatedTransition, MaterialSwap }

    public sealed class AnimationKeyframeValue
    {
        internal AnimationKeyframeValue(float time, float? floatValue, UnityEngine.Object objectValue)
        {
            Time = time;
            FloatValue = floatValue;
            ObjectValue = objectValue;
        }

        public float Time { get; }
        public float? FloatValue { get; }
        public UnityEngine.Object ObjectValue { get; }
        public string DisplayValue => FloatValue.HasValue
            ? FloatValue.Value.ToString("0.###")
            : ObjectValue != null ? ObjectValue.name : "None";
    }

    public sealed class AnimationClipSource
    {
        internal AnimationClipSource(UnityEngine.Object controller, string location, Transform bindingRoot)
        {
            Controller = controller;
            Location = location ?? "Unknown source";
            BindingRoot = bindingRoot;
        }

        public UnityEngine.Object Controller { get; }
        public string Location { get; }
        public Transform BindingRoot { get; }
    }

    public sealed class AnimationMaterialMatch
    {
        internal AnimationMaterialMatch(MeshMaterialSlot slot, AnimationMatchConfidence confidence,
            string evidence, ShaderAnalysisResult shaderAnalysis, ShaderFeatureAnalysis feature,
            ShaderTileControl tileControl)
        {
            Slot = slot;
            Confidence = confidence;
            Evidence = evidence ?? string.Empty;
            ShaderAnalysis = shaderAnalysis;
            Feature = feature;
            TileControl = tileControl;
        }

        public MeshMaterialSlot Slot { get; }
        public AnimationMatchConfidence Confidence { get; }
        public string Evidence { get; }
        public ShaderAnalysisResult ShaderAnalysis { get; }
        public ShaderFeatureAnalysis Feature { get; }
        public ShaderTileControl TileControl { get; }
        public string FeatureLabel => Feature == null ? null : Feature.DisplayName;
        public int? UdimNumber => TileControl == null ? (int?)null : TileControl.UdimNumber;
    }

    public sealed class AnimationBindingAnalysis
    {
        internal AnimationBindingAnalysis(AnimationClip clip, string assetPath, AnimationClipSource source,
            string bindingPath, Type componentType, string propertyName, AnimationBindingKind kind,
            GameObject targetGameObject, Renderer targetRenderer, int? materialSlotIndex,
            Material targetMaterial, List<AnimationKeyframeValue> keyframes, string diagnostic)
        {
            Clip = clip;
            AssetPath = assetPath ?? string.Empty;
            Source = source;
            BindingPath = bindingPath ?? string.Empty;
            ComponentType = componentType;
            PropertyName = propertyName ?? string.Empty;
            Kind = kind;
            TargetGameObject = targetGameObject;
            TargetRenderer = targetRenderer;
            MaterialSlotIndex = materialSlotIndex;
            TargetMaterial = targetMaterial;
            Keyframes = keyframes.AsReadOnly();
            Diagnostic = diagnostic;
            Matches = new List<AnimationMaterialMatch>().AsReadOnly();
        }

        public AnimationClip Clip { get; }
        public string AssetPath { get; }
        public AnimationClipSource Source { get; }
        public string BindingPath { get; }
        public Type ComponentType { get; }
        public string ComponentTypeName => ComponentType == null ? "Unknown" : ComponentType.Name;
        public string PropertyName { get; }
        public AnimationBindingKind Kind { get; }
        public GameObject TargetGameObject { get; }
        public Renderer TargetRenderer { get; }
        public int? MaterialSlotIndex { get; }
        public Material TargetMaterial { get; private set; }
        public IReadOnlyList<AnimationKeyframeValue> Keyframes { get; }
        public string Diagnostic { get; }
        public IReadOnlyList<AnimationMaterialMatch> Matches { get; private set; }
        public AnimationMatchConfidence BestConfidence { get; private set; }

        internal void SetMatches(List<AnimationMaterialMatch> matches)
        {
            Matches = matches.AsReadOnly();
            BestConfidence = AnimationMatchConfidence.Unknown;
            foreach (AnimationMaterialMatch match in matches)
            {
                if (match.Confidence > BestConfidence)
                    BestConfidence = match.Confidence;
            }
        }

        internal void SetResolvedMaterial(Material material)
        {
            if (TargetMaterial == null)
                TargetMaterial = material;
        }
    }

    public sealed class AnimationClipAnalysis
    {
        internal AnimationClipAnalysis(AnimationClip clip, string assetPath, List<AnimationClipSource> sources,
            List<AnimationBindingAnalysis> bindings)
        {
            Clip = clip;
            AssetPath = assetPath ?? string.Empty;
            Sources = sources.AsReadOnly();
            Bindings = bindings.AsReadOnly();
            Behaviour = DetermineBehaviour(bindings);
        }

        public AnimationClip Clip { get; }
        public string AssetPath { get; }
        public IReadOnlyList<AnimationClipSource> Sources { get; }
        public IReadOnlyList<AnimationBindingAnalysis> Bindings { get; }
        public AnimationClipBehaviour Behaviour { get; }
        public int RelatedBindingCount
        {
            get
            {
                int count = 0;
                foreach (AnimationBindingAnalysis binding in Bindings)
                    if (binding.Matches.Count > 0) count++;
                return count;
            }
        }

        private static AnimationClipBehaviour DetermineBehaviour(List<AnimationBindingAnalysis> bindings)
        {
            bool hasFloat = false;
            bool hasAnimatedFloat = false;
            foreach (AnimationBindingAnalysis binding in bindings)
            {
                if (binding.Kind == AnimationBindingKind.ObjectReferenceCurve && binding.Keyframes.Count > 0)
                    return AnimationClipBehaviour.MaterialSwap;
                if (binding.Kind != AnimationBindingKind.FloatCurve || binding.Keyframes.Count == 0)
                    continue;
                hasFloat = true;
                float first = binding.Keyframes[0].FloatValue ?? 0f;
                for (int index = 1; index < binding.Keyframes.Count; index++)
                {
                    float value = binding.Keyframes[index].FloatValue ?? first;
                    if (!Mathf.Approximately(first, value))
                    {
                        hasAnimatedFloat = true;
                        break;
                    }
                }
            }
            if (hasAnimatedFloat) return AnimationClipBehaviour.AnimatedTransition;
            return hasFloat ? AnimationClipBehaviour.ConstantStateSetting : AnimationClipBehaviour.Unknown;
        }
    }

    public sealed class AvatarAnimationAnalysisReport
    {
        internal AvatarAnimationAnalysisReport(AnimationScanScope scope, List<AnimationClipAnalysis> clips,
            List<AnalysisDiagnostic> diagnostics)
        {
            Scope = scope;
            Clips = clips.AsReadOnly();
            Diagnostics = diagnostics.AsReadOnly();
        }

        public AnimationScanScope Scope { get; }
        public IReadOnlyList<AnimationClipAnalysis> Clips { get; }
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
        public int BindingCount
        {
            get
            {
                int count = 0;
                foreach (AnimationClipAnalysis clip in Clips) count += clip.Bindings.Count;
                return count;
            }
        }
        public int RelatedClipCount
        {
            get
            {
                int count = 0;
                foreach (AnimationClipAnalysis clip in Clips)
                    if (clip.RelatedBindingCount > 0) count++;
                return count;
            }
        }
    }

    /// <summary>Read-only animation discovery and material-binding correlation for one avatar scan.</summary>
    public static class AvatarAnimationAnalyzer
    {
        private static readonly Regex IndexedMaterialPattern = new Regex(
            @"(?:materials|m_Materials)\.Array\.data\[(\d+)\]", RegexOptions.Compiled);
        private static readonly HashSet<string> IgnoredAnimationClipNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HumanoidDefault"
        };

        /// <summary>
        /// Animation clip names intentionally excluded from discovery. Add future known-unhelpful Unity defaults here
        /// instead of scattering special cases through the discovery paths.
        /// </summary>
        public static IReadOnlyCollection<string> IgnoredAnimationClipNames => IgnoredAnimationClipNameSet;

        public static AvatarAnimationAnalysisReport Analyze(AvatarUvScanReport report, AnimationScanScope scope)
        {
            AvatarAnimationAnalysisReport completed = null;
            IEnumerator operation = AnalyzeIncremental(report, scope, null, value => completed = value);
            while (operation.MoveNext()) { }
            return completed ?? new AvatarAnimationAnalysisReport(scope,
                new List<AnimationClipAnalysis>(), new List<AnalysisDiagnostic>());
        }

        /// <summary>Runs read-only animation analysis over editor updates. Each yielded step is safe to resume later.</summary>
        public static IEnumerator AnalyzeIncremental(AvatarUvScanReport report, AnimationScanScope scope,
            Action<float, string> progress, Action<AvatarAnimationAnalysisReport> completed)
        {
            if (report == null || report.Root == null)
            {
                completed?.Invoke(new AvatarAnimationAnalysisReport(scope,
                    new List<AnimationClipAnalysis>(), new List<AnalysisDiagnostic>()));
                yield break;
            }

            var diagnostics = new List<AnalysisDiagnostic>();
            Report(progress, 0.02f, "Discovering avatar-linked animation clips");
            Dictionary<AnimationClip, List<AnimationClipSource>> discovered = DiscoverAvatarLinked(report.Root, diagnostics);
            yield return null;

            if (scope == AnimationScanScope.WholeProject)
            {
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
                for (int index = 0; index < guids.Length; index++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        var clip = asset as AnimationClip;
                        if (clip != null && !discovered.ContainsKey(clip))
                            AddClip(discovered, clip, null, "Project asset search", report.Root.transform);
                    }
                    if (index % 8 == 0 || index == guids.Length - 1)
                    {
                        float portion = guids.Length == 0 ? 1f : (index + 1f) / guids.Length;
                        Report(progress, 0.05f + portion * 0.25f, "Discovering project animation clips");
                        yield return null;
                    }
                }
            }

            var slotContexts = CollectSlots(report);
            var matchesBySlot = new Dictionary<MeshMaterialSlot, List<AnimationBindingAnalysis>>();
            foreach (MaterialSlotContext context in slotContexts)
                matchesBySlot[context.Slot] = new List<AnimationBindingAnalysis>();

            var pairs = new List<KeyValuePair<AnimationClip, List<AnimationClipSource>>>(discovered);
            var clipResults = new List<AnimationClipAnalysis>();
            int sourceCount = 0;
            foreach (KeyValuePair<AnimationClip, List<AnimationClipSource>> pair in pairs)
                sourceCount += Math.Max(1, pair.Value.Count);
            int sourceIndex = 0;
            foreach (KeyValuePair<AnimationClip, List<AnimationClipSource>> pair in pairs)
            {
                AnimationClip clip = pair.Key;
                var bindings = new List<AnimationBindingAnalysis>();
                string assetPath = AssetDatabase.GetAssetPath(clip);
                foreach (AnimationClipSource source in pair.Value)
                {
                    ExtractBindings(clip, assetPath, source, slotContexts, matchesBySlot, bindings, diagnostics);
                    sourceIndex++;
                    float portion = sourceCount == 0 ? 1f : sourceIndex / (float)sourceCount;
                    Report(progress, 0.30f + portion * 0.68f, "Analyzing " + clip.name);
                    yield return null;
                }
                clipResults.Add(new AnimationClipAnalysis(clip, assetPath, pair.Value, bindings));
            }
            clipResults.Sort((left, right) => string.Compare(left.Clip.name, right.Clip.name, StringComparison.OrdinalIgnoreCase));

            foreach (KeyValuePair<MeshMaterialSlot, List<AnimationBindingAnalysis>> pair in matchesBySlot)
                pair.Key.AnimationBindings = pair.Value.AsReadOnly();

            var result = new AvatarAnimationAnalysisReport(scope, clipResults, diagnostics);
            Report(progress, 1f, "Animation analysis complete");
            completed?.Invoke(result);
        }

        private static Dictionary<AnimationClip, List<AnimationClipSource>> DiscoverAvatarLinked(GameObject root,
            List<AnalysisDiagnostic> diagnostics)
        {
            var clips = new Dictionary<AnimationClip, List<AnimationClipSource>>();
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController == null) continue;
                CollectController(animator.runtimeAnimatorController, animator.runtimeAnimatorController,
                    animator.transform, "Animator controller", clips, new HashSet<RuntimeAnimatorController>());
            }
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
            {
                try
                {
                    foreach (AnimationState state in animation)
                        AddClip(clips, state.clip, animation, $"Animation component state '{state.name}'", animation.transform);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                        $"Could not inspect Animation component on {animation.name}: {exception.Message}"));
                }
            }
            CollectDescriptorControllerReferences(root, clips, diagnostics);
            return clips;
        }

        private static void Report(Action<float, string> progress, float value, string message)
        {
            progress?.Invoke(Mathf.Clamp01(value), message ?? string.Empty);
        }

        private static void CollectDescriptorControllerReferences(GameObject root,
            Dictionary<AnimationClip, List<AnimationClipSource>> clips, List<AnalysisDiagnostic> diagnostics)
        {
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue; // Missing script.
                Type type = component.GetType();
                if (type.Name.IndexOf("Descriptor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    RuntimeAnimatorController controller = null;
                    try
                    {
                        if (member is FieldInfo field && typeof(RuntimeAnimatorController).IsAssignableFrom(field.FieldType))
                            controller = field.GetValue(component) as RuntimeAnimatorController;
                        else if (member is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0 &&
                                 typeof(RuntimeAnimatorController).IsAssignableFrom(property.PropertyType))
                            controller = property.GetValue(component, null) as RuntimeAnimatorController;
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Info,
                            $"Could not read optional descriptor controller {type.Name}.{member.Name}: {exception.Message}"));
                    }
                    if (controller != null)
                        CollectController(controller, controller, component.transform,
                            $"Descriptor {type.Name}.{member.Name}", clips, new HashSet<RuntimeAnimatorController>());
                }
            }
        }

        private static void CollectController(RuntimeAnimatorController controller, UnityEngine.Object sourceController,
            Transform root, string location, Dictionary<AnimationClip, List<AnimationClipSource>> clips,
            HashSet<RuntimeAnimatorController> visited)
        {
            if (controller == null || !visited.Add(controller)) return;
            var overrideController = controller as AnimatorOverrideController;
            if (overrideController != null)
            {
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(overrides);
                foreach (KeyValuePair<AnimationClip, AnimationClip> pair in overrides)
                {
                    if (pair.Value != null && pair.Value != pair.Key)
                        AddClip(clips, pair.Value, sourceController,
                            $"{location} override '{pair.Key.name}'", root);
                }
                CollectController(overrideController.runtimeAnimatorController, sourceController, root,
                    location + " base controller", clips, visited);
                return;
            }

            var animatorController = controller as AnimatorController;
            if (animatorController == null)
            {
                foreach (AnimationClip clip in controller.animationClips)
                    AddClip(clips, clip, sourceController, location, root);
                return;
            }

            var visitedMotions = new HashSet<Motion>();
            foreach (AnimatorControllerLayer layer in animatorController.layers)
                TraverseStateMachine(layer.stateMachine, sourceController, root,
                    $"{location} / Layer '{layer.name}'", clips, visitedMotions);
        }

        private static void TraverseStateMachine(AnimatorStateMachine machine, UnityEngine.Object controller,
            Transform root, string location, Dictionary<AnimationClip, List<AnimationClipSource>> clips,
            HashSet<Motion> visitedMotions)
        {
            if (machine == null) return;
            foreach (ChildAnimatorState state in machine.states)
                TraverseMotion(state.state.motion, controller, root,
                    location + " / State '" + state.state.name + "'", clips, visitedMotions);
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                TraverseStateMachine(child.stateMachine, controller, root,
                    location + " / State machine '" + child.stateMachine.name + "'", clips, visitedMotions);
        }

        private static void TraverseMotion(Motion motion, UnityEngine.Object controller, Transform root,
            string location, Dictionary<AnimationClip, List<AnimationClipSource>> clips, HashSet<Motion> visited)
        {
            if (motion == null) return;
            var clip = motion as AnimationClip;
            if (clip != null)
            {
                AddClip(clips, clip, controller, location, root);
                return;
            }
            if (!visited.Add(motion)) return;
            var blendTree = motion as BlendTree;
            if (blendTree == null) return;
            foreach (ChildMotion child in blendTree.children)
                TraverseMotion(child.motion, controller, root, location + " / Blend Tree '" + blendTree.name + "'", clips, visited);
        }

        private static void AddClip(Dictionary<AnimationClip, List<AnimationClipSource>> clips, AnimationClip clip,
            UnityEngine.Object controller, string location, Transform root)
        {
            if (clip == null || IgnoredAnimationClipNameSet.Contains(clip.name)) return;
            if (!clips.TryGetValue(clip, out List<AnimationClipSource> sources))
            {
                sources = new List<AnimationClipSource>();
                clips.Add(clip, sources);
            }
            foreach (AnimationClipSource source in sources)
            {
                if (source.Controller == controller && source.Location == location && source.BindingRoot == root)
                    return;
            }
            sources.Add(new AnimationClipSource(controller, location, root));
        }

        private static List<MaterialSlotContext> CollectSlots(AvatarUvScanReport report)
        {
            var result = new List<MaterialSlotContext>();
            foreach (MeshUvScanResult mesh in report.Meshes)
                foreach (MeshUsageInfo usage in mesh.UsageDetails)
                    foreach (MeshMaterialSlot slot in usage.MaterialSlots)
                        result.Add(new MaterialSlotContext(usage, slot));
            return result;
        }

        private static void ExtractBindings(AnimationClip clip, string assetPath, AnimationClipSource source,
            List<MaterialSlotContext> slots, Dictionary<MeshMaterialSlot, List<AnimationBindingAnalysis>> matchesBySlot,
            List<AnimationBindingAnalysis> result, List<AnalysisDiagnostic> diagnostics)
        {
            try
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var values = new List<AnimationKeyframeValue>();
                    if (curve != null)
                        foreach (Keyframe key in curve.keys)
                            values.Add(new AnimationKeyframeValue(key.time, key.value, null));
                    AddBinding(clip, assetPath, source, binding, AnimationBindingKind.FloatCurve, values,
                        curve == null ? "Float binding had no readable curve data." : null,
                        slots, matchesBySlot, result);
                }
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    ObjectReferenceKeyframe[] curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    var values = new List<AnimationKeyframeValue>();
                    if (curve != null)
                        foreach (ObjectReferenceKeyframe key in curve)
                            values.Add(new AnimationKeyframeValue(key.time, null, key.value));
                    AddBinding(clip, assetPath, source, binding, AnimationBindingKind.ObjectReferenceCurve, values,
                        curve == null ? "Object-reference binding had no readable curve data." : null,
                        slots, matchesBySlot, result);
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning,
                    $"Could not read animation bindings from {clip.name}: {exception.Message}"));
            }
        }

        private static void AddBinding(AnimationClip clip, string assetPath, AnimationClipSource source,
            EditorCurveBinding curveBinding, AnimationBindingKind kind, List<AnimationKeyframeValue> values,
            string diagnostic, List<MaterialSlotContext> slots,
            Dictionary<MeshMaterialSlot, List<AnimationBindingAnalysis>> matchesBySlot,
            List<AnimationBindingAnalysis> result)
        {
            GameObject target = ResolveTarget(source.BindingRoot, curveBinding.path);
            Renderer renderer = ResolveRenderer(target, curveBinding.type);
            int? slotIndex = ParseMaterialSlot(curveBinding.propertyName);
            Material targetMaterial = FindObjectMaterial(values);
            var binding = new AnimationBindingAnalysis(clip, assetPath, source, curveBinding.path,
                curveBinding.type, curveBinding.propertyName, kind, target, renderer, slotIndex,
                targetMaterial, values, diagnostic);
            List<AnimationMaterialMatch> matches = MatchBinding(binding, slots);
            binding.SetMatches(matches);
            if (matches.Count == 1)
                binding.SetResolvedMaterial(matches[0].Slot.Material);
            foreach (AnimationMaterialMatch match in matches)
                matchesBySlot[match.Slot].Add(binding);
            result.Add(binding);
        }

        private static List<AnimationMaterialMatch> MatchBinding(AnimationBindingAnalysis binding,
            List<MaterialSlotContext> slots)
        {
            var candidates = new List<MaterialSlotContext>();
            string property = ExtractShaderProperty(binding.PropertyName);
            foreach (MaterialSlotContext context in slots)
            {
                if (binding.TargetRenderer != null && context.Renderer != binding.TargetRenderer)
                    continue;
                if (binding.MaterialSlotIndex.HasValue && context.Slot.SlotIndex != binding.MaterialSlotIndex.Value)
                    continue;
                if (binding.TargetRenderer == null && string.IsNullOrEmpty(property))
                    continue;
                candidates.Add(context);
            }

            var matches = new List<AnimationMaterialMatch>();
            foreach (MaterialSlotContext candidate in candidates)
            {
                FindFeature(candidate.Slot, property, out ShaderAnalysisResult analysis,
                    out ShaderFeatureAnalysis feature, out ShaderTileControl control);
                bool isSwap = binding.Kind == AnimationBindingKind.ObjectReferenceCurve &&
                              binding.MaterialSlotIndex.HasValue;
                if (!isSwap && string.IsNullOrEmpty(property))
                    continue;
                if (binding.TargetRenderer != null && binding.MaterialSlotIndex.HasValue)
                {
                    matches.Add(new AnimationMaterialMatch(candidate.Slot, AnimationMatchConfidence.Confirmed,
                        $"Binding resolves to renderer {candidate.Renderer.name}, material slot {candidate.Slot.SlotIndex}.",
                        analysis, feature, control));
                }
                else if (binding.TargetRenderer != null)
                {
                    matches.Add(new AnimationMaterialMatch(candidate.Slot, AnimationMatchConfidence.Probable,
                        $"Binding resolves to renderer {candidate.Renderer.name}, but does not identify a material slot.",
                        analysis, feature, control));
                }
                else if (analysis != null)
                {
                    matches.Add(new AnimationMaterialMatch(candidate.Slot, AnimationMatchConfidence.Probable,
                        "Binding property matches a scanned shader feature, but its target renderer could not be resolved.",
                        analysis, feature, control));
                }
            }
            return matches;
        }

        private static void FindFeature(MeshMaterialSlot slot, string property, out ShaderAnalysisResult analysis,
            out ShaderFeatureAnalysis feature, out ShaderTileControl control)
        {
            analysis = null;
            feature = null;
            control = null;
            if (string.IsNullOrEmpty(property) || slot.ShaderAnalysis == null) return;
            foreach (ShaderAnalysisResult candidateAnalysis in slot.ShaderAnalysis.Results)
            foreach (ShaderFeatureAnalysis candidateFeature in candidateAnalysis.Features)
            foreach (ShaderTileControl candidateControl in candidateFeature.Controls)
            {
                if (!PropertyMatches(property, candidateControl.PropertyName)) continue;
                analysis = candidateAnalysis;
                feature = candidateFeature;
                control = candidateControl;
                return;
            }
        }

        private static bool PropertyMatches(string bindingProperty, string controlProperty) =>
            string.Equals(bindingProperty, controlProperty, StringComparison.Ordinal) ||
            bindingProperty.StartsWith(controlProperty + ".", StringComparison.Ordinal);

        private static GameObject ResolveTarget(Transform root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root.gameObject;
            Transform target = root.Find(path);
            return target == null ? null : target.gameObject;
        }

        private static Renderer ResolveRenderer(GameObject target, Type bindingType)
        {
            if (target == null || bindingType == null || !typeof(Renderer).IsAssignableFrom(bindingType)) return null;
            return target.GetComponent(bindingType) as Renderer ?? target.GetComponent<Renderer>();
        }

        private static int? ParseMaterialSlot(string propertyName)
        {
            Match match = IndexedMaterialPattern.Match(propertyName ?? string.Empty);
            return match.Success && int.TryParse(match.Groups[1].Value, out int slot) ? slot : (int?)null;
        }

        private static string ExtractShaderProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return string.Empty;
            int materialPrefix = propertyName.IndexOf("material.", StringComparison.Ordinal);
            if (materialPrefix >= 0) return propertyName.Substring(materialPrefix + "material.".Length);
            Match indexed = IndexedMaterialPattern.Match(propertyName);
            if (indexed.Success)
            {
                int after = indexed.Index + indexed.Length;
                if (after < propertyName.Length && propertyName[after] == '.') return propertyName.Substring(after + 1);
            }
            return propertyName.StartsWith("_") ? propertyName : string.Empty;
        }

        private static Material FindObjectMaterial(List<AnimationKeyframeValue> values)
        {
            Material material = null;
            foreach (AnimationKeyframeValue value in values)
            {
                var candidate = value.ObjectValue as Material;
                if (candidate == null) continue;
                if (material != null && material != candidate) return null;
                material = candidate;
            }
            return material;
        }

        private sealed class MaterialSlotContext
        {
            internal MaterialSlotContext(MeshUsageInfo usage, MeshMaterialSlot slot)
            {
                Usage = usage;
                Slot = slot;
            }
            internal MeshUsageInfo Usage { get; }
            internal MeshMaterialSlot Slot { get; }
            internal Renderer Renderer => Usage.Renderer;
        }
    }
}
