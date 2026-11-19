using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nvh.UvTileScanner.Conversion
{
    /// <summary>Opt-in Editor-only tracing. Never imports, validates, or changes the observed objects.</summary>
    [InitializeOnLoad]
    public static class PreviewMaterialDiagnostics
    {
        private const string EnabledKey = "NVH.UVTool.TracePreviewMaterials";
        private const string MenuPath = "NVH/UV Tile Scanner/Development/Trace Preview Material Events";
        private static readonly HashSet<FeatureVisibilityPreviewSession> Sessions =
            new HashSet<FeatureVisibilityPreviewSession>();

        static PreviewMaterialDiagnostics()
        {
            EditorApplication.projectChanged += ProjectChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += AfterReload;
            if (Enabled) Capture("diagnostics initialized after load");
        }

        // SessionState survives assembly reloads, but does not enable logging in future Editor sessions.
        public static bool Enabled
        {
            get => SessionState.GetBool(EnabledKey, false);
            set
            {
                SessionState.SetBool(EnabledKey, value);
                if (value) Capture("trace enabled");
            }
        }

        public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Library/UVTool/PreviewMaterialTrace.log"));

        [MenuItem(MenuPath)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        internal static void Register(FeatureVisibilityPreviewSession session) => Sessions.Add(session);
        internal static void Unregister(FeatureVisibilityPreviewSession session) => Sessions.Remove(session);

        private static void ProjectChanged() => Capture("EditorApplication.projectChanged");
        private static void BeforeReload() => Capture("AssemblyReloadEvents.beforeAssemblyReload");
        private static void AfterReload() => Capture("AssemblyReloadEvents.afterAssemblyReload");

        public static void Capture(string reason)
        {
            if (!Enabled) return;
            var text = new StringBuilder();
            text.AppendLine($"{DateTime.UtcNow:O} | {reason} | editorTime={EditorApplication.timeSinceStartup:R}" +
                $" | updating={EditorApplication.isUpdating} compiling={EditorApplication.isCompiling}" +
                $" | event={Event.current?.type.ToString() ?? "none"}");
            var seen = new HashSet<int>();
            foreach (FeatureVisibilityPreviewSession session in Sessions)
            {
                if (!session.IsActive) continue;
                text.AppendLine($"session clone={session.PreviewInstance.GetInstanceID()} mode={session.MaterialMode}");
                foreach (Renderer renderer in session.PreviewInstance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    text.Append($"renderer {renderer.name} id={renderer.GetInstanceID()} slots=");
                    foreach (Material material in materials) text.Append((material == null ? 0 : material.GetInstanceID()) + ",");
                    text.AppendLine();
                    foreach (Material material in materials)
                    {
                        if (material == null || !seen.Add(material.GetInstanceID())) continue;
                        text.AppendLine($"material {material.name} id={material.GetInstanceID()} shader={material.shader?.name}" +
                            $" shaderId={material.shader?.GetInstanceID()} flags={material.globalIlluminationFlags}" +
                            $" keywords={string.Join(",", material.shaderKeywords)} hideFlags={material.hideFlags}");
                        text.AppendLine(EditorJsonUtility.ToJson(material));
                        foreach (string property in material.GetTexturePropertyNames())
                        {
                            Texture texture = material.GetTexture(property);
                            if (texture != null)
                                text.AppendLine($"texture {property} id={texture.GetInstanceID()} path={AssetDatabase.GetAssetPath(texture)}");
                        }
                    }
                }
            }
            // Library output cannot itself cause an asset import. Tracing must never break preview cleanup.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, text.ToString());
            }
            catch (IOException exception) { Debug.LogWarning("[UV Preview Trace] " + exception.Message); }
            catch (UnauthorizedAccessException exception) { Debug.LogWarning("[UV Preview Trace] " + exception.Message); }
            Debug.Log("[UV Preview Trace] " + reason + " (" + LogPath + ")");
        }
    }

    internal sealed class PreviewMaterialAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom, bool didDomainReload)
        {
            if (!PreviewMaterialDiagnostics.Enabled) return;
            PreviewMaterialDiagnostics.Capture("OnPostprocessAllAssets didDomainReload=" + didDomainReload +
                " imported=[" + string.Join(",", imported) + "] deleted=[" + string.Join(",", deleted) +
                "] moved=[" + string.Join(",", moved) + "] movedFrom=[" + string.Join(",", movedFrom) + "]");
            EditorApplication.delayCall += CaptureAfterImport;
        }

        private static void CaptureAfterImport() => PreviewMaterialDiagnostics.Capture("postprocess delayCall");
    }
}
