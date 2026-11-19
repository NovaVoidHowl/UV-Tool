using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace Nvh.UvTileScanner.Conversion
{
    public enum PreviewMaterialMode { Original, Fallback, Diagnostic }

    public sealed class PreviewLegendEntry
    {
        internal PreviewLegendEntry(string group, string renderer, int sourceSlot, int output, string tiles)
        { GroupId = group; RendererPath = renderer; SourceMaterialSlot = sourceSlot; OutputSubMesh = output; Tiles = tiles; }
        public string GroupId { get; }
        public string RendererPath { get; }
        public int SourceMaterialSlot { get; }
        public int OutputSubMesh { get; }
        public string Tiles { get; }
        public Color Colour => PreviewMaterialCache.GroupColour(GroupId);
    }

    // Owns only generated, in-memory materials; source references are cache keys, never edited.
    public sealed class PreviewMaterialCache : IDisposable
    {
        private readonly Dictionary<Material, Material> _fallback = new Dictionary<Material, Material>();
        private readonly Dictionary<string, Material> _diagnostic = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly List<string> _warnings = new List<string>();
        private Material _missing;
        private Material _unmapped;
        public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

        public static Color GroupColour(string id)
        {
            uint hash = 2166136261;
            foreach (char c in id ?? string.Empty) { hash ^= c; hash *= 16777619; }
            // Avalanche adjacent identifiers before selecting hue, so e.g. group-1/group-2 differ.
            hash ^= hash >> 16; hash *= 0x7feb352d; hash ^= hash >> 15;
            hash *= 0x846ca68b; hash ^= hash >> 16;
            return Color.HSVToRGB((hash % 16777216) / 16777216f, .65f + ((hash >> 24) % 3) * .1f, .95f);
        }

        private static Material Create(string name, string shader)
        {
            Shader found = Shader.Find(shader);
            if (found == null || !found.isSupported) throw new InvalidOperationException("Preview shader unavailable: " + shader);
            return new Material(found) { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        public Material Unmapped()
        {
            if (_unmapped == null)
            {
                _unmapped = Create("UV Diagnostic: unmapped", "Unlit/Color");
                _unmapped.color = Color.gray;
            }
            return _unmapped;
        }

        public Material Diagnostic(string group)
        {
            if (!_diagnostic.TryGetValue(group, out Material material))
            {
                material = Create("UV Diagnostic: " + group, "Unlit/Color");
                _diagnostic.Add(group, material);
                material.color = GroupColour(group);
            }
            return material;
        }

        // Validate both property presence and shader property type, including unfamiliar/locked shaders.
        private static string Property(Material source, ShaderPropertyType type, params string[] names)
        {
            if (source == null || source.shader == null) return null;
            foreach (string name in names)
            {
                int index = source.shader.FindPropertyIndex(name);
                if (source.HasProperty(name) && index >= 0 && source.shader.GetPropertyType(index) == type) return name;
            }
            return null;
        }

        public Material Fallback(Material source)
        {
            if (source == null && _missing != null) return _missing;
            if (source != null && _fallback.TryGetValue(source, out Material cached)) return cached;
            Material result = Create("UV Fallback: " + (source == null ? "Missing source" : source.name), "Standard");
            if (source == null) _missing = result; else _fallback.Add(source, result);
            bool baseMapped = false;
            string colour = Property(source, ShaderPropertyType.Color, "_Color", "_BaseColor");
            if (colour != null) { result.color = source.GetColor(colour); baseMapped = true; }
            string main = CopyTexture(source, result, "_MainTex", "_MainTex", "_BaseMap");
            if (main != null) baseMapped = true;
            if (!baseMapped)
            {
                result.color = Color.magenta;
                _warnings.Add(result.name + ": no reliable base colour/texture properties; using magenta.");
            }
            CopyFloat(source, result, "_Metallic", "_Metallic");
            CopyFloat(source, result, "_Glossiness", "_Glossiness", "_Smoothness");
            if (CopyTexture(source, result, "_BumpMap", "_BumpMap") != null)
            { result.EnableKeyword("_NORMALMAP"); CopyFloat(source, result, "_BumpScale", "_BumpScale"); }
            if (CopyTexture(source, result, "_MetallicGlossMap", "_MetallicGlossMap") != null)
                result.EnableKeyword("_METALLICGLOSSMAP");
            CopyTexture(source, result, "_EmissionMap", "_EmissionMap");
            string emission = Property(source, ShaderPropertyType.Color, "_EmissionColor");
            if (emission != null) result.SetColor("_EmissionColor", source.GetColor(emission));
            // Standard validation derives _EMISSION from the GI flags, not merely from a stored map/colour.
            // In particular, locked/custom shaders can retain bright emission data while emission is disabled.
            // Keep the source's explicit emission state and normalize before the first renderer assignment.
            result.globalIlluminationFlags = source == null
                ? MaterialGlobalIlluminationFlags.EmissiveIsBlack : source.globalIlluminationFlags;
            MaterialEditor.FixupEmissiveFlag(result);
            bool emissionEnabled = (result.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
            if (emissionEnabled) result.EnableKeyword("_EMISSION");
            else result.DisableKeyword("_EMISSION");
            if (!emissionEnabled && (result.GetTexture("_EmissionMap") != null ||
                result.GetColor("_EmissionColor").maxColorComponent > 0))
                _warnings.Add(result.name + ": stored emission properties copied but emission is disabled by source GI flags. " +
                    "Custom/animated emission enablement and masks are not translated.");
            result.SetOverrideTag("RenderType", string.Empty);
            string mode = Property(source, ShaderPropertyType.Float, "_Mode");
            if (mode != null && source.shader.name == "Standard")
            {
                int value = (int)source.GetFloat(mode);
                result.SetFloat("_Mode", value);
                CopyFloat(source, result, "_Cutoff", "_Cutoff");
                if (value == 1)
                {
                    result.SetOverrideTag("RenderType", "TransparentCutout");
                    result.EnableKeyword("_ALPHATEST_ON"); result.renderQueue = 2450;
                }
                else if (value == 2 || value == 3)
                {
                    result.SetOverrideTag("RenderType", "Transparent");
                    result.SetInt("_SrcBlend", (int)(value == 3 ? BlendMode.One : BlendMode.SrcAlpha));
                    result.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    result.SetInt("_ZWrite", 0);
                    result.EnableKeyword(value == 3 ? "_ALPHAPREMULTIPLY_ON" : "_ALPHABLEND_ON"); result.renderQueue = 3000;
                }
            }
            if (source != null && source.shader != null && source.shader.name != "Standard")
                _warnings.Add(source.name + ": only exposed compatible properties copied; baked/locked values, custom transparency, matcaps, outlines, hue, lighting and UV dissolve/discard are not translated. Missing optional maps/values use Standard defaults.");
            return result;
        }

        private static string CopyTexture(Material source, Material target, string destination, params string[] names)
        {
            string name = Property(source, ShaderPropertyType.Texture, names);
            if (name == null || source.GetTexture(name) == null) return null;
            target.SetTexture(destination, source.GetTexture(name));
            target.SetTextureScale(destination, source.GetTextureScale(name));
            target.SetTextureOffset(destination, source.GetTextureOffset(name));
            return name;
        }
        private static void CopyFloat(Material source, Material target, string destination, params string[] names)
        {
            string name = Property(source, ShaderPropertyType.Float, names) ?? Property(source, ShaderPropertyType.Range, names);
            if (name != null) target.SetFloat(destination, source.GetFloat(name));
        }
        public void Dispose()
        {
            foreach (Material m in _fallback.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            foreach (Material m in _diagnostic.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            if (_missing != null) UnityEngine.Object.DestroyImmediate(_missing);
            if (_unmapped != null) UnityEngine.Object.DestroyImmediate(_unmapped);
            _unmapped = null;
            _fallback.Clear(); _diagnostic.Clear(); _missing = null;
        }
    }
}
