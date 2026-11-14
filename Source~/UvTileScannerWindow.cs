using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nvh.UvTileScanner
{
    public sealed class UvTileScannerWindow : EditorWindow
    {
        private const string WindowTitle = "UV Tile Scanner";
        private const string UxmlGuid = "9084d5f243624d4fb8aa5b1f2cf47730";
        private const string UssGuid = "cc49e43249c140d7a5f1aa623b3c811c";

        [SerializeField] private GameObject _avatarRoot;
        [SerializeField] private bool _includeInactive = true;
        [SerializeField] private bool _wholeProjectAnimations;
        [SerializeField] private bool _onlyMultipleTiles;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private AnimationPropertyFilter _animationPropertyFilter = AnimationPropertyFilter.UvControls;
        [SerializeField] private string _animationSearch = string.Empty;

        private AvatarUvScanReport _report;
        private ObjectField _avatarRootField;
        private Toggle _includeInactiveToggle;
        private Toggle _wholeProjectAnimationsToggle;
        private Toggle _multipleTilesToggle;
        private ToolbarSearchField _searchField;
        private VisualElement _animationFilterToolbar;
        private DropdownField _animationCategoryFilter;
        private ToolbarSearchField _animationSearchField;
        private Label _animationFilterCounts;
        private Button _scanButton;
        private Button _clearButton;
        private Label _versionBadge;
        private VisualElement _scanProgress;
        private Label _scanProgressLabel;
        private ProgressBar _scanProgressBar;
        private Button _cancelScanButton;
        private VisualElement _emptyState;
        private VisualElement _reportContent;
        private Label _summaryTitle;
        private Label _summaryCounts;
        private Label _missingMeshesWarning;
        private VisualElement _animationResults;
        private VisualElement _results;
        private AvatarAnimationAnalysisReport _indexedAnimationReport;
        private AnimationAnalysisFilterIndex _animationFilterIndex;
        private AnimationFilterResult _animationFilterResult;
        private readonly List<MaterialAnimationView> _materialAnimationViews = new List<MaterialAnimationView>();
        private IEnumerator _meshScan;
        private IEnumerator _animationScan;
        private AnimationScanScope _animationScope;
        private bool _isScanning;
        private float _scanProgressValue;
        private string _scanProgressText;

        private sealed class MaterialAnimationView
        {
            internal MeshMaterialSlot Slot;
            internal Foldout Foldout;
            internal VisualElement Body;
        }

        [MenuItem("NVH/UV Tile Scanner")]
        public static void Open()
        {
            GetWindow<UvTileScannerWindow>(WindowTitle).minSize = new Vector2(520f, 340f);
        }

        [MenuItem("GameObject/Scan second UV tiles", false, 30)]
        private static void ScanSelected()
        {
            UvTileScannerWindow window = GetWindow<UvTileScannerWindow>(WindowTitle);
            window._avatarRoot = Selection.activeGameObject;
            window.SyncInputControls();
            window.RunScan();
        }

        [MenuItem("GameObject/Scan second UV tiles", true)]
        private static bool CanScanSelected() => Selection.activeGameObject != null;

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            VisualTreeAsset layout = LoadAssetByGuid<VisualTreeAsset>(UxmlGuid);
            StyleSheet styleSheet = LoadAssetByGuid<StyleSheet>(UssGuid);
            if (layout == null || styleSheet == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "The UV Tile Scanner UI assets could not be loaded. Reinstall the complete package, including Editor/UI.",
                    HelpBoxMessageType.Error));
                return;
            }

            layout.CloneTree(rootVisualElement);
            rootVisualElement.styleSheets.Add(styleSheet);
            CacheControls();
            ConfigureControls();
            SyncInputControls();
            RefreshView();
        }

        private static T LoadAssetByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private void CacheControls()
        {
            _avatarRootField = rootVisualElement.Q<ObjectField>("avatar-root-field");
            _includeInactiveToggle = rootVisualElement.Q<Toggle>("include-inactive-toggle");
            _wholeProjectAnimationsToggle = rootVisualElement.Q<Toggle>("whole-project-animations-toggle");
            _multipleTilesToggle = rootVisualElement.Q<Toggle>("multiple-tiles-toggle");
            _searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
            _animationFilterToolbar = rootVisualElement.Q("animation-filter-toolbar");
            _animationCategoryFilter = rootVisualElement.Q<DropdownField>("animation-category-filter");
            _animationSearchField = rootVisualElement.Q<ToolbarSearchField>("animation-search-field");
            _animationFilterCounts = rootVisualElement.Q<Label>("animation-filter-counts");
            _scanButton = rootVisualElement.Q<Button>("scan-button");
            _clearButton = rootVisualElement.Q<Button>("clear-button");
            _versionBadge = rootVisualElement.Q<Label>("tool-version");
            _scanProgress = rootVisualElement.Q("scan-progress");
            _scanProgressLabel = rootVisualElement.Q<Label>("scan-progress-label");
            _scanProgressBar = rootVisualElement.Q<ProgressBar>("scan-progress-bar");
            _cancelScanButton = rootVisualElement.Q<Button>("cancel-scan-button");
            _emptyState = rootVisualElement.Q("empty-state");
            _reportContent = rootVisualElement.Q("report-content");
            _summaryTitle = rootVisualElement.Q<Label>("summary-title");
            _summaryCounts = rootVisualElement.Q<Label>("summary-counts");
            _missingMeshesWarning = rootVisualElement.Q<Label>("missing-meshes-warning");
            _animationResults = rootVisualElement.Q("animation-results");
            _results = rootVisualElement.Q("results");
        }

        private void ConfigureControls()
        {
            ConfigureVersionBadge();
            _avatarRootField.objectType = typeof(GameObject);
            _avatarRootField.allowSceneObjects = true;
            _avatarRootField.RegisterValueChangedCallback(evt =>
            {
                _avatarRoot = evt.newValue as GameObject;
                if (_report != null && _report.Root != _avatarRoot)
                {
                    _report = null;
                    InvalidateAnimationFilterIndex();
                }
                RefreshView();
            });

            _includeInactiveToggle.RegisterValueChangedCallback(evt => _includeInactive = evt.newValue);
            _wholeProjectAnimationsToggle.RegisterValueChangedCallback(evt => _wholeProjectAnimations = evt.newValue);
            _animationCategoryFilter.choices = new List<string> { "UV Controls", "All Properties", "Other Properties" };
            _animationCategoryFilter.SetValueWithoutNotify(GetAnimationFilterLabel(_animationPropertyFilter));
            _animationCategoryFilter.RegisterValueChangedCallback(evt =>
            {
                _animationPropertyFilter = ParseAnimationFilter(evt.newValue);
                RefreshAnimationFiltering();
            });
            _animationSearchField.RegisterValueChangedCallback(evt =>
            {
                _animationSearch = evt.newValue ?? string.Empty;
                RefreshAnimationFiltering();
            });
            _multipleTilesToggle.RegisterValueChangedCallback(evt =>
            {
                _onlyMultipleTiles = evt.newValue;
                RebuildResults();
            });
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RebuildResults();
            });
            _scanButton.clicked += RunScan;
            _clearButton.clicked += ClearReport;
            _cancelScanButton.clicked += () => CancelScan(true);
        }

        private void ConfigureVersionBadge()
        {
            if (_versionBadge == null)
                return;

            try
            {
                PackageManagerInfo package = PackageManagerInfo.FindForAssembly(typeof(UvTileScannerWindow).Assembly);
                if (package != null && !string.IsNullOrEmpty(package.version))
                {
                    _versionBadge.text = $"v{package.version}";
                    _versionBadge.tooltip = $"{package.displayName} package version {package.version}";
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UV Tile Scanner could not read its package version: {exception.Message}");
            }

            _versionBadge.text = "Version unavailable";
            _versionBadge.tooltip = "Unity Package Manager could not resolve this assembly's package metadata.";
        }

        private void SyncInputControls()
        {
            if (_avatarRootField == null)
                return;

            _avatarRootField.SetValueWithoutNotify(_avatarRoot);
            _includeInactiveToggle.SetValueWithoutNotify(_includeInactive);
            _wholeProjectAnimationsToggle.SetValueWithoutNotify(_wholeProjectAnimations);
            _multipleTilesToggle.SetValueWithoutNotify(_onlyMultipleTiles);
            _searchField.SetValueWithoutNotify(_search ?? string.Empty);
            _animationCategoryFilter.SetValueWithoutNotify(GetAnimationFilterLabel(_animationPropertyFilter));
            _animationSearchField.SetValueWithoutNotify(_animationSearch ?? string.Empty);
        }

        private void RefreshView()
        {
            if (_scanButton == null)
                return;

            _scanButton.SetEnabled(_avatarRoot != null);
            _scanButton.SetEnabled(_avatarRoot != null && !_isScanning);
            _clearButton.SetEnabled(_report != null && !_isScanning);
            SetDisplayed(_scanProgress, _isScanning);
            UpdateScanProgressDisplay();

            bool hasReport = _report != null && _report.Root != null;
            SetDisplayed(_emptyState, !hasReport);
            SetDisplayed(_reportContent, hasReport);
            SetDisplayed(_animationFilterToolbar, hasReport && _report.AnimationAnalysis != null);

            if (!hasReport)
            {
                Label message = _emptyState.Q<Label>("empty-state-message");
                if (_report != null && _report.Root == null)
                {
                    message.text = "The previously scanned avatar no longer exists. Choose another root and scan again.";
                    _emptyState.EnableInClassList("message-warning", true);
                }
                else
                {
                    message.text = "Choose an avatar GameObject, then scan for MeshFilter and SkinnedMeshRenderer meshes. " +
                                   "The scanner checks UV1: the second UV set, commonly called UV2.";
                    _emptyState.EnableInClassList("message-warning", false);
                }

                return;
            }

            UpdateSummary();
            RebuildResults();
        }

        private void UpdateSummary()
        {
            _summaryTitle.text = $"{_report.Meshes.Count} unique mesh(es) found";
            _summaryCounts.text =
                $"Multiple tiles: {_report.MultipleTiles}     Single tile: {_report.SingleTile}     " +
                $"No second UV set: {_report.WithoutSecondUvSet}     Issues: {_report.Issues}\n" +
                $"Material slots: {_report.MaterialSlotCount}     Unique materials: {_report.UniqueMaterialCount}     " +
                $"Unique shaders: {_report.UniqueShaderCount}";

            AvatarAnimationAnalysisReport animation = _report.AnimationAnalysis;
            if (animation != null)
            {
                _summaryCounts.text += $"\nAnimation scope: {FormatEnum(_isScanning ? _animationScope : animation.Scope)}     " +
                                       $"Clips: {animation.Clips.Count}     Related clips: {animation.RelatedClipCount}     " +
                                       $"Bindings: {animation.BindingCount}";
                EnsureAnimationFilterResult();
                if (_animationFilterResult != null)
                    _summaryCounts.text += $"\nMatching bindings: {_animationFilterResult.TotalMatchingBindingCount}     " +
                                           $"UV feature: {_animationFilterResult.UvFeatureBindingCount}     " +
                                           $"Other material: {_animationFilterResult.OtherMaterialBindingCount}     " +
                                           $"Visible: {_animationFilterResult.VisibleBindingCount}";
            }

            bool hasMissingMeshes = _report.MissingMeshReferences > 0;
            SetDisplayed(_missingMeshesWarning, hasMissingMeshes);
            if (hasMissingMeshes)
            {
                _missingMeshesWarning.text =
                    $"{_report.MissingMeshReferences} renderer/filter component(s) had no mesh assigned.";
            }
        }

        private void RebuildResults()
        {
            if (_results == null || _report == null || _report.Root == null)
                return;

            _results.Clear();
            _materialAnimationViews.Clear();
            RebuildAnimationResults();
            int visible = 0;
            foreach (MeshUvScanResult result in _report.Meshes)
            {
                if (!ShouldShow(result))
                    continue;

                visible++;
                _results.Add(CreateResultCard(result));
            }

            if (visible == 0)
            {
                var noMatches = new Label("No meshes match the current filters.");
                noMatches.AddToClassList("message-box");
                _results.Add(noMatches);
            }
        }

        private void RebuildAnimationResults()
        {
            if (_animationResults == null)
                return;
            _animationResults.Clear();
            EnsureAnimationFilterResult();
            if (_animationFilterResult == null || _report == null || _report.AnimationAnalysis == null)
                return;

            _animationFilterCounts.text =
                $"Matching: {_animationFilterResult.TotalMatchingBindingCount}  •  " +
                $"UV controls: {_animationFilterResult.UvFeatureBindingCount}  •  " +
                $"Other: {_animationFilterResult.OtherMaterialBindingCount}  •  " +
                $"Visible: {_animationFilterResult.VisibleBindingCount}";
            _animationResults.Add(CreateAvatarAnimationAnalysis(_report.AnimationAnalysis, _animationFilterResult));
        }

        private void RefreshAnimationFiltering()
        {
            if (_report == null)
                return;
            _animationFilterResult = null;
            EnsureAnimationFilterResult();
            UpdateSummary();
            RebuildAnimationResults();
            foreach (MaterialAnimationView view in _materialAnimationViews)
                RefreshMaterialAnimationView(view);
        }

        private void EnsureAnimationFilterResult()
        {
            AvatarAnimationAnalysisReport animation = _report == null ? null : _report.AnimationAnalysis;
            if (!ReferenceEquals(animation, _indexedAnimationReport))
            {
                _indexedAnimationReport = animation;
                _animationFilterIndex = animation == null ? null : new AnimationAnalysisFilterIndex(animation);
                _animationFilterResult = null;
            }
            if (_animationFilterResult == null && _animationFilterIndex != null)
                _animationFilterResult = _animationFilterIndex.Filter(_animationPropertyFilter, _animationSearch);
        }

        private void InvalidateAnimationFilterIndex()
        {
            _indexedAnimationReport = null;
            _animationFilterIndex = null;
            _animationFilterResult = null;
            _materialAnimationViews.Clear();
        }

        private bool ShouldShow(MeshUvScanResult result)
        {
            if (_onlyMultipleTiles && result.Status != UvTileScanStatus.MultipleTiles)
                return false;
            if (string.IsNullOrWhiteSpace(_search))
                return true;

            if (result.Mesh.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            foreach (string usage in result.Usages)
            {
                if (usage.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (MeshUsageInfo usage in result.UsageDetails)
            {
                foreach (MeshMaterialSlot slot in usage.MaterialSlots)
                {
                    if (slot.Material != null &&
                        slot.Material.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (slot.Shader != null &&
                        slot.Shader.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (slot.ShaderAnalysis != null)
                    {
                        foreach (ShaderAnalysisResult analysis in slot.ShaderAnalysis.Results)
                        {
                            if (ContainsSearch(analysis.TargetFamily) || ContainsSearch(analysis.DetectorName))
                                return true;
                            foreach (ShaderFeatureAnalysis feature in analysis.Features)
                            {
                                if (ContainsSearch(feature.DisplayName))
                                    return true;
                            }
                        }
                    }
                    foreach (AnimationBindingAnalysis binding in slot.AnimationBindings)
                    {
                        if (ContainsSearch(binding.Clip.name) || ContainsSearch(binding.PropertyName) ||
                            (binding.Source != null && ContainsSearch(binding.Source.Location)))
                            return true;
                    }
                }
            }

            return false;
        }

        private bool ContainsSearch(string value) => !string.IsNullOrEmpty(value) &&
            value.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

        private VisualElement CreateResultCard(MeshUvScanResult result)
        {
            var card = new VisualElement();
            card.AddToClassList("result-card");
            card.AddToClassList(GetStatusClass(result.Status));

            var foldout = new Foldout
            {
                text = $"{result.Mesh.name}  —  {GetStatusLabel(result)}"
            };
            foldout.AddToClassList("result-foldout");
            foldout.SetValueWithoutNotify(result.Status == UvTileScanStatus.MultipleTiles);
            card.Add(foldout);

            var actions = new VisualElement();
            actions.AddToClassList("result-actions");
            var meshField = new ObjectField("Mesh asset")
            {
                objectType = typeof(Mesh),
                allowSceneObjects = false
            };
            meshField.SetValueWithoutNotify(result.Mesh);
            meshField.SetEnabled(false);
            meshField.AddToClassList("mesh-field");
            actions.Add(meshField);

            var pingButton = new Button(() => EditorGUIUtility.PingObject(result.Mesh)) { text = "Ping" };
            pingButton.AddToClassList("ping-button");
            actions.Add(pingButton);
            foldout.Add(actions);

            foldout.Add(CreateKeyValueRow("Vertices", result.Mesh.vertexCount.ToString()));
            if (result.Tiles.Count > 0)
                foldout.Add(CreateKeyValueRow("Tiles", JoinTiles(result.Tiles), true));

            if (result.CrossedTilePrimitiveCount > 0)
            {
                var warning = new Label(
                    $"{result.CrossedTilePrimitiveCount} primitive(s) cross an integer tile boundary. " +
                    "Tiles are assigned using each primitive's UV centroid.");
                warning.AddToClassList("message-box");
                warning.AddToClassList("message-warning");
                foldout.Add(warning);
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                var error = new Label(result.Error);
                error.AddToClassList("message-box");
                error.AddToClassList("message-error");
                foldout.Add(error);
            }

            var usagesTitle = new Label(result.UsageDetails.Count == 1
                ? "Renderer and materials"
                : $"Renderers and materials ({result.UsageDetails.Count})");
            usagesTitle.AddToClassList("detail-label");
            usagesTitle.AddToClassList("usages-title");
            foldout.Add(usagesTitle);

            var usages = new VisualElement();
            usages.AddToClassList("usage-list");
            foreach (MeshUsageInfo usage in result.UsageDetails)
                usages.Add(CreateUsageCard(result.Mesh, usage));

            foldout.Add(usages);
            return card;
        }

        private VisualElement CreateUsageCard(Mesh mesh, MeshUsageInfo usage)
        {
            var usageCard = new VisualElement();
            usageCard.AddToClassList("usage-card");

            var path = new Label(usage.DisplayPath);
            path.AddToClassList("usage-path");
            usageCard.Add(path);

            if (usage.Renderer == null)
            {
                var warning = new Label("No MeshRenderer is attached, so this MeshFilter has no material assignments.");
                warning.AddToClassList("message-box");
                warning.AddToClassList("message-warning");
                usageCard.Add(warning);
                return usageCard;
            }

            if (usage.MaterialSlots.Count == 0)
            {
                var empty = new Label("No material slots are assigned to this renderer.");
                empty.AddToClassList("empty-materials");
                usageCard.Add(empty);
                return usageCard;
            }

            if (usage.MaterialSlots.Count != mesh.subMeshCount)
            {
                var mismatch = new Label(
                    $"This renderer has {usage.MaterialSlots.Count} material slot(s), while the mesh has " +
                    $"{mesh.subMeshCount} submesh(es).");
                mismatch.AddToClassList("message-box");
                mismatch.AddToClassList("message-warning");
                usageCard.Add(mismatch);
            }

            foreach (MeshMaterialSlot slot in usage.MaterialSlots)
                usageCard.Add(CreateMaterialSlotCard(slot));

            return usageCard;
        }

        private VisualElement CreateMaterialSlotCard(MeshMaterialSlot slot)
        {
            var card = new VisualElement();
            card.AddToClassList("material-slot-card");
            var row = new VisualElement();
            row.AddToClassList("material-slot-row");

            var slotLabel = new Label($"Slot {slot.SlotIndex}");
            slotLabel.AddToClassList("material-slot-index");
            row.Add(slotLabel);

            ObjectField materialField = CreateReadOnlyObjectField("Material", typeof(Material), slot.Material);
            materialField.AddToClassList("material-object-field");
            row.Add(materialField);

            ObjectField shaderField = CreateReadOnlyObjectField("Shader", typeof(Shader), slot.Shader);
            shaderField.AddToClassList("shader-object-field");
            row.Add(shaderField);

            var copyButton = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = BuildAnalysisText(slot);
            }) { text = "Copy" };
            copyButton.tooltip = "Copy this material's shader analysis as text.";
            copyButton.AddToClassList("copy-button");
            row.Add(copyButton);

            card.Add(row);
            card.Add(CreateShaderAnalysis(slot));
            card.Add(CreateAnimationAnalysis(slot));
            return card;
        }

        private static VisualElement CreateAvatarAnimationAnalysis(AvatarAnimationAnalysisReport report,
            AnimationFilterResult filtered)
        {
            var foldout = new Foldout
            {
                text = $"Animation analysis — {filtered.Clips.Count} visible clip(s), " +
                       $"{filtered.VisibleBindingCount} visible binding(s)",
                value = false
            };
            foldout.AddToClassList("analysis-foldout");
            foldout.Add(CreateKeyValueRow("Scope", FormatEnum(report.Scope)));
            foldout.Add(CreateKeyValueRow("Filter", GetAnimationFilterLabel(filtered.Category)));
            if (!string.IsNullOrEmpty(filtered.Search))
                foldout.Add(CreateKeyValueRow("Search", filtered.Search, true));
            foreach (AnalysisDiagnostic diagnostic in report.Diagnostics)
                foldout.Add(CreateDiagnosticLabel(diagnostic));
            if (report.Clips.Count == 0)
            {
                foldout.Add(new Label("No animation clips were found in the selected scan scope."));
                return foldout;
            }
            if (filtered.Clips.Count == 0)
            {
                foldout.Add(new Label("No matching animation bindings satisfy the current category and search filters."));
                return foldout;
            }
            foreach (AnimationClipGroup clip in filtered.Clips)
                foldout.Add(CreateClipGroup(clip));
            return foldout;
        }

        private static VisualElement CreateClipGroup(AnimationClipGroup group)
        {
            AnimationClipAnalysis clip = group.Clip;
            string clipName = clip.Clip != null ? clip.Clip.name : "Missing clip reference";
            var foldout = new Foldout
            {
                text = $"{clipName} — {FormatEnum(clip.Behaviour)}, {group.VisibleBindingCount} binding(s)",
                value = false
            };
            foldout.tooltip = string.IsNullOrEmpty(clip.AssetPath) ? clipName : clip.AssetPath;
            foldout.AddToClassList("family-foldout");
            bool built = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue || built)
                    return;
                built = true;
                PopulateClipGroup(foldout, group);
            });
            return foldout;
        }

        private static void PopulateClipGroup(Foldout foldout, AnimationClipGroup group)
        {
            AnimationClipAnalysis clip = group.Clip;
            foldout.Add(CreateObjectAndPingRow("Clip", clip.Clip));
            if (!string.IsNullOrEmpty(clip.AssetPath))
                foldout.Add(CreateKeyValueRow("Asset path", clip.AssetPath, true));
            foreach (AnimationClipSource source in clip.Sources)
            {
                if (source.Controller != null)
                    foldout.Add(CreateObjectAndPingRow("Source", source.Controller));
                foldout.Add(CreateKeyValueRow("Location", source.Location, true));
            }
            foreach (AnimationTargetGroup target in group.Targets)
                foldout.Add(CreateTargetGroup(target));
        }

        private static VisualElement CreateTargetGroup(AnimationTargetGroup target)
        {
            var foldout = new Foldout { text = GetTargetLabel(target), value = false };
            foldout.AddToClassList("animation-target-group");
            bool built = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue || built)
                    return;
                built = true;
                if (target.Renderer != null)
                    foldout.Add(CreateObjectAndPingRow("Renderer", target.Renderer));
                else
                    foldout.Add(CreateKeyValueRow("Renderer", "Not resolved", true));
                if (target.Material != null)
                    foldout.Add(CreateObjectAndPingRow("Material", target.Material));
                else
                    foldout.Add(CreateKeyValueRow("Material", "Not resolved", true));
                foldout.Add(CreateKeyValueRow("Material slot", target.MaterialSlotIndex.HasValue
                    ? target.MaterialSlotIndex.Value.ToString() : "Not resolved"));
                foreach (AnimationFilteredBinding binding in target.Bindings)
                    foldout.Add(CreateBindingSummary(binding));
            });
            return foldout;
        }

        private VisualElement CreateAnimationAnalysis(MeshMaterialSlot slot)
        {
            var view = new MaterialAnimationView
            {
                Slot = slot,
                Foldout = new Foldout { value = false },
                Body = new VisualElement()
            };
            view.Foldout.AddToClassList("analysis-foldout");
            view.Foldout.Add(view.Body);
            _materialAnimationViews.Add(view);
            RefreshMaterialAnimationView(view);
            return view.Foldout;
        }

        private void RefreshMaterialAnimationView(MaterialAnimationView view)
        {
            EnsureAnimationFilterResult();
            IReadOnlyList<AnimationFilteredBinding> bindings = _animationFilterResult == null
                ? Array.Empty<AnimationFilteredBinding>()
                : _animationFilterResult.GetBindingsForSlot(view.Slot);
            view.Foldout.text = $"Animation analysis ({bindings.Count} visible binding target(s))";
            view.Body.Clear();
            // Build only the lightweight clip headers here. Binding details remain lazy on each clip foldout.
            // This deliberately avoids a parent foldout callback: child Foldout change events bubble and used
            // to rebuild this body, detaching the child that the user had just expanded.
            PopulateMaterialAnimationView(view);
        }

        private void PopulateMaterialAnimationView(MaterialAnimationView view)
        {
            view.Body.Clear();
            IReadOnlyList<AnimationFilteredBinding> bindings = _animationFilterResult == null
                ? Array.Empty<AnimationFilteredBinding>()
                : _animationFilterResult.GetBindingsForSlot(view.Slot);
            if (bindings.Count == 0)
            {
                view.Body.Add(new Label("No animation binding matches this material slot and the current filters."));
                return;
            }

            var clips = new List<AnimationClipAnalysis>();
            var byClip = new Dictionary<AnimationClipAnalysis, List<AnimationFilteredBinding>>();
            foreach (AnimationFilteredBinding binding in bindings)
            {
                if (!byClip.TryGetValue(binding.Clip, out List<AnimationFilteredBinding> clipBindings))
                {
                    clipBindings = new List<AnimationFilteredBinding>();
                    byClip.Add(binding.Clip, clipBindings);
                    clips.Add(binding.Clip);
                }
                clipBindings.Add(binding);
            }
            foreach (AnimationClipAnalysis clip in clips)
            {
                string clipName = clip.Clip != null ? clip.Clip.name : "Missing clip reference";
                List<AnimationFilteredBinding> clipBindings = byClip[clip];
                var clipFoldout = new Foldout
                {
                    text = $"{clipName} — {clipBindings.Count} binding target(s)",
                    value = false,
                    tooltip = clip.AssetPath
                };
                clipFoldout.AddToClassList("feature-foldout");
                bool built = false;
                clipFoldout.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue || built)
                        return;
                    built = true;
                    clipFoldout.Add(CreateObjectAndPingRow("Clip", clip.Clip));
                    foreach (AnimationFilteredBinding binding in clipBindings)
                        clipFoldout.Add(CreateBindingSummary(binding));
                });
                view.Body.Add(clipFoldout);
            }
        }

        private static VisualElement CreateBindingSummary(AnimationFilteredBinding filtered)
        {
            AnimationBindingAnalysis binding = filtered.Binding;
            string featureLabel = GetBindingFeatureLabel(filtered);
            var foldout = new Foldout
            {
                text = $"{featureLabel}: {FormatAnimatedValues(binding.Keyframes)} — " +
                       $"{FormatEnum(binding.BestConfidence)}",
                value = false
            };
            foldout.AddToClassList("feature-foldout");
            if (binding.Source != null)
            {
                if (binding.Source.Controller != null)
                    foldout.Add(CreateObjectAndPingRow("Controller", binding.Source.Controller));
                foldout.Add(CreateKeyValueRow("Source", binding.Source.Location, true));
            }
            foldout.Add(CreateKeyValueRow("Binding path", string.IsNullOrEmpty(binding.BindingPath) ? "Animator root" : binding.BindingPath, true));
            foldout.Add(CreateKeyValueRow("Component", binding.ComponentTypeName));
            foldout.Add(CreateKeyValueRow("Curve", FormatEnum(binding.Kind)));
            foldout.Add(CreateKeyValueRow("Property", binding.PropertyName, true));
            if (filtered.TargetRenderer != null)
                foldout.Add(CreateObjectAndPingRow("Renderer", filtered.TargetRenderer));
            else if (binding.TargetGameObject != null)
                foldout.Add(CreateObjectAndPingRow("GameObject", binding.TargetGameObject));
            else
                foldout.Add(CreateKeyValueRow("Renderer", "Not resolved", true));
            if (filtered.TargetMaterial != null)
                foldout.Add(CreateObjectAndPingRow("Material", filtered.TargetMaterial));
            foldout.Add(CreateKeyValueRow("Material slot", filtered.Slot != null
                ? filtered.Slot.SlotIndex.ToString()
                : binding.MaterialSlotIndex.HasValue ? binding.MaterialSlotIndex.Value.ToString() : "Not resolved"));
            if (binding.Kind == AnimationBindingKind.ObjectReferenceCurve && binding.TargetMaterial != null)
                foldout.Add(CreateObjectAndPingRow("Replacement", binding.TargetMaterial));
            foreach (AnimationMaterialMatch match in filtered.Matches)
            {
                string feature = string.IsNullOrEmpty(match.FeatureLabel) ? "Other material property" : match.FeatureLabel;
                foldout.Add(CreateKeyValueRow("Feature", feature, true));
                if (match.Feature != null)
                    foldout.Add(CreateKeyValueRow("UV channel", match.Feature.UvChannel.HasValue
                        ? "UV" + match.Feature.UvChannel.Value : "Unknown"));
                if (match.TileControl != null)
                {
                    foldout.Add(CreateKeyValueRow("Tile",
                        $"Row {match.TileControl.Row}, column {match.TileControl.Column}, " +
                        $"UDIM {match.TileControl.UdimNumber}", true));
                }
                foldout.Add(CreateKeyValueRow("Match confidence", FormatEnum(match.Confidence)));
                foldout.Add(CreateKeyValueRow("Match evidence", match.Evidence, true));
            }
            foldout.Add(CreateKeyValueRow("Animated values", FormatAnimatedValues(binding.Keyframes), true));
            foldout.Add(CreateKeyValueRow("Keys", FormatKeyframes(binding.Keyframes), true));
            foreach (string evidence in filtered.ComplementaryEvidence)
            {
                Label label = new Label("Complementary value evidence: " + evidence);
                label.AddToClassList("complementary-evidence");
                label.AddToClassList("wrap-text");
                foldout.Add(label);
            }
            if (!string.IsNullOrEmpty(binding.Diagnostic))
                foldout.Add(CreateDiagnosticLabel(new AnalysisDiagnostic(AnalysisDiagnosticSeverity.Warning, binding.Diagnostic)));
            return foldout;
        }

        private static VisualElement CreateShaderAnalysis(MeshMaterialSlot slot)
        {
            var foldout = new Foldout { text = "Shader analysis", value = false };
            foldout.AddToClassList("analysis-foldout");
            MaterialShaderAnalysisReport report = slot.ShaderAnalysis;
            if (slot.Material == null)
            {
                foldout.Add(new Label("No material is assigned."));
                return foldout;
            }
            if (report == null || (report.Results.Count == 0 && report.Diagnostics.Count == 0))
            {
                foldout.Add(new Label("Unsupported shader family: no installed detector accepted this material."));
                return foldout;
            }

            foreach (AnalysisDiagnostic diagnostic in report.Diagnostics)
                foldout.Add(CreateDiagnosticLabel(diagnostic));

            foreach (ShaderAnalysisResult analysis in report.Results)
            {
                var family = new Foldout
                {
                    text = $"{analysis.TargetFamily}: {FormatEnum(analysis.Classification)}",
                    value = analysis.Classification == ShaderFamilyClassification.Confirmed ||
                            analysis.Classification == ShaderFamilyClassification.Probable
                };
                family.AddToClassList("family-foldout");
                family.Add(CreateKeyValueRow("Detector", analysis.DetectorName));
                family.Add(CreateKeyValueRow("Locked", FormatEnum(analysis.Locked)));
                family.Add(CreateKeyValueRow("Version", $"{analysis.Version.DisplayValue} " +
                    $"({FormatEnum(analysis.Version.Kind)}, {FormatEnum(analysis.Version.Confidence)})", true));
                family.Add(CreateKeyValueRow("Version source", analysis.Version.Method, true));
                if (analysis.Version.Evidence.Count > 0)
                    family.Add(CreateKeyValueRow("Version evidence", JoinStrings(analysis.Version.Evidence), true));
                if (analysis.Version.CompatibilityFamily != null)
                {
                    family.Add(CreateKeyValueRow("Compatibility family",
                        $"{analysis.Version.CompatibilityFamily.DisplayValue} " +
                        $"({FormatEnum(analysis.Version.CompatibilityFamily.Confidence)})", true));
                    family.Add(CreateKeyValueRow("Family source",
                        analysis.Version.CompatibilityFamily.Method, true));
                    if (analysis.Version.CompatibilityFamily.Evidence.Count > 0)
                        family.Add(CreateKeyValueRow("Family evidence",
                            JoinStrings(analysis.Version.CompatibilityFamily.Evidence), true));
                }
                if (analysis.Classification == ShaderFamilyClassification.ConfirmedNonTarget ||
                    analysis.Classification == ShaderFamilyClassification.Unknown)
                {
                    family.Add(new Label("This detector cannot provide supported tile-feature analysis for this shader family."));
                }
                if (analysis.IdentificationEvidence.Count > 0)
                    family.Add(CreateKeyValueRow("Evidence", JoinStrings(analysis.IdentificationEvidence), true));
                foreach (AnalysisDiagnostic diagnostic in analysis.Diagnostics)
                    family.Add(CreateDiagnosticLabel(diagnostic));
                foreach (ShaderFeatureAnalysis feature in analysis.Features)
                    family.Add(CreateFeatureAnalysis(slot, analysis, feature));
                if (analysis.Features.Count == 0 &&
                    (analysis.Classification == ShaderFamilyClassification.Confirmed ||
                     analysis.Classification == ShaderFamilyClassification.Probable))
                    family.Add(new Label("No supported tile feature properties were found."));
                foldout.Add(family);
            }

            return foldout;
        }

        private static VisualElement CreateFeatureAnalysis(MeshMaterialSlot slot,
            ShaderAnalysisResult analysis, ShaderFeatureAnalysis feature)
        {
            var foldout = new Foldout
            {
                text = $"{feature.DisplayName} — {FormatEnum(feature.Enabled)}",
                value = feature.Enabled == AnalysisTriState.Yes
            };
            foldout.AddToClassList("feature-foldout");
            foldout.Add(CreateKeyValueRow("UV channel",
                feature.UvChannel.HasValue ? $"UV{feature.UvChannel.Value}" : "Unknown"));
            foldout.Add(CreateKeyValueRow("Readability", FormatEnum(feature.Readability)));
            if (feature.Evidence.Count > 0)
                foldout.Add(CreateKeyValueRow("Evidence", JoinStrings(feature.Evidence), true));

            ShaderFeatureMeshCorrelation correlation = FindCorrelation(slot, analysis.DetectorId, feature.Type);
            if (correlation != null)
            {
                foldout.Add(CreateKeyValueRow("Submesh", string.Join(", ", correlation.SubMeshIndices)));
                foldout.Add(CreateKeyValueRow("Mesh tiles", correlation.OccupiedTiles.Count == 0
                    ? "None" : JoinTiles(correlation.OccupiedTiles), true));
                foldout.Add(CreateKeyValueRow("Configured tiles", correlation.ConfiguredTiles.Count == 0
                    ? "None" : JoinTiles(correlation.ConfiguredTiles), true));
                if (correlation.ConfiguredButUnoccupied.Count > 0)
                    foldout.Add(CreateKeyValueRow("Not on mesh", JoinTiles(correlation.ConfiguredButUnoccupied), true));
                if (correlation.OccupiedButUnconfigured.Count > 0)
                    foldout.Add(CreateKeyValueRow("No tile value", JoinTiles(correlation.OccupiedButUnconfigured), true));
                if (!string.IsNullOrEmpty(correlation.Diagnostic))
                    foldout.Add(CreateDiagnosticLabel(new AnalysisDiagnostic(
                        AnalysisDiagnosticSeverity.Warning, correlation.Diagnostic)));
            }

            foreach (AnalysisDiagnostic diagnostic in feature.Diagnostics)
                foldout.Add(CreateDiagnosticLabel(diagnostic));
            if (feature.Controls.Count > 0)
            {
                var controls = new Foldout { text = $"Tile controls ({feature.Controls.Count})", value = false };
                controls.AddToClassList("tile-controls-foldout");
                foreach (ShaderTileControl control in feature.Controls)
                {
                    string value = control.Value.HasValue ? control.Value.Value.ToString("0.###") : "Unknown";
                    string animated = control.MarkedAnimated.HasValue
                        ? (control.MarkedAnimated.Value ? ", animated" : ", not marked animated")
                        : string.Empty;
                    controls.Add(CreateKeyValueRow($"{control.Column}, {control.Row}",
                        $"UDIM {control.UdimNumber}: {control.PropertyName} ({control.PropertyType}) = {value} — " +
                        $"{FormatEnum(control.State)}, {FormatEnum(control.Readability)}, " +
                        $"{FormatEnum(control.Confidence)} confidence{animated}", true));
                }
                foldout.Add(controls);
            }
            return foldout;
        }

        private static ShaderFeatureMeshCorrelation FindCorrelation(MeshMaterialSlot slot,
            string detectorId, ShaderFeatureType type)
        {
            foreach (ShaderFeatureMeshCorrelation correlation in slot.FeatureCorrelations)
            {
                if (correlation.DetectorId == detectorId && correlation.FeatureType == type)
                    return correlation;
            }
            return null;
        }

        private static Label CreateDiagnosticLabel(AnalysisDiagnostic diagnostic)
        {
            var label = new Label($"{diagnostic.Severity}: {diagnostic.Message}");
            label.AddToClassList("message-box");
            label.AddToClassList(diagnostic.Severity == AnalysisDiagnosticSeverity.Error
                ? "message-error" : "message-warning");
            return label;
        }

        private static string BuildAnalysisText(MeshMaterialSlot slot)
        {
            var lines = new List<string>
            {
                $"Material slot: {slot.SlotIndex}",
                $"Material: {(slot.Material != null ? slot.Material.name : "None")}",
                $"Shader: {(slot.Shader != null ? slot.Shader.name : "None")}" 
            };
            if (slot.ShaderAnalysis != null)
            {
                foreach (ShaderAnalysisResult analysis in slot.ShaderAnalysis.Results)
                {
                    lines.Add($"{analysis.TargetFamily}: {analysis.Classification}; locked: {analysis.Locked}; " +
                              $"version: {analysis.Version.DisplayValue} ({analysis.Version.Method})");
                    if (analysis.Version.CompatibilityFamily != null)
                        lines.Add($"  Compatibility family: {analysis.Version.CompatibilityFamily.DisplayValue} " +
                                  $"({analysis.Version.CompatibilityFamily.Method})");
                    foreach (ShaderFeatureAnalysis feature in analysis.Features)
                    {
                        lines.Add($"  {feature.DisplayName}: {feature.Enabled}; " +
                                  $"UV: {(feature.UvChannel.HasValue ? "UV" + feature.UvChannel.Value : "Unknown")}");
                        ShaderFeatureMeshCorrelation correlation = FindCorrelation(slot, analysis.DetectorId, feature.Type);
                        if (correlation != null)
                            lines.Add($"    Mesh tiles: {JoinTiles(correlation.OccupiedTiles)}; configured: {JoinTiles(correlation.ConfiguredTiles)}");
                        foreach (ShaderTileControl control in feature.Controls)
                            lines.Add($"    ({control.Column}, {control.Row}) {control.PropertyName} = " +
                                      $"{(control.Value.HasValue ? control.Value.Value.ToString("0.###") : "Unknown")} " +
                                      $"[{control.PropertyType}; {control.State}; {control.Readability}; {control.Confidence}]");
                    }
                }
                foreach (AnalysisDiagnostic diagnostic in slot.ShaderAnalysis.Diagnostics)
                    lines.Add($"{diagnostic.Severity}: {diagnostic.Message}");
            }
            foreach (AnimationBindingAnalysis binding in slot.AnimationBindings)
            {
                lines.Add($"Animation: {binding.Clip.name}; {binding.PropertyName}; {binding.Kind}; " +
                          $"path: {(string.IsNullOrEmpty(binding.BindingPath) ? "Animator root" : binding.BindingPath)}; " +
                          $"slot: {(binding.MaterialSlotIndex.HasValue ? binding.MaterialSlotIndex.Value.ToString() : "Not resolved")}; " +
                          $"keys: {FormatKeyframes(binding.Keyframes)}");
                foreach (AnimationMaterialMatch match in binding.Matches)
                    if (match.Slot == slot)
                        lines.Add($"  {match.Confidence}: {match.FeatureLabel ?? "Raw binding"}; {match.Evidence}");
            }
            return string.Join("\n", lines);
        }

        private static VisualElement CreateObjectAndPingRow(string label, UnityEngine.Object value)
        {
            var row = new VisualElement();
            row.AddToClassList("detail-row");
            var objectField = new ObjectField(label)
            {
                objectType = value == null ? typeof(UnityEngine.Object) : value.GetType(),
                allowSceneObjects = true
            };
            objectField.SetValueWithoutNotify(value);
            objectField.style.flexGrow = 1;
            row.Add(objectField);
            var ping = new Button(() => EditorGUIUtility.PingObject(value)) { text = "Ping" };
            ping.SetEnabled(value != null);
            ping.AddToClassList("ping-button");
            row.Add(ping);
            return row;
        }

        private static string FormatKeyframes(IReadOnlyList<AnimationKeyframeValue> values)
        {
            if (values == null || values.Count == 0) return "No keys";
            var labels = new List<string>();
            int shown = Math.Min(values.Count, 6);
            for (int index = 0; index < shown; index++)
                labels.Add($"{values[index].Time:0.###}s = {values[index].DisplayValue}");
            if (values.Count > shown) labels.Add($"+{values.Count - shown} more");
            return string.Join(", ", labels.ToArray());
        }

        private static string FormatAnimatedValues(IReadOnlyList<AnimationKeyframeValue> values)
        {
            if (values == null || values.Count == 0)
                return "No extracted values";
            bool hasFloat = false;
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            var distinct = new List<string>();
            foreach (AnimationKeyframeValue value in values)
            {
                if (value.FloatValue.HasValue)
                {
                    hasFloat = true;
                    minimum = Math.Min(minimum, value.FloatValue.Value);
                    maximum = Math.Max(maximum, value.FloatValue.Value);
                }
                string display = value.DisplayValue;
                if (!distinct.Contains(display) && distinct.Count < 6)
                    distinct.Add(display);
            }
            string valuesText = string.Join(", ", distinct.ToArray());
            if (!hasFloat || Mathf.Approximately(minimum, maximum))
                return distinct.Count == 1 ? "Value " + valuesText : "Values " + valuesText;
            return $"Values {valuesText} (range {minimum:0.###}–{maximum:0.###})";
        }

        private static string GetBindingFeatureLabel(AnimationFilteredBinding binding)
        {
            var labels = new List<string>();
            foreach (AnimationMaterialMatch match in binding.Matches)
            {
                string label = match.FeatureLabel;
                if (string.IsNullOrEmpty(label))
                    continue;
                if (match.UdimNumber.HasValue)
                    label += " / UDIM " + match.UdimNumber.Value;
                if (!labels.Contains(label))
                    labels.Add(label);
            }
            return labels.Count > 0 ? string.Join(", ", labels.ToArray()) : binding.Binding.PropertyName;
        }

        private static string GetTargetLabel(AnimationTargetGroup target)
        {
            string path = string.IsNullOrEmpty(target.BindingPath)
                ? target.Renderer != null ? target.Renderer.name : "Unresolved renderer"
                : target.BindingPath;
            string material = target.Material != null ? target.Material.name : "Unresolved material";
            string slot = target.MaterialSlotIndex.HasValue
                ? "Slot " + target.MaterialSlotIndex.Value : "Slot unresolved";
            return $"{path} / {material} ({slot}) — {target.Bindings.Count} property binding(s)";
        }

        private static string GetAnimationFilterLabel(AnimationPropertyFilter filter)
        {
            switch (filter)
            {
                case AnimationPropertyFilter.AllProperties: return "All Properties";
                case AnimationPropertyFilter.OtherProperties: return "Other Properties";
                default: return "UV Controls";
            }
        }

        private static AnimationPropertyFilter ParseAnimationFilter(string label)
        {
            if (string.Equals(label, "All Properties", StringComparison.Ordinal))
                return AnimationPropertyFilter.AllProperties;
            if (string.Equals(label, "Other Properties", StringComparison.Ordinal))
                return AnimationPropertyFilter.OtherProperties;
            return AnimationPropertyFilter.UvControls;
        }

        private static string FormatEnum(object value)
        {
            if (value == null) return "Unknown";
            string text = value.ToString();
            var words = new List<char>(text.Length + 4);
            for (int index = 0; index < text.Length; index++)
            {
                if (index > 0 && char.IsUpper(text[index]) && !char.IsUpper(text[index - 1]))
                    words.Add(' ');
                words.Add(text[index]);
            }
            return new string(words.ToArray());
        }

        private static string JoinStrings(IReadOnlyList<string> values) => string.Join("  ", values);

        private static ObjectField CreateReadOnlyObjectField(
            string label,
            Type objectType,
            UnityEngine.Object value)
        {
            var field = new ObjectField(label)
            {
                objectType = objectType,
                allowSceneObjects = false
            };
            field.SetValueWithoutNotify(value);
            field.SetEnabled(false);
            return field;
        }

        private static VisualElement CreateKeyValueRow(string key, string value, bool wrap = false)
        {
            var row = new VisualElement();
            row.AddToClassList("detail-row");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("detail-label");
            row.Add(keyLabel);

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("detail-value");
            if (wrap)
                valueLabel.AddToClassList("wrap-text");
            row.Add(valueLabel);
            return row;
        }

        private static string GetStatusClass(UvTileScanStatus status)
        {
            switch (status)
            {
                case UvTileScanStatus.MultipleTiles: return "status-multiple";
                case UvTileScanStatus.SingleTile: return "status-single";
                case UvTileScanStatus.NoSecondUvSet: return "status-missing";
                default: return "status-error";
            }
        }

        private static string GetStatusLabel(MeshUvScanResult result)
        {
            switch (result.Status)
            {
                case UvTileScanStatus.NoSecondUvSet: return "No second UV set";
                case UvTileScanStatus.EmptySecondUvSet: return "Empty second UV set";
                case UvTileScanStatus.SingleTile: return "1 tile";
                case UvTileScanStatus.MultipleTiles: return $"{result.Tiles.Count} tiles";
                default: return "Could not scan";
            }
        }

        private static string JoinTiles(IReadOnlyList<UvTile> tiles)
        {
            var labels = new string[tiles.Count];
            for (int index = 0; index < tiles.Count; index++)
                labels[index] = tiles[index].ToString();
            return string.Join(",  ", labels);
        }

        private void RunScan()
        {
            if (_avatarRoot == null || _isScanning)
                return;

            try
            {
                _animationScope = _wholeProjectAnimations
                    ? AnimationScanScope.WholeProject : AnimationScanScope.AvatarLinked;
                _report = null;
                InvalidateAnimationFilterIndex();
                _isScanning = true;
                _scanProgressValue = 0f;
                _scanProgressText = "Preparing mesh and material scan";
                _meshScan = AvatarUvScanner.ScanMeshAndShaderIncremental(_avatarRoot, _includeInactive,
                    ShaderDetectorRegistry.Discover(),
                    (value, message) =>
                    {
                        _scanProgressValue = value * 0.5f;
                        _scanProgressText = message;
                    },
                    report => _report = report);
                EditorApplication.update += AdvanceScan;
                RefreshView();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Scan failed. See the Console."));
            }
        }

        private void ClearReport()
        {
            CancelScan(false);
            _report = null;
            InvalidateAnimationFilterIndex();
            RefreshView();
        }

        private void StartAnimationScan()
        {
            _scanProgressValue = 0.5f;
            _scanProgressText = "Preparing animation analysis";
            AvatarUvScanReport report = _report;
            _animationScan = AvatarAnimationAnalyzer.AnalyzeIncremental(report, _animationScope,
                (value, message) =>
                {
                    _scanProgressValue = 0.5f + (value * 0.5f);
                    _scanProgressText = message;
                },
                result =>
                {
                    if (_report == report)
                        _report.SetAnimationAnalysis(result);
                });
        }

        private void AdvanceScan()
        {
            try
            {
                if (_meshScan != null)
                {
                    if (_meshScan.MoveNext())
                    {
                        UpdateScanProgressDisplay();
                        return;
                    }

                    _meshScan = null;
                    if (_report == null)
                        throw new InvalidOperationException("The mesh and material scan completed without a report.");
                    RefreshView();
                    StartAnimationScan();
                }

                if (_animationScan != null && _animationScan.MoveNext())
                {
                    UpdateScanProgressDisplay();
                    return;
                }
                CompleteScan();
            }
            catch (Exception exception)
            {
                CancelScan(false);
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Scan failed. See the Console."));
                RefreshView();
            }
        }

        private void CompleteScan()
        {
            EditorApplication.update -= AdvanceScan;
            _meshScan = null;
            _animationScan = null;
            _isScanning = false;
            _scanProgressValue = 1f;
            _scanProgressText = "Scan complete";
            RefreshView();
        }

        private void CancelScan(bool notify)
        {
            if (!_isScanning && _meshScan == null && _animationScan == null)
                return;
            EditorApplication.update -= AdvanceScan;
            _meshScan = null;
            _animationScan = null;
            _isScanning = false;
            _scanProgressValue = 0f;
            _scanProgressText = string.Empty;
            if (notify)
                ShowNotification(new GUIContent("Scan cancelled."));
            RefreshView();
        }

        private void UpdateScanProgressDisplay()
        {
            if (_scanProgressLabel == null || _scanProgressBar == null)
                return;
            _scanProgressLabel.text = string.IsNullOrEmpty(_scanProgressText)
                ? "Scanning avatar" : _scanProgressText;
            _scanProgressBar.value = Mathf.Round(_scanProgressValue * 100f);
            _scanProgressBar.title = $"{_scanProgressBar.value:0}%";
        }

        private void OnDisable()
        {
            EditorApplication.update -= AdvanceScan;
            _meshScan = null;
            _animationScan = null;
            _isScanning = false;
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
