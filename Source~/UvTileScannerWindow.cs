using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Nvh.UvTileScanner.Conversion;
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
        private const string NoRenderMaterialGuid = "a8e39e8773144624967f9cd02fc4ecaf";

        [SerializeField] private GameObject _avatarRoot;
        [SerializeField] private bool _includeInactive = true;
        [SerializeField] private bool _wholeProjectAnimations;
        [SerializeField] private bool _onlyMultipleTiles;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private AnimationPropertyFilter _animationPropertyFilter = AnimationPropertyFilter.UvControls;
        [SerializeField] private string _animationSearch = string.Empty;
        [SerializeField] private TextAsset _conversionConfigurationAsset;
        [SerializeField] private bool _createFallbackVariant;
        [SerializeField] private GameObject _previewAvatarSource;
        [SerializeField] private TextAsset _previewManifestAsset;

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
        private ObjectField _conversionConfigField;
        private Toggle _createFallbackVariantToggle;
        private Button _exportYamlButton;
        private Button _validateConfigButton;
        private Button _dryRunButton;
        private Button _runConversionButton;
        private VisualElement _conversionStatus;
        private GeometryConversionReport _conversionReport;
        private readonly List<ConversionDiagnostic> _conversionDiagnostics = new List<ConversionDiagnostic>();
        private ObjectField _previewAvatarField;
        private ObjectField _previewConfigField;
        private ObjectField _previewManifestField;
        private Button _startPreviewButton;
        private Button _resetPreviewButton;
        private Button _restorePreviewButton;
        private Button _endPreviewButton;
        private VisualElement _previewStatus;
        private VisualElement _previewControls;
        private VisualElement _previewActionRow;
        private FeatureVisibilityPreviewSession _previewSession;
        private readonly List<ConversionDiagnostic> _previewDiagnostics = new List<ConversionDiagnostic>();
        private string _previewStatusMessage;
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

        [MenuItem("NVH/UV Tile Scanner/Scanner")]
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
            _conversionConfigField = rootVisualElement.Q<ObjectField>("conversion-config-field");
            _createFallbackVariantToggle = rootVisualElement.Q<Toggle>("create-fallback-variant-toggle");
            _exportYamlButton = rootVisualElement.Q<Button>("export-yaml-button");
            _validateConfigButton = rootVisualElement.Q<Button>("validate-config-button");
            _dryRunButton = rootVisualElement.Q<Button>("dry-run-button");
            _runConversionButton = rootVisualElement.Q<Button>("run-conversion-button");
            _conversionStatus = rootVisualElement.Q("conversion-status");
            _previewAvatarField = rootVisualElement.Q<ObjectField>("preview-avatar-field");
            _previewConfigField = rootVisualElement.Q<ObjectField>("preview-config-field");
            _previewManifestField = rootVisualElement.Q<ObjectField>("preview-manifest-field");
            _startPreviewButton = rootVisualElement.Q<Button>("start-preview-button");
            _resetPreviewButton = rootVisualElement.Q<Button>("reset-preview-button");
            _restorePreviewButton = rootVisualElement.Q<Button>("restore-preview-button");
            _endPreviewButton = rootVisualElement.Q<Button>("end-preview-button");
            _previewStatus = rootVisualElement.Q("preview-status");
            _previewControls = rootVisualElement.Q("preview-controls");
            _previewActionRow = rootVisualElement.Q("preview-action-row");
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
                    InvalidateConversionReport();
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

            _conversionConfigField.objectType = typeof(TextAsset);
            _conversionConfigField.allowSceneObjects = false;
            _conversionConfigField.RegisterValueChangedCallback(evt =>
            {
                EndFeaturePreview(false);
                _conversionConfigurationAsset = evt.newValue as TextAsset;
                _previewConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
                InvalidateConversionReport();
                RefreshConversionControls();
                RefreshPreviewControls();
            });
            _createFallbackVariantToggle.RegisterValueChangedCallback(evt =>
            {
                _createFallbackVariant = evt.newValue;
                RefreshConversionControls();
            });
            _exportYamlButton.clicked += ExportConfigurationTemplate;
            _validateConfigButton.clicked += () => ValidateConfiguration(false);
            _dryRunButton.clicked += () => ValidateConfiguration(true);
            _runConversionButton.clicked += RunConversion;

            _previewAvatarField.objectType = typeof(GameObject);
            _previewAvatarField.allowSceneObjects = true;
            _previewAvatarField.RegisterValueChangedCallback(evt =>
            {
                EndFeaturePreview(false);
                _previewAvatarSource = evt.newValue as GameObject;
                RefreshPreviewControls();
            });
            _previewConfigField.objectType = typeof(TextAsset);
            _previewConfigField.allowSceneObjects = false;
            _previewConfigField.RegisterValueChangedCallback(evt =>
            {
                EndFeaturePreview(false);
                _conversionConfigurationAsset = evt.newValue as TextAsset;
                _conversionConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
                InvalidateConversionReport();
                RefreshConversionControls();
                RefreshPreviewControls();
            });
            _previewManifestField.objectType = typeof(TextAsset);
            _previewManifestField.allowSceneObjects = false;
            _previewManifestField.RegisterValueChangedCallback(evt =>
            {
                EndFeaturePreview(false);
                _previewManifestAsset = evt.newValue as TextAsset;
                RefreshPreviewControls();
            });
            _startPreviewButton.clicked += StartFeaturePreview;
            _resetPreviewButton.clicked += ResetFeaturePreview;
            _restorePreviewButton.clicked += RestorePreviewMaterials;
            _endPreviewButton.clicked += () => EndFeaturePreview(true);
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
            _conversionConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
            _createFallbackVariantToggle.SetValueWithoutNotify(_createFallbackVariant);
            _previewAvatarField.SetValueWithoutNotify(_previewAvatarSource);
            _previewConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
            _previewManifestField.SetValueWithoutNotify(_previewManifestAsset);
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
            RefreshConversionControls();
            RefreshPreviewControls();

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
                InvalidateConversionReport();
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
            InvalidateConversionReport();
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
            EndFeaturePreview(false);
            EditorApplication.update -= AdvanceScan;
            _meshScan = null;
            _animationScan = null;
            _isScanning = false;
        }

        private void ExportConfigurationTemplate()
        {
            if (_report == null || _report.Root == null)
                return;
            string defaultName = string.IsNullOrWhiteSpace(_report.Root.name)
                ? "AvatarGeometry" : _report.Root.name + "Geometry";
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Export geometry conversion YAML", defaultName, "yaml",
                "Choose where to save the editable conversion configuration template.");
            if (string.IsNullOrEmpty(assetPath))
                return;
            if (File.Exists(ToAbsoluteProjectPath(assetPath)) &&
                !EditorUtility.DisplayDialog("Replace YAML configuration?",
                    "The selected file already exists. Replace it with a newly generated template?",
                    "Replace", "Cancel"))
                return;
            try
            {
                File.WriteAllText(ToAbsoluteProjectPath(assetPath), ConfigurationTemplateExporter.Export(_report));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                _conversionConfigurationAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                _conversionConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
                _previewConfigField.SetValueWithoutNotify(_conversionConfigurationAsset);
                InvalidateConversionReport();
                RefreshConversionControls();
                EditorGUIUtility.PingObject(_conversionConfigurationAsset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("YAML export failed. See the Console."));
            }
        }

        private void ValidateConfiguration(bool dryRun)
        {
            _conversionDiagnostics.Clear();
            _conversionReport = null;
            if (_conversionConfigurationAsset == null || _avatarRoot == null)
            {
                RefreshConversionControls();
                return;
            }
            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(_conversionConfigurationAsset.text);
            _conversionDiagnostics.AddRange(parsed.Diagnostics);
            if (parsed.Success)
            {
                _conversionReport = GeometryConversionPlanner.ValidateAndPlan(_avatarRoot, parsed.Configuration, _report);
                _conversionDiagnostics.AddRange(_conversionReport.Diagnostics);
            }
            RefreshConversionControls();
            if (_conversionReport != null && !_conversionReport.HasErrors)
                ShowNotification(new GUIContent(dryRun ? "Dry run passed." : "Configuration is valid."));
        }

        private void RunConversion()
        {
            if (_conversionConfigurationAsset == null || _avatarRoot == null)
                return;
            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(_conversionConfigurationAsset.text);
            _conversionDiagnostics.Clear();
            _conversionDiagnostics.AddRange(parsed.Diagnostics);
            if (!parsed.Success)
            {
                _conversionReport = null;
                RefreshConversionControls();
                return;
            }
            GeometryConversionReport validation = GeometryConversionPlanner.ValidateAndPlan(
                _avatarRoot, parsed.Configuration, _report);
            if (validation.HasErrors)
            {
                _conversionReport = validation;
                _conversionDiagnostics.AddRange(validation.Diagnostics);
                RefreshConversionControls();
                return;
            }
            _conversionReport = GeometryConversionOutput.Convert(_avatarRoot, parsed.Configuration, _report,
                _createFallbackVariant);
            _conversionDiagnostics.AddRange(_conversionReport.Diagnostics);
            RefreshConversionControls();
            if (_conversionReport.HasErrors)
                return;
            string preferredPrefabPath = !string.IsNullOrEmpty(_conversionReport.FallbackPrefabPath)
                ? _conversionReport.FallbackPrefabPath : _conversionReport.OutputPrefabPath;
            UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(preferredPrefabPath);
            _previewAvatarSource = prefab as GameObject;
            _previewManifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(_conversionReport.ManifestPath);
            _previewAvatarField.SetValueWithoutNotify(_previewAvatarSource);
            _previewManifestField.SetValueWithoutNotify(_previewManifestAsset);
            RefreshPreviewControls();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            ShowNotification(new GUIContent(string.IsNullOrEmpty(_conversionReport.FallbackPrefabPath)
                ? "Converted prefab and manifest created."
                : "Original and fallback-material prefabs created."));
        }

        private void InvalidateConversionReport()
        {
            _conversionReport = null;
            _conversionDiagnostics.Clear();
        }

        private void RefreshConversionControls()
        {
            if (_conversionStatus == null)
                return;
            bool hasScan = _report != null && _report.Root != null;
            bool hasConfiguration = _conversionConfigurationAsset != null;
            _exportYamlButton.SetEnabled(hasScan && !_isScanning);
            _validateConfigButton.SetEnabled(hasScan && hasConfiguration && !_isScanning);
            _dryRunButton.SetEnabled(hasScan && hasConfiguration && !_isScanning);
            _runConversionButton.SetEnabled(hasScan && hasConfiguration && !_isScanning &&
                                            _conversionReport != null && !_conversionReport.HasErrors);
            _conversionStatus.Clear();
            if (!hasConfiguration)
            {
                AddConversionMessage("Export or select a YAML configuration to begin.",
                    ConversionDiagnosticSeverity.Info);
                return;
            }
            if (_conversionReport != null)
            {
                int subMeshes = 0;
                foreach (RendererConversionPlan renderer in _conversionReport.Renderers)
                    subMeshes += renderer.OutputSubMeshes.Count;
                string summary = _conversionReport.HasErrors
                    ? "Validation failed; conversion is disabled."
                    : $"Ready: {_conversionReport.Renderers.Count} renderer(s), " +
                      $"{_conversionReport.TotalTriangleCount} triangle(s), {subMeshes} output submesh(es), " +
                      $"{_conversionReport.UnassignedTriangleCount} unassigned triangle(s).";
                AddConversionMessage(summary, _conversionReport.HasErrors
                    ? ConversionDiagnosticSeverity.Error : ConversionDiagnosticSeverity.Info);
                if (!_conversionReport.HasErrors && string.IsNullOrEmpty(_conversionReport.OutputPrefabPath) &&
                    _createFallbackVariant)
                    AddConversionMessage("Conversion will also create a separate persistent fallback-material prefab.",
                        ConversionDiagnosticSeverity.Info);
                if (!string.IsNullOrEmpty(_conversionReport.OutputPrefabPath))
                    AddConversionMessage("Created " + _conversionReport.OutputPrefabPath +
                                         " and " + _conversionReport.ManifestPath,
                        ConversionDiagnosticSeverity.Info);
                if (!string.IsNullOrEmpty(_conversionReport.FallbackPrefabPath))
                    AddConversionMessage("Created upload fallback variant " +
                                         _conversionReport.FallbackPrefabPath + " with " +
                                         _conversionReport.GeneratedFallbackMaterialPaths.Count +
                                         " persistent Standard material asset(s).",
                        ConversionDiagnosticSeverity.Info);
                foreach (RendererConversionPlan renderer in _conversionReport.Renderers)
                {
                    AddConversionMessage($"{renderer.RendererPath}: {renderer.TriangleCount} triangle(s), " +
                                         $"{renderer.OutputSubMeshes.Count} output submesh(es), " +
                                         $"{renderer.UnassignedTriangleCount} unassigned.",
                        ConversionDiagnosticSeverity.Info);
                    foreach (OutputSubMeshPlan output in renderer.OutputSubMeshes)
                    {
                        string group = output.IsUnassigned ? "Unassigned/pass-through" : output.GroupId;
                        AddConversionMessage($"  {group}: source material slot {output.SourceMaterialSlot} → " +
                                             $"output submesh {output.OutputSubMesh}, {output.TriangleCount} triangle(s).",
                            ConversionDiagnosticSeverity.Info);
                    }
                }
            }
            else if (_conversionDiagnostics.Count == 0)
                AddConversionMessage("Configuration changed. Validate or dry-run it before conversion.",
                    ConversionDiagnosticSeverity.Info);
            foreach (ConversionDiagnostic diagnostic in _conversionDiagnostics)
                AddConversionMessage(diagnostic.ToString(), diagnostic.Severity);
        }

        private void AddConversionMessage(string message, ConversionDiagnosticSeverity severity)
        {
            var label = new Label(message);
            label.AddToClassList("message-box");
            label.AddToClassList("wrap-text");
            label.AddToClassList(severity == ConversionDiagnosticSeverity.Error
                ? "message-error" : severity == ConversionDiagnosticSeverity.Warning
                    ? "message-warning" : "message-info");
            _conversionStatus.Add(label);
        }

        private void StartFeaturePreview()
        {
            EndFeaturePreview(false);
            if (_previewAvatarSource == null || _conversionConfigurationAsset == null ||
                _previewManifestAsset == null)
            {
                RefreshPreviewControls();
                return;
            }

            ConfigurationParseResult configuration = GeometryConfigurationYaml.Parse(
                _conversionConfigurationAsset.text);
            _previewDiagnostics.AddRange(configuration.Diagnostics);
            ManifestParseResult manifest = GeometryConversionManifestJson.Parse(_previewManifestAsset.text);
            _previewDiagnostics.AddRange(manifest.Diagnostics);
            if (!configuration.Success || !manifest.Success)
            {
                RefreshPreviewControls();
                return;
            }

            Material noRender = LoadAssetByGuid<Material>(NoRenderMaterialGuid);
            FeaturePreviewStartResult started = FeatureVisibilityPreviewSession.Start(
                _previewAvatarSource, configuration.Configuration, manifest.Manifest, noRender, _previewMaterialMode);
            _previewDiagnostics.AddRange(started.Diagnostics);
            _previewSession = started.Session;
            if (_previewSession != null)
            {
                _previewStatusMessage = "Preview active on a disposable scene clone. YAML defaults are applied.";
                Selection.activeGameObject = _previewSession.PreviewInstance;
                EditorGUIUtility.PingObject(_previewSession.PreviewInstance);
            }
            RefreshPreviewControls();
        }

        private void ResetFeaturePreview()
        {
            if (_previewSession == null || !_previewSession.IsActive) return;
            _previewSession.ResetToDefaults();
            _previewStatusMessage = "YAML default states restored.";
            RefreshPreviewControls();
        }

        private void RestorePreviewMaterials()
        {
            if (_previewSession == null || !_previewSession.IsActive) return;
            _previewSession.RestoreAllMaterials();
            _previewStatusMessage = "All preview materials restored. Changing a feature state will reapply the preview.";
            RefreshPreviewControls();
        }

        private void EndFeaturePreview(bool notify)
        {
            if (_previewSession != null)
            {
                _previewSession.EndPreview();
                _previewSession = null;
                if (notify) ShowNotification(new GUIContent("Feature preview ended and materials restored."));
            }
            _previewStatusMessage = null;
            _previewDiagnostics.Clear();
            if (_previewControls != null) RefreshPreviewControls();
        }

        private PreviewMaterialMode _previewMaterialMode = PreviewMaterialMode.Fallback;

        private void RefreshPreviewControls()
        {
            if (_previewStatus == null || _previewControls == null)
                return;
            bool active = _previewSession != null && _previewSession.IsActive;
            bool canStart = !active && _previewAvatarSource != null &&
                            _conversionConfigurationAsset != null && _previewManifestAsset != null;
            _startPreviewButton.SetEnabled(canStart);
            SetDisplayed(_previewActionRow, active);
            _previewStatus.Clear();
            _previewControls.Clear();
            if (!active && _previewDiagnostics.Count == 0)
            {
                AddPreviewMessage(canStart
                    ? "Ready to create a disposable preview instance."
                    : "Select a converted avatar, YAML configuration, and conversion manifest.",
                    ConversionDiagnosticSeverity.Info);
            }
            if (!string.IsNullOrEmpty(_previewStatusMessage))
                AddPreviewMessage(_previewStatusMessage, ConversionDiagnosticSeverity.Info);
            foreach (ConversionDiagnostic diagnostic in _previewDiagnostics)
                AddPreviewMessage(diagnostic.ToString(), diagnostic.Severity);
            var mode = new EnumField("Materials", _previewMaterialMode);
            mode.RegisterValueChangedCallback(evt =>
            {
                _previewMaterialMode = (PreviewMaterialMode)evt.newValue;
                if (_previewSession != null && _previewSession.IsActive)
                    _previewSession.SetMaterialMode(_previewMaterialMode);
                RefreshPreviewControls();
            });
            _previewControls.Add(mode);
            var description = new HelpBox(_previewMaterialMode == PreviewMaterialMode.Original
                ? "Original materials: shader UV discard/dissolve can prevent visible groups from appearing."
                : _previewMaterialMode == PreviewMaterialMode.Fallback
                    ? "Temporary Standard materials preserve exposed basic appearance properties. Advanced shader effects are omitted."
                    : "Untextured colours identify manifest geometry groups. Unmapped geometry uses a neutral colour.",
                _previewMaterialMode == PreviewMaterialMode.Original ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            _previewControls.Add(description);
            if (!active) return;
            foreach (string warning in _previewSession.MaterialWarnings)
                _previewControls.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
            if (_previewMaterialMode == PreviewMaterialMode.Diagnostic)
            {
                var legend = new Foldout { text = "Geometry group legend", value = false };
                foreach (PreviewLegendEntry entry in _previewSession.Legend)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    var swatch = new VisualElement();
                    swatch.style.backgroundColor = entry.Colour;
                    swatch.style.width = 18; swatch.style.height = 18; swatch.style.marginRight = 6;
                    row.Add(swatch);
                    var label = new Label($"{entry.GroupId} | {entry.RendererPath} | source slot {entry.SourceMaterialSlot} | UDIM {entry.Tiles} | output {entry.OutputSubMesh}");
                    label.AddToClassList("wrap-text");
                    row.Add(label); legend.Add(row);
                }
                _previewControls.Add(legend);
            }

            foreach (FeatureConfiguration feature in _previewSession.Features)
                _previewControls.Add(CreatePreviewFeatureControl(feature));
        }

        private VisualElement CreatePreviewFeatureControl(FeatureConfiguration feature)
        {
            var card = new VisualElement();
            card.AddToClassList("preview-feature-card");
            var title = new Label(feature.Id + " — " + FormatEnum(feature.Type));
            title.AddToClassList("preview-feature-label");
            card.Add(title);
            List<FeatureStateConfiguration> states = feature.States ?? new List<FeatureStateConfiguration>();
            if (feature.Type == GeometryFeatureType.Toggle && states.Count == 2)
            {
                FeatureStateConfiguration checkedState = states[0];
                FeatureStateConfiguration uncheckedState = states[1];
                int firstVisible = checkedState.VisibleGroups == null ? 0 : checkedState.VisibleGroups.Count;
                int secondVisible = uncheckedState.VisibleGroups == null ? 0 : uncheckedState.VisibleGroups.Count;
                if (secondVisible > firstVisible || (secondVisible == firstVisible &&
                    string.Equals(feature.DefaultState, uncheckedState.Id, StringComparison.Ordinal)))
                {
                    FeatureStateConfiguration swap = checkedState;
                    checkedState = uncheckedState;
                    uncheckedState = swap;
                }
                FeatureStateConfiguration onState = checkedState;
                FeatureStateConfiguration offState = uncheckedState;
                var toggle = new Toggle($"{offState.Id} / {onState.Id}")
                {
                    tooltip = $"Unchecked selects '{offState.Id}'; checked selects '{onState.Id}'."
                };
                toggle.SetValueWithoutNotify(string.Equals(_previewSession.GetActiveState(feature.Id),
                    onState.Id, StringComparison.Ordinal));
                toggle.RegisterValueChangedCallback(evt =>
                {
                    string state = evt.newValue ? onState.Id : offState.Id;
                    _previewSession.SetFeatureState(feature.Id, state);
                    _previewStatusMessage = $"Feature '{feature.Id}' set to '{state}'.";
                    RefreshPreviewStatusOnly();
                });
                card.Add(toggle);
            }
            else
            {
                var choices = new List<string>();
                foreach (FeatureStateConfiguration state in states) choices.Add(state.Id);
                var dropdown = new DropdownField("State", choices, 0);
                dropdown.SetValueWithoutNotify(_previewSession.GetActiveState(feature.Id));
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    _previewSession.SetFeatureState(feature.Id, evt.newValue);
                    _previewStatusMessage = $"Feature '{feature.Id}' set to '{evt.newValue}'.";
                    RefreshPreviewStatusOnly();
                });
                card.Add(dropdown);
            }
            return card;
        }

        private void RefreshPreviewStatusOnly()
        {
            _previewStatus.Clear();
            if (!string.IsNullOrEmpty(_previewStatusMessage))
                AddPreviewMessage(_previewStatusMessage, ConversionDiagnosticSeverity.Info);
            foreach (ConversionDiagnostic diagnostic in _previewDiagnostics)
                AddPreviewMessage(diagnostic.ToString(), diagnostic.Severity);
        }

        private void AddPreviewMessage(string message, ConversionDiagnosticSeverity severity)
        {
            var label = new Label(message);
            label.AddToClassList("message-box");
            label.AddToClassList("wrap-text");
            label.AddToClassList(severity == ConversionDiagnosticSeverity.Error
                ? "message-error" : severity == ConversionDiagnosticSeverity.Warning
                    ? "message-warning" : "message-info");
            _previewStatus.Add(label);
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
