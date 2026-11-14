using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace Nvh.UvTileScanner
{
    public enum AnimationPropertyFilter
    {
        UvControls,
        AllProperties,
        OtherProperties
    }

    /// <summary>One material-target occurrence of an analysed animation binding.</summary>
    public sealed class AnimationFilteredBinding
    {
        internal AnimationFilteredBinding(AnimationClipAnalysis clip, AnimationBindingAnalysis binding,
            MeshMaterialSlot slot, List<AnimationMaterialMatch> matches, bool isUvControl, string searchText)
        {
            Clip = clip;
            Binding = binding;
            Slot = slot;
            Matches = matches.AsReadOnly();
            IsUvControl = isUvControl;
            SearchText = searchText ?? string.Empty;
            ComplementaryEvidence = new List<string>().AsReadOnly();
        }

        public AnimationClipAnalysis Clip { get; }
        public AnimationBindingAnalysis Binding { get; }
        public MeshMaterialSlot Slot { get; }
        public Renderer TargetRenderer => Binding.TargetRenderer;
        public Material TargetMaterial => Slot != null && Slot.Material != null ? Slot.Material : Binding.TargetMaterial;
        public IReadOnlyList<AnimationMaterialMatch> Matches { get; }
        public bool IsUvControl { get; }
        public IReadOnlyList<string> ComplementaryEvidence { get; private set; }
        internal string SearchText { get; }

        internal void SetComplementaryEvidence(List<string> evidence)
        {
            ComplementaryEvidence = evidence.AsReadOnly();
        }
    }

    public sealed class AnimationTargetGroup
    {
        internal AnimationTargetGroup(Renderer renderer, MeshMaterialSlot slot, string bindingPath,
            List<AnimationFilteredBinding> bindings)
        {
            Renderer = renderer;
            Slot = slot;
            BindingPath = bindingPath ?? string.Empty;
            Bindings = bindings.AsReadOnly();
        }

        public Renderer Renderer { get; }
        public MeshMaterialSlot Slot { get; }
        public Material Material => Slot == null ? null : Slot.Material;
        public int? MaterialSlotIndex => Slot == null ? (int?)null : Slot.SlotIndex;
        public string BindingPath { get; }
        public IReadOnlyList<AnimationFilteredBinding> Bindings { get; }
    }

    public sealed class AnimationClipGroup
    {
        internal AnimationClipGroup(AnimationClipAnalysis clip, List<AnimationTargetGroup> targets,
            int visibleBindingCount)
        {
            Clip = clip;
            Targets = targets.AsReadOnly();
            VisibleBindingCount = visibleBindingCount;
        }

        public AnimationClipAnalysis Clip { get; }
        public IReadOnlyList<AnimationTargetGroup> Targets { get; }
        public int VisibleBindingCount { get; }
    }

    public sealed class AnimationFilterResult
    {
        private readonly Dictionary<MeshMaterialSlot, IReadOnlyList<AnimationFilteredBinding>> _bindingsBySlot;

        internal AnimationFilterResult(AnimationPropertyFilter category, string search,
            List<AnimationClipGroup> clips, int totalMatchingBindingCount, int uvFeatureBindingCount,
            int otherMaterialBindingCount, int visibleBindingCount,
            Dictionary<MeshMaterialSlot, List<AnimationFilteredBinding>> bindingsBySlot)
        {
            Category = category;
            Search = search ?? string.Empty;
            Clips = clips.AsReadOnly();
            TotalMatchingBindingCount = totalMatchingBindingCount;
            UvFeatureBindingCount = uvFeatureBindingCount;
            OtherMaterialBindingCount = otherMaterialBindingCount;
            VisibleBindingCount = visibleBindingCount;
            _bindingsBySlot = new Dictionary<MeshMaterialSlot, IReadOnlyList<AnimationFilteredBinding>>();
            foreach (KeyValuePair<MeshMaterialSlot, List<AnimationFilteredBinding>> pair in bindingsBySlot)
                _bindingsBySlot[pair.Key] = pair.Value.AsReadOnly();
        }

        public AnimationPropertyFilter Category { get; }
        public string Search { get; }
        public IReadOnlyList<AnimationClipGroup> Clips { get; }
        public int TotalMatchingBindingCount { get; }
        public int UvFeatureBindingCount { get; }
        public int OtherMaterialBindingCount { get; }
        public int VisibleBindingCount { get; }

        public IReadOnlyList<AnimationFilteredBinding> GetBindingsForSlot(MeshMaterialSlot slot)
        {
            return slot != null && _bindingsBySlot.TryGetValue(slot, out IReadOnlyList<AnimationFilteredBinding> bindings)
                ? bindings : Array.Empty<AnimationFilteredBinding>();
        }
    }

    /// <summary>
    /// Cached, detector-neutral animation binding index. Filtering never performs AssetDatabase work and cached
    /// results are reused when a category/search combination is revisited.
    /// </summary>
    public sealed class AnimationAnalysisFilterIndex
    {
        private sealed class MutableTargetGroup
        {
            internal Renderer Renderer;
            internal MeshMaterialSlot Slot;
            internal string BindingPath;
            internal readonly List<AnimationFilteredBinding> Bindings = new List<AnimationFilteredBinding>();
        }

        private readonly AvatarAnimationAnalysisReport _report;
        private readonly Dictionary<AnimationClipAnalysis, List<AnimationFilteredBinding>> _entriesByClip =
            new Dictionary<AnimationClipAnalysis, List<AnimationFilteredBinding>>();
        private readonly Dictionary<string, AnimationFilterResult> _cache =
            new Dictionary<string, AnimationFilterResult>(StringComparer.Ordinal);

        public AnimationAnalysisFilterIndex(AvatarAnimationAnalysisReport report)
        {
            _report = report;
            BuildIndex();
        }

        public int TotalMatchingBindingCount { get; private set; }
        public int UvFeatureBindingCount { get; private set; }
        public int OtherMaterialBindingCount { get; private set; }

        public AnimationFilterResult Filter(AnimationPropertyFilter category, string search = null)
        {
            string normalizedSearch = (search ?? string.Empty).Trim();
            string cacheKey = ((int)category).ToString() + "\n" + normalizedSearch.ToUpperInvariant();
            if (_cache.TryGetValue(cacheKey, out AnimationFilterResult cached))
                return cached;

            if (_cache.Count >= 32)
                _cache.Clear();

            var clips = new List<AnimationClipGroup>();
            var visibleBindings = new HashSet<AnimationBindingAnalysis>();
            var bindingsBySlot = new Dictionary<MeshMaterialSlot, List<AnimationFilteredBinding>>();
            if (_report != null)
            {
                foreach (AnimationClipAnalysis clip in _report.Clips)
                {
                    if (!_entriesByClip.TryGetValue(clip, out List<AnimationFilteredBinding> entries))
                        continue;

                    var targets = new List<MutableTargetGroup>();
                    var clipBindings = new HashSet<AnimationBindingAnalysis>();
                    foreach (AnimationFilteredBinding entry in entries)
                    {
                        if (!MatchesCategory(entry, category) || !MatchesSearch(entry, normalizedSearch))
                            continue;

                        clipBindings.Add(entry.Binding);
                        visibleBindings.Add(entry.Binding);
                        MutableTargetGroup target = FindTarget(targets, entry);
                        target.Bindings.Add(entry);
                        if (entry.Slot != null)
                        {
                            if (!bindingsBySlot.TryGetValue(entry.Slot, out List<AnimationFilteredBinding> slotBindings))
                            {
                                slotBindings = new List<AnimationFilteredBinding>();
                                bindingsBySlot.Add(entry.Slot, slotBindings);
                            }
                            slotBindings.Add(entry);
                        }
                    }

                    if (targets.Count == 0)
                        continue;
                    var finalTargets = new List<AnimationTargetGroup>(targets.Count);
                    foreach (MutableTargetGroup target in targets)
                        finalTargets.Add(new AnimationTargetGroup(target.Renderer, target.Slot,
                            target.BindingPath, target.Bindings));
                    clips.Add(new AnimationClipGroup(clip, finalTargets, clipBindings.Count));
                }
            }

            var result = new AnimationFilterResult(category, normalizedSearch, clips,
                TotalMatchingBindingCount, UvFeatureBindingCount, OtherMaterialBindingCount,
                visibleBindings.Count, bindingsBySlot);
            _cache.Add(cacheKey, result);
            return result;
        }

        private void BuildIndex()
        {
            if (_report == null)
                return;

            var allEntries = new List<AnimationFilteredBinding>();
            var countedBindings = new HashSet<AnimationBindingAnalysis>();
            var uvBindings = new HashSet<AnimationBindingAnalysis>();
            foreach (AnimationClipAnalysis clip in _report.Clips)
            {
                var clipEntries = new List<AnimationFilteredBinding>();
                foreach (AnimationBindingAnalysis binding in clip.Bindings)
                {
                    if (binding.Matches.Count == 0)
                        continue;

                    bool isUvControl = false;
                    foreach (AnimationMaterialMatch match in binding.Matches)
                        if (match.Feature != null)
                            isUvControl = true;
                    countedBindings.Add(binding);
                    if (isUvControl)
                        uvBindings.Add(binding);

                    var matchesBySlot = new Dictionary<MeshMaterialSlot, List<AnimationMaterialMatch>>();
                    foreach (AnimationMaterialMatch match in binding.Matches)
                    {
                        MeshMaterialSlot slot = match.Slot;
                        if (!matchesBySlot.TryGetValue(slot, out List<AnimationMaterialMatch> slotMatches))
                        {
                            slotMatches = new List<AnimationMaterialMatch>();
                            matchesBySlot.Add(slot, slotMatches);
                        }
                        slotMatches.Add(match);
                    }
                    foreach (KeyValuePair<MeshMaterialSlot, List<AnimationMaterialMatch>> pair in matchesBySlot)
                    {
                        var entry = new AnimationFilteredBinding(clip, binding, pair.Key, pair.Value,
                            isUvControl, BuildSearchText(clip, binding, pair.Key, pair.Value));
                        clipEntries.Add(entry);
                        allEntries.Add(entry);
                    }
                }
                if (clipEntries.Count > 0)
                    _entriesByClip.Add(clip, clipEntries);
            }

            TotalMatchingBindingCount = countedBindings.Count;
            UvFeatureBindingCount = uvBindings.Count;
            OtherMaterialBindingCount = TotalMatchingBindingCount - UvFeatureBindingCount;
            BuildComplementaryEvidence(allEntries);
        }

        private static MutableTargetGroup FindTarget(List<MutableTargetGroup> targets, AnimationFilteredBinding entry)
        {
            foreach (MutableTargetGroup target in targets)
            {
                if (ReferenceEquals(target.Slot, entry.Slot) &&
                    ReferenceEquals(target.Renderer, entry.TargetRenderer) &&
                    string.Equals(target.BindingPath, entry.Binding.BindingPath, StringComparison.Ordinal))
                    return target;
            }
            var created = new MutableTargetGroup
            {
                Renderer = entry.TargetRenderer,
                Slot = entry.Slot,
                BindingPath = entry.Binding.BindingPath
            };
            targets.Add(created);
            return created;
        }

        private static bool MatchesCategory(AnimationFilteredBinding entry, AnimationPropertyFilter category)
        {
            switch (category)
            {
                case AnimationPropertyFilter.UvControls: return entry.IsUvControl;
                case AnimationPropertyFilter.OtherProperties: return !entry.IsUvControl;
                default: return true;
            }
        }

        private static bool MatchesSearch(AnimationFilteredBinding entry, string search)
        {
            return string.IsNullOrEmpty(search) ||
                   entry.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildSearchText(AnimationClipAnalysis clip, AnimationBindingAnalysis binding,
            MeshMaterialSlot slot, List<AnimationMaterialMatch> matches)
        {
            var text = new StringBuilder();
            Append(text, clip.Clip != null ? clip.Clip.name : null);
            Append(text, clip.AssetPath);
            Append(text, binding.BindingPath);
            Append(text, binding.PropertyName);
            Append(text, binding.TargetRenderer != null ? binding.TargetRenderer.name : null);
            Append(text, slot != null && slot.Material != null ? slot.Material.name : null);
            foreach (AnimationMaterialMatch match in matches)
            {
                Append(text, match.FeatureLabel);
                Append(text, match.ShaderAnalysis == null ? null : match.ShaderAnalysis.DetectorName);
                if (match.TileControl != null)
                {
                    Append(text, "UDIM " + match.TileControl.UdimNumber);
                    Append(text, "row " + match.TileControl.Row);
                    Append(text, "column " + match.TileControl.Column);
                }
            }
            return text.ToString();
        }

        private static void Append(StringBuilder text, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (text.Length > 0)
                text.Append('\n');
            text.Append(value);
        }

        private static void BuildComplementaryEvidence(List<AnimationFilteredBinding> entries)
        {
            var byTile = new Dictionary<string, List<AnimationFilteredBinding>>(StringComparer.Ordinal);
            foreach (AnimationFilteredBinding entry in entries)
            {
                if (!TryGetConstantValue(entry.Binding, out float value))
                    continue;
                foreach (AnimationMaterialMatch match in entry.Matches)
                {
                    if (match.Feature == null || match.TileControl == null)
                        continue;
                    string detector = match.ShaderAnalysis == null ? string.Empty : match.ShaderAnalysis.DetectorId;
                    string key = RuntimeHelpers.GetHashCode(entry.Slot) + "|" + detector + "|" +
                                 (int)match.Feature.Type + "|" + match.TileControl.UdimNumber;
                    if (!byTile.TryGetValue(key, out List<AnimationFilteredBinding> group))
                    {
                        group = new List<AnimationFilteredBinding>();
                        byTile.Add(key, group);
                    }
                    if (!group.Contains(entry))
                        group.Add(entry);
                }
            }

            foreach (List<AnimationFilteredBinding> group in byTile.Values)
            {
                foreach (AnimationFilteredBinding entry in group)
                {
                    TryGetConstantValue(entry.Binding, out float ownValue);
                    var evidence = new List<string>();
                    foreach (AnimationFilteredBinding other in group)
                    {
                        if (ReferenceEquals(entry.Binding, other.Binding) ||
                            ReferenceEquals(entry.Clip, other.Clip) ||
                            !TryGetConstantValue(other.Binding, out float otherValue) ||
                            !AreOppositeControlValues(ownValue, otherValue))
                            continue;
                        string clipName = other.Clip.Clip != null ? other.Clip.Clip.name : "Missing clip reference";
                        string label = clipName + " uses " + otherValue.ToString("0.###") +
                                       " for the same feature tile";
                        if (!evidence.Contains(label))
                            evidence.Add(label);
                    }
                    entry.SetComplementaryEvidence(evidence);
                }
            }
        }

        private static bool TryGetConstantValue(AnimationBindingAnalysis binding, out float value)
        {
            value = 0f;
            if (binding.Kind != AnimationBindingKind.FloatCurve || binding.Keyframes.Count == 0 ||
                !binding.Keyframes[0].FloatValue.HasValue)
                return false;
            value = binding.Keyframes[0].FloatValue.Value;
            for (int index = 1; index < binding.Keyframes.Count; index++)
                if (!binding.Keyframes[index].FloatValue.HasValue ||
                    !Mathf.Approximately(value, binding.Keyframes[index].FloatValue.Value))
                    return false;
            return true;
        }

        private static bool AreOppositeControlValues(float first, float second)
        {
            return (Mathf.Approximately(first, 0f) && Mathf.Approximately(second, 1f)) ||
                   (Mathf.Approximately(first, 1f) && Mathf.Approximately(second, 0f)) ||
                   (Mathf.Approximately(first, -1f) && Mathf.Approximately(second, 1f)) ||
                   (Mathf.Approximately(first, 1f) && Mathf.Approximately(second, -1f));
        }
    }
}
