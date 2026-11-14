using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nvh.UvTileScanner.Poiyomi;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nvh.UvTileScanner.Tests
{
    public sealed class UvTileScannerTests
    {
        private GameObject _root;
        private Mesh _mesh;
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<string> _assetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_mesh);
            foreach (Material material in _materials)
                Object.DestroyImmediate(material);
            _materials.Clear();
            foreach (string path in _assetPaths)
                AssetDatabase.DeleteAsset(path);
            _assetPaths.Clear();
        }

        [Test]
        public void Scan_GroupsMultipleUsesOfTheSameMesh()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh);
            AddMeshObject("Clothes", _mesh);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root);

            Assert.That(report.Meshes, Has.Count.EqualTo(1));
            Assert.That(report.Meshes[0].Usages, Has.Count.EqualTo(2));
            Assert.That(report.Meshes[0].Status, Is.EqualTo(UvTileScanStatus.MultipleTiles));
            Assert.That(report.Meshes[0].Tiles, Has.Count.EqualTo(2));
            Assert.That(report.Meshes[0].Tiles[0], Is.EqualTo(new UvTile(0, 0)));
            Assert.That(report.Meshes[0].Tiles[1], Is.EqualTo(new UvTile(1, 0)));
        }

        [Test]
        public void Scan_ReportsMissingSecondUvSet()
        {
            _root = new GameObject("Avatar");
            _mesh = new Mesh { name = "No UV2" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            AddMeshObject("Body", _mesh);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root);

            Assert.That(report.WithoutSecondUvSet, Is.EqualTo(1));
            Assert.That(report.Meshes[0].Status, Is.EqualTo(UvTileScanStatus.NoSecondUvSet));
        }

        [Test]
        public void Scan_DoesNotTreatUpperBoundaryAsAnotherTile()
        {
            _root = new GameObject("Avatar");
            _mesh = new Mesh { name = "Boundary UV2" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.uv2 = new[] { Vector2.zero, Vector2.right, Vector2.up };
            AddMeshObject("Body", _mesh);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root);

            Assert.That(report.Meshes[0].Status, Is.EqualTo(UvTileScanStatus.SingleTile));
            Assert.That(report.Meshes[0].Tiles[0], Is.EqualTo(new UvTile(0, 0)));
            Assert.That(report.Meshes[0].CrossedTilePrimitiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Scan_ReportsEveryMaterialSlotAndShaderPerRendererUsage()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null, "The built-in fallback shader should be available in Edit Mode.");

            var firstMaterial = new Material(shader) { name = "Body Main" };
            var secondMaterial = new Material(shader) { name = "Body Detail" };
            _materials.Add(firstMaterial);
            _materials.Add(secondMaterial);
            renderer.sharedMaterials = new[] { firstMaterial, secondMaterial };

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root);
            MeshUvScanResult meshResult = report.Meshes[0];
            MeshUsageInfo usage = meshResult.UsageDetails[0];

            Assert.That(usage.Renderer, Is.SameAs(renderer));
            Assert.That(usage.MaterialSlots, Has.Count.EqualTo(2));
            Assert.That(usage.MaterialSlots[0].SlotIndex, Is.EqualTo(0));
            Assert.That(usage.MaterialSlots[0].Material, Is.SameAs(firstMaterial));
            Assert.That(usage.MaterialSlots[0].Shader, Is.SameAs(shader));
            Assert.That(usage.MaterialSlots[1].SlotIndex, Is.EqualTo(1));
            Assert.That(usage.MaterialSlots[1].Material, Is.SameAs(secondMaterial));
            Assert.That(usage.MaterialSlots[1].Shader, Is.SameAs(shader));
            Assert.That(report.MaterialSlotCount, Is.EqualTo(2));
            Assert.That(report.UniqueMaterialCount, Is.EqualTo(2));
            Assert.That(report.UniqueShaderCount, Is.EqualTo(1));
        }

        [Test]
        public void UiToolkitAssets_AreImportableByTheirStableGuids()
        {
            const string uxmlGuid = "9084d5f243624d4fb8aa5b1f2cf47730";
            const string ussGuid = "cc49e43249c140d7a5f1aa623b3c811c";

            string uxmlPath = AssetDatabase.GUIDToAssetPath(uxmlGuid);
            string ussPath = AssetDatabase.GUIDToAssetPath(ussGuid);

            Assert.That(uxmlPath, Is.Not.Empty);
            Assert.That(ussPath, Is.Not.Empty);
            Assert.That(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath), Is.Not.Null);
        }

        [Test]
        public void UiToolkitWindow_ClonesItsUxmlControls()
        {
            UvTileScannerWindow window = ScriptableObject.CreateInstance<UvTileScannerWindow>();
            try
            {
                window.CreateGUI();

                Assert.That(window.rootVisualElement.Q<ObjectField>("avatar-root-field"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Toggle>("whole-project-animations-toggle"), Is.Not.Null);
                DropdownField animationCategory = window.rootVisualElement.Q<DropdownField>("animation-category-filter");
                Assert.That(animationCategory, Is.Not.Null);
                Assert.That(animationCategory.value, Is.EqualTo("UV Controls"));
                Assert.That(window.rootVisualElement.Q<ToolbarSearchField>("animation-search-field"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q("animation-results"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Button>("scan-button"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<ProgressBar>("scan-progress-bar"), Is.Not.Null);
                Button cancelButton = window.rootVisualElement.Q<Button>("cancel-scan-button");
                Assert.That(cancelButton, Is.Not.Null);
                Assert.That(cancelButton.text, Is.EqualTo("Cancel scan"));
                Assert.That(window.rootVisualElement.Q<ScrollView>("results-scroll"), Is.Not.Null);
                Label versionBadge = window.rootVisualElement.Q<Label>("tool-version");
                PackageManagerInfo package = PackageManagerInfo.FindForAssembly(typeof(UvTileScannerWindow).Assembly);
                Assert.That(package, Is.Not.Null);
                Assert.That(versionBadge, Is.Not.Null);
                Assert.That(versionBadge.text, Is.EqualTo("v" + package.version));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void UiToolkitWindow_NestedMaterialAnimationClipFoldoutRemainsExpanded()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            renderer.sharedMaterial = MakeMaterial("Body", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            AnimationClip clip = CreateClip("Expandable Clip", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1", 0f, 1f);
            AttachController(_root, clip, false, false);
            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                AnimationScanScope.AvatarLinked);
            MeshMaterialSlot slot = report.Meshes[0].UsageDetails[0].MaterialSlots[0];
            UvTileScannerWindow window = ScriptableObject.CreateInstance<UvTileScannerWindow>();
            try
            {
                window.CreateGUI();
                typeof(UvTileScannerWindow).GetField("_report", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, report);
                MethodInfo create = typeof(UvTileScannerWindow).GetMethod("CreateAnimationAnalysis",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var materialFoldout = (Foldout)create.Invoke(window, new object[] { slot });
                window.rootVisualElement.Add(materialFoldout);

                Foldout clipFoldout = window.rootVisualElement.Query<Foldout>().ToList()
                    .Single(candidate => candidate.text.StartsWith("Expandable Clip", StringComparison.Ordinal));
                clipFoldout.value = true;

                Assert.That(clipFoldout.value, Is.True);
                Assert.That(clipFoldout.parent, Is.Not.Null,
                    "A bubbled child event must not rebuild and detach the expanded clip foldout.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void DetectorRegistry_RunsMultipleDetectorsAndContainsFailures()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Registry Material", Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterial = material;
            var registry = new ShaderDetectorRegistry(new IShaderUvTileDetector[]
            {
                new FixedDetector("first"), new ThrowingDetector("broken"), new FixedDetector("second")
            });

            MaterialShaderAnalysisReport analysis = AvatarUvScanner.Scan(_root, true, registry)
                .Meshes[0].UsageDetails[0].MaterialSlots[0].ShaderAnalysis;

            Assert.That(analysis.Results.Select(result => result.DetectorId), Is.EquivalentTo(new[] { "first", "second" }));
            Assert.That(analysis.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("deliberate failure")), Is.True);
        }

        [Test]
        public void CoreScanner_WorksWithNoInstalledDetectors()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            renderer.sharedMaterial = MakeMaterial("No Detectors", Shader.Find("Hidden/InternalErrorShader"));

            MeshMaterialSlot slot = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()))
                .Meshes[0].UsageDetails[0].MaterialSlots[0];

            Assert.That(slot.ShaderAnalysis.Results, Is.Empty);
            Assert.That(slot.ShaderAnalysis.Unsupported, Is.True);
        }

        [Test]
        public void Scan_CachesDuplicateMaterialAnalysis()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoSubmeshTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Repeated", Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterials = new[] { material, material };
            var detector = new CountingDetector("counting");

            MeshUsageInfo usage = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(new[] { detector }))
                .Meshes[0].UsageDetails[0];

            Assert.That(detector.AnalysisCount, Is.EqualTo(1));
            Assert.That(usage.MaterialSlots[0].ShaderAnalysis, Is.SameAs(usage.MaterialSlots[1].ShaderAnalysis));
        }

        [Test]
        public void PoiyomiDetector_IdentifiesUnlockedShaderVersionAndTileFeatures()
        {
            Shader shader = Shader.Find(".poiyomi/Poiyomi Toon Synthetic");
            Assert.That(shader, Is.Not.Null, "The synthetic test shader was not imported.");
            Material material = MakeMaterial("Synthetic Poiyomi", shader);
            material.SetFloat("_UDIMDiscardRow0_1", 1f);
            material.SetFloat("_UVTileDissolveAlpha_Row0_0", 0.5f);

            ShaderAnalysisResult result = new PoiyomiShaderDetector().Analyze(material, new ShaderAnalysisContext());

            Assert.That(result.Classification, Is.EqualTo(ShaderFamilyClassification.Confirmed));
            Assert.That(result.Locked, Is.EqualTo(AnalysisTriState.No));
            Assert.That(result.Version.DisplayValue, Is.EqualTo("10.0.29"));
            Assert.That(result.Version.Kind, Is.EqualTo(ShaderVersionKind.Exact));
            Assert.That(result.Version.Method, Is.EqualTo("shader_master_label property description"));
            Assert.That(result.Version.CompatibilityFamily.DisplayValue, Is.EqualTo("10.0.x"));
            Assert.That(result.Version.CompatibilityFamily.Method,
                Is.EqualTo("Official PoiyomiVersionDetector.TryGetVersion"));
            Assert.That(result.Features.Any(feature => feature.Type == ShaderFeatureType.UvTileDiscard &&
                                                       feature.UvChannel == 1), Is.True);
            ShaderFeatureAnalysis dissolve = result.Features.Single(feature =>
                feature.Type == ShaderFeatureType.UvTileDissolve);
            Assert.That(dissolve.UvChannel, Is.EqualTo(0));
            Assert.That(dissolve.Controls[0].State, Is.EqualTo(ShaderTileState.PartialDissolve));
        }

        [Test]
        public void PoiyomiDetector_TreatsLegacyNameWithSignaturesAsProbable()
        {
            Shader shader = Shader.Find("Poiyomi/Legacy Synthetic");
            Assert.That(shader, Is.Not.Null);
            Material material = MakeMaterial("Legacy", shader);

            ShaderAnalysisResult result = new PoiyomiShaderDetector().Analyze(material, new ShaderAnalysisContext());

            Assert.That(result.Classification, Is.EqualTo(ShaderFamilyClassification.Probable));
            Assert.That(result.Version.Kind, Is.EqualTo(ShaderVersionKind.FamilyOrRange));
            Assert.That(result.Version.DisplayValue, Is.EqualTo("8.1.x family"));
            Assert.That(result.Version.CompatibilityFamily.DisplayValue, Is.EqualTo("8.1.x"));
        }

        [Test]
        public void PoiyomiDetector_ReportsLockedMissingPropertiesAsPartialInsteadOfGuessing()
        {
            Material material = MakeMaterial("Locked", Shader.Find("Hidden/InternalErrorShader"));
            material.SetOverrideTag("OriginalShader", ".poiyomi/Poiyomi Toon");

            ShaderAnalysisResult result = new PoiyomiShaderDetector().Analyze(material, new ShaderAnalysisContext());

            Assert.That(result.Classification, Is.EqualTo(ShaderFamilyClassification.Confirmed));
            Assert.That(result.Locked, Is.EqualTo(AnalysisTriState.Yes));
            Assert.That(result.Version.Kind, Is.EqualTo(ShaderVersionKind.Unknown));
            Assert.That(result.Version.CompatibilityFamily, Is.Null);
            Assert.That(result.Features, Is.Empty);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("baked")), Is.True);
        }

        [Test]
        public void PoiyomiDetector_ConfirmsKnownNonTargetShader()
        {
            Material material = MakeMaterial("Built In", Shader.Find("Hidden/InternalErrorShader"));

            ShaderAnalysisResult result = new PoiyomiShaderDetector().Analyze(material, new ShaderAnalysisContext());

            Assert.That(result.Classification, Is.EqualTo(ShaderFamilyClassification.ConfirmedNonTarget));
        }

        [Test]
        public void PoiyomiDetector_PreservesCompleteVersionReturnedByOfficialApi()
        {
            Material material = MakeMaterial("Official Exact", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));

            ShaderAnalysisResult result = new PoiyomiShaderDetector().Analyze(material, new ShaderAnalysisContext());

            Assert.That(result.Version.DisplayValue, Is.EqualTo("10.0.29"));
            Assert.That(result.Version.Kind, Is.EqualTo(ShaderVersionKind.Exact));
            Assert.That(result.Version.Method, Is.EqualTo("Official PoiyomiVersionDetector.TryGetVersion"));
            Assert.That(result.Version.CompatibilityFamily.DisplayValue, Is.EqualTo("10.0.x"));
        }

        [Test]
        public void Scan_CorrelatesEachMaterialSlotToItsActualSubmeshAndFeatureUvChannel()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoSubmeshTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Poiyomi", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            renderer.sharedMaterials = new[] { material, material };

            MeshUsageInfo usage = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }))
                .Meshes[0].UsageDetails[0];
            ShaderFeatureMeshCorrelation first = usage.MaterialSlots[0].FeatureCorrelations.Single(correlation =>
                correlation.FeatureType == ShaderFeatureType.UvTileDiscard);
            ShaderFeatureMeshCorrelation second = usage.MaterialSlots[1].FeatureCorrelations.Single(correlation =>
                correlation.FeatureType == ShaderFeatureType.UvTileDiscard);

            Assert.That(first.SubMeshIndices, Is.EqualTo(new[] { 0 }));
            Assert.That(first.OccupiedTiles, Is.EqualTo(new[] { new UvTile(0, 0) }));
            Assert.That(first.ConfiguredButUnoccupied, Contains.Item(new UvTile(1, 0)));
            Assert.That(second.SubMeshIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(second.OccupiedTiles, Is.EqualTo(new[] { new UvTile(1, 0) }));
            Assert.That(second.ConfiguredButUnoccupied, Is.Empty);
        }

        [Test]
        public void IncrementalMeshScan_ReportsRendererAndMeshAnalysisStages()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            var stages = new List<string>();
            AvatarUvScanReport completed = null;

            IEnumerator scan = AvatarUvScanner.ScanMeshAndShaderIncremental(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()),
                (value, message) => stages.Add(message), report => completed = report);
            while (scan.MoveNext()) { }

            Assert.That(completed, Is.Not.Null);
            Assert.That(completed.Meshes, Has.Count.EqualTo(1));
            Assert.That(stages, Has.Some.Contains("Finding renderers, meshes, and materials"));
            Assert.That(stages, Has.Some.Contains("Analyzing mesh UVs and shader features"));
        }

        [Test]
        public void AnimationAnalysis_ConfirmsIndexedRendererPropertyAndUvTileControl()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Poiyomi", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            renderer.sharedMaterial = material;
            AnimationClip clip = CreateClip("DissolveTile", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1", 0f, 1f);
            AttachController(_root, clip, false, false);

            MeshMaterialSlot slot = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                    AnimationScanScope.AvatarLinked)
                .Meshes[0].UsageDetails[0].MaterialSlots[0];

            AnimationBindingAnalysis binding = slot.AnimationBindings.Single();
            Assert.That(binding.BestConfidence, Is.EqualTo(AnimationMatchConfidence.Confirmed));
            Assert.That(binding.MaterialSlotIndex, Is.EqualTo(0));
            Assert.That(binding.TargetMaterial, Is.SameAs(material));
            Assert.That(binding.Keyframes, Has.Count.EqualTo(2));
            Assert.That(binding.Matches.Single().Feature.Type, Is.EqualTo(ShaderFeatureType.UvTileDissolve));
            Assert.That(binding.Matches.Single().UdimNumber, Is.EqualTo(1002));
        }

        [Test]
        public void AnimationAnalysis_UsesIndexedSlotAndSupportsSharedMaterialsAcrossRenderers()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoSubmeshTileMesh();
            Material material = MakeMaterial("Shared", Shader.Find("Hidden/InternalErrorShader"));
            MeshRenderer body = AddMeshObject("Body", _mesh);
            MeshRenderer clothes = AddMeshObject("Clothes", _mesh);
            body.sharedMaterials = new[] { material, material };
            clothes.sharedMaterials = new[] { material, material };
            AnimationClip bodyClip = CreateClip("BodySlotOne", "Body", typeof(MeshRenderer),
                "materials.Array.data[1]._Unknown", 0f, 1f);
            AnimationClip clothesClip = CreateClip("ClothesSlotOne", "Clothes", typeof(MeshRenderer),
                "materials.Array.data[1]._Unknown", 1f, 0f);
            AttachController(_root, bodyClip, false, false, clothesClip);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            List<MeshUsageInfo> usages = report.Meshes[0].UsageDetails.ToList();
            MeshMaterialSlot bodySlot = usages.Single(usage => usage.Renderer == body).MaterialSlots[1];
            MeshMaterialSlot clothesSlot = usages.Single(usage => usage.Renderer == clothes).MaterialSlots[1];

            Assert.That(bodySlot.AnimationBindings.Single().BestConfidence, Is.EqualTo(AnimationMatchConfidence.Confirmed));
            Assert.That(clothesSlot.AnimationBindings.Single().BestConfidence, Is.EqualTo(AnimationMatchConfidence.Confirmed));
            Assert.That(report.AnimationAnalysis.RelatedClipCount, Is.EqualTo(2));
        }

        [Test]
        public void AnimationAnalysis_FindsNestedStateMachineAndBlendTreePaths()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            GameObject nested = new GameObject("Nested");
            nested.transform.SetParent(_root.transform);
            MeshRenderer renderer = AddMeshObjectUnder(nested, "Body", _mesh);
            renderer.sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip clip = CreateClip("NestedBlend", "Nested/Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 0f, 1f);
            AttachController(_root, clip, true, true);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            AnimationClipAnalysis found = report.AnimationAnalysis.Clips.Single(result => result.Clip == clip);

            Assert.That(found.Bindings.Single().TargetRenderer, Is.SameAs(renderer));
            Assert.That(found.Sources.Any(source => source.Location.Contains("State machine") &&
                                                    source.Location.Contains("Blend Tree")), Is.True);
        }

        [Test]
        public void AnimationAnalysis_UsesOverrideClipsAndReadsMaterialSwapCurves()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material original = MakeMaterial("Original", Shader.Find("Hidden/InternalErrorShader"));
            Material replacement = MakeMaterial("Replacement", Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterial = original;
            AnimationClip baseClip = CreateClip("Base", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 0f, 1f);
            AnimationClip overrideClip = CreateObjectClip("OverrideSwap", "Body", typeof(MeshRenderer),
                "m_Materials.Array.data[0]", replacement);
            AttachOverrideController(_root, baseClip, overrideClip);

            MeshMaterialSlot slot = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked)
                .Meshes[0].UsageDetails[0].MaterialSlots[0];
            AnimationBindingAnalysis binding = slot.AnimationBindings.Single(result => result.Clip == overrideClip);

            Assert.That(binding.Kind, Is.EqualTo(AnimationBindingKind.ObjectReferenceCurve));
            Assert.That(binding.TargetMaterial, Is.SameAs(replacement));
            Assert.That(binding.BestConfidence, Is.EqualTo(AnimationMatchConfidence.Confirmed));
        }

        [Test]
        public void AnimationAnalysis_WholeProjectIncludesUnrelatedClipWithoutFalseConfirmation()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip unrelated = CreateClip("UnrelatedProjectClip", "NotOnAvatar", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 0f, 1f);

            AvatarUvScanReport linked = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            AvatarUvScanReport project = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.WholeProject);

            Assert.That(linked.AnimationAnalysis.Clips.Any(result => result.Clip == unrelated), Is.False);
            AnimationClipAnalysis result = project.AnimationAnalysis.Clips.Single(item => item.Clip == unrelated);
            Assert.That(result.Bindings.Single().BestConfidence, Is.EqualTo(AnimationMatchConfidence.Unknown));
        }

        [Test]
        public void AnimationAnalysis_HandlesMissingControllerAndRawUnknownProperties()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            renderer.sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip clip = CreateClip("RawUnknown", "Body", typeof(MeshRenderer), "material._NotRecognised", 0f, 0.5f);
            AttachController(_root, clip, false, false);
            var noController = new GameObject("NoController");
            noController.transform.SetParent(_root.transform);
            noController.AddComponent<Animator>(); // Intentionally has no controller.

            MeshMaterialSlot slot = AvatarUvScanner.Scan(_root, true,
                    new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked)
                .Meshes[0].UsageDetails[0].MaterialSlots[0];

            Assert.That(slot.AnimationBindings.Single().BestConfidence, Is.EqualTo(AnimationMatchConfidence.Probable));
            Assert.That(slot.AnimationBindings.Single().Matches.Single().Feature, Is.Null);
        }

        [Test]
        public void AnimationAnalysis_FindsLegacyAnimationComponentsAndHandlesEmptyCurves()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip legacy = CreateClip("Legacy", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 1f, 1f);
            legacy.legacy = true;
            EditorUtility.SetDirty(legacy);
            AssetDatabase.SaveAssets();
            _root.AddComponent<Animation>().AddClip(legacy, "Legacy");
            AnimationClip empty = CreateEmptyCurveClip("EmptyCurve", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown");
            AttachController(_root, empty, false, false);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);

            Assert.That(report.AnimationAnalysis.Clips.Any(result => result.Clip == legacy), Is.True);
            Assert.That(report.AnimationAnalysis.Clips.Any(result => result.Clip == empty), Is.True);
        }

        [Test]
        public void AnimationAnalysis_SkipsNamedDefaultClipBlocklist()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip humanoidDefault = CreateClip("HumanoidDefault", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Ignored", 0f, 1f);
            AnimationClip usefulClip = CreateClip("UsefulClip", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Useful", 0f, 1f);
            AttachController(_root, humanoidDefault, false, false, usefulClip);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);

            Assert.That(AvatarAnimationAnalyzer.IgnoredAnimationClipNames, Contains.Item("HumanoidDefault"));
            Assert.That(report.AnimationAnalysis.Clips.Any(result => result.Clip == humanoidDefault), Is.False);
            Assert.That(report.AnimationAnalysis.Clips.Any(result => result.Clip == usefulClip), Is.True);
        }

        [Test]
        public void AnimationFilter_SeparatesUvControlsAndUnknownMaterialProperties()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Filter Body Material", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            renderer.sharedMaterial = material;
            AnimationClip uv = CreateClip("UV Control", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1", 0f, 1f);
            AnimationClip other = CreateClip("Colour Control", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UnknownHueProperty", 0f, 0.5f);
            AttachController(_root, uv, false, false, other);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                AnimationScanScope.AvatarLinked);
            var index = new AnimationAnalysisFilterIndex(report.AnimationAnalysis);
            AnimationFilterResult uvResult = index.Filter(AnimationPropertyFilter.UvControls);
            AnimationFilterResult otherResult = index.Filter(AnimationPropertyFilter.OtherProperties);

            Assert.That(index.TotalMatchingBindingCount, Is.EqualTo(2));
            Assert.That(index.UvFeatureBindingCount, Is.EqualTo(1));
            Assert.That(index.OtherMaterialBindingCount, Is.EqualTo(1));
            Assert.That(uvResult.Clips.Single().Clip.Clip, Is.SameAs(uv));
            Assert.That(uvResult.Clips.Single().Targets.Single().Bindings.Single().IsUvControl, Is.True);
            Assert.That(otherResult.Clips.Single().Clip.Clip, Is.SameAs(other));
            Assert.That(otherResult.Clips.Single().Targets.Single().Bindings.Single().IsUvControl, Is.False);
        }

        [Test]
        public void AnimationFilter_GroupsMultipleBindingsUnderOneClipAndTarget()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            renderer.sharedMaterial = MakeMaterial("Body", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            AnimationClip clip = CreateClip("Multi Binding", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_0", 0f, 1f);
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
            {
                path = "Body",
                type = typeof(MeshRenderer),
                propertyName = "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1"
            }, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            AttachController(_root, clip, false, false);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                AnimationScanScope.AvatarLinked);
            AnimationFilterResult filtered = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.UvControls);

            Assert.That(filtered.Clips, Has.Count.EqualTo(1));
            Assert.That(filtered.Clips[0].VisibleBindingCount, Is.EqualTo(2));
            Assert.That(filtered.Clips[0].Targets, Has.Count.EqualTo(1));
            Assert.That(filtered.Clips[0].Targets[0].Bindings, Has.Count.EqualTo(2));
        }

        [Test]
        public void AnimationFilter_KeepsIdenticallyNamedClipAssetsSeparate()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip first = CreateClip("Same Name", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._First", 0f, 1f);
            AnimationClip second = CreateClip("Same Name", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Second", 1f, 0f);
            AttachController(_root, first, false, false, second);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            AnimationFilterResult filtered = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.AllProperties);

            Assert.That(filtered.Clips, Has.Count.EqualTo(2));
            Assert.That(filtered.Clips.Select(group => group.Clip.Clip), Is.EquivalentTo(new[] { first, second }));
            Assert.That(filtered.Clips[0].Clip.AssetPath, Is.Not.EqualTo(filtered.Clips[1].Clip.AssetPath));
        }

        [Test]
        public void AnimationFilter_UsesFeatureMetadataFromMultipleDetectorTypes()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Modular", Shader.Find("Hidden/InternalErrorShader"));
            AnimationClip first = CreateClip("First Detector", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._FirstDetectorTile", 0f, 1f);
            AnimationClip second = CreateClip("Second Detector", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._SecondDetectorTile", 1f, 0f);
            AttachController(_root, first, false, false, second);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[]
                {
                    new FirstTestUvDetector(), new SecondTestUvDetector()
                }), AnimationScanScope.AvatarLinked);
            AnimationFilterResult filtered = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.UvControls);

            Assert.That(filtered.VisibleBindingCount, Is.EqualTo(2));
            Assert.That(filtered.Clips.SelectMany(group => group.Targets)
                .SelectMany(group => group.Bindings)
                .SelectMany(binding => binding.Matches)
                .Select(match => match.ShaderAnalysis.DetectorId),
                Is.EquivalentTo(new[] { "first-test-uv", "second-test-uv" }));
        }

        [Test]
        public void AnimationFilter_GroupsMultipleRenderersAndMaterialsIndependently()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            MeshRenderer body = AddMeshObject("Body", _mesh);
            MeshRenderer clothes = AddMeshObject("Clothes", _mesh);
            Material bodyMaterial = MakeMaterial("Body Material", Shader.Find("Hidden/InternalErrorShader"));
            Material clothesMaterial = MakeMaterial("Clothes Material", Shader.Find("Hidden/InternalErrorShader"));
            body.sharedMaterial = bodyMaterial;
            clothes.sharedMaterial = clothesMaterial;
            AnimationClip bodyClip = CreateClip("Body Property", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 0f, 1f);
            AnimationClip clothesClip = CreateClip("Clothes Property", "Clothes", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 1f, 0f);
            AttachController(_root, bodyClip, false, false, clothesClip);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            AnimationFilterResult filtered = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.AllProperties);
            List<AnimationTargetGroup> targets = filtered.Clips.SelectMany(group => group.Targets).ToList();

            Assert.That(targets.Select(target => target.Renderer), Is.EquivalentTo(new[] { body, clothes }));
            Assert.That(targets.Select(target => target.Material), Is.EquivalentTo(new[] { bodyMaterial, clothesMaterial }));
        }

        [Test]
        public void AnimationFilter_PreservesProbableUnresolvedUvMatches()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Body", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            AnimationClip clip = CreateClip("Unresolved UV", "MissingRenderer", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1", 0f, 1f);
            AttachController(_root, clip, false, false);

            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                AnimationScanScope.AvatarLinked);
            AnimationFilteredBinding binding = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.UvControls).Clips.Single().Targets.Single().Bindings.Single();

            Assert.That(binding.TargetRenderer, Is.Null);
            Assert.That(binding.Matches.Single().Confidence, Is.EqualTo(AnimationMatchConfidence.Probable));
        }

        [Test]
        public void AnimationFilter_CombinesCategoryAndSearchAndReusesCachedResults()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeTwoTileMesh();
            AddMeshObject("Body", _mesh).sharedMaterial = MakeMaterial("Searchable Material", Shader.Find(".poiyomi/Poiyomi Toon Synthetic"));
            AnimationClip first = CreateClip("Search UV", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._UVTileDissolveAlpha_Row0_1", 0f, 1f);
            AnimationClip second = CreateClip("Search Other", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Brightness", 1f, 0f);
            AttachController(_root, first, false, false, second);
            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(new IShaderUvTileDetector[] { new PoiyomiShaderDetector() }),
                AnimationScanScope.AvatarLinked);
            AvatarAnimationAnalysisReport originalAnalysis = report.AnimationAnalysis;
            var index = new AnimationAnalysisFilterIndex(report.AnimationAnalysis);

            AnimationFilterResult uvUdim = index.Filter(AnimationPropertyFilter.UvControls, "UDIM 1002");
            AnimationFilterResult noOtherUdim = index.Filter(AnimationPropertyFilter.OtherProperties, "UDIM 1002");
            AnimationFilterResult allMaterial = index.Filter(AnimationPropertyFilter.AllProperties, "Searchable Material");
            AnimationFilterResult uvAgain = index.Filter(AnimationPropertyFilter.UvControls, "UDIM 1002");

            Assert.That(uvUdim.VisibleBindingCount, Is.EqualTo(1));
            Assert.That(noOtherUdim.VisibleBindingCount, Is.EqualTo(0));
            Assert.That(allMaterial.VisibleBindingCount, Is.EqualTo(2));
            Assert.That(uvAgain, Is.SameAs(uvUdim), "Switching filters must reuse cached scan-derived results.");
            Assert.That(report.AnimationAnalysis, Is.SameAs(originalAnalysis),
                "Filtering must not replace or rescan the animation report.");
        }

        [Test]
        public void AnimationFilter_HandlesEmptyReportsAndDestroyedTargetReferences()
        {
            _root = new GameObject("Avatar");
            AvatarUvScanReport emptyReport = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            AnimationFilterResult empty = new AnimationAnalysisFilterIndex(emptyReport.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.AllProperties, "anything");
            Assert.That(empty.VisibleBindingCount, Is.EqualTo(0));
            Assert.That(empty.Clips, Is.Empty);

            _mesh = MakeTwoTileMesh();
            MeshRenderer renderer = AddMeshObject("Body", _mesh);
            Material material = MakeMaterial("Temporary", Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterial = material;
            AnimationClip clip = CreateClip("Missing References", "Body", typeof(MeshRenderer),
                "materials.Array.data[0]._Unknown", 0f, 1f);
            AttachController(_root, clip, false, false);
            AvatarUvScanReport report = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()), AnimationScanScope.AvatarLinked);
            Object.DestroyImmediate(renderer.gameObject);
            Object.DestroyImmediate(material);
            _materials.Remove(material);

            AnimationFilterResult filtered = null;
            Assert.DoesNotThrow(() => filtered = new AnimationAnalysisFilterIndex(report.AnimationAnalysis)
                .Filter(AnimationPropertyFilter.AllProperties));
            Assert.That(filtered.VisibleBindingCount, Is.EqualTo(1));
            Assert.That(filtered.Clips.Single().Targets.Single().Renderer == null, Is.True);
            Assert.That(filtered.Clips.Single().Targets.Single().Material == null, Is.True);
        }

        private Mesh MakeTwoTileMesh()
        {
            var mesh = new Mesh { name = "Two Tile Mesh" };
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.up,
                Vector3.forward, Vector3.right + Vector3.forward, Vector3.up + Vector3.forward
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.uv2 = new[]
            {
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f),
                new Vector2(1.1f, 0.1f), new Vector2(1.9f, 0.1f), new Vector2(1.1f, 0.9f)
            };
            return mesh;
        }

        private Mesh MakeTwoSubmeshTileMesh()
        {
            var mesh = new Mesh { name = "Two Submesh Tile Mesh" };
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.up,
                Vector3.forward, Vector3.right + Vector3.forward, Vector3.up + Vector3.forward
            };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            Vector2[] tiles =
            {
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f),
                new Vector2(1.1f, 0.1f), new Vector2(1.9f, 0.1f), new Vector2(1.1f, 0.9f)
            };
            mesh.uv = tiles;
            mesh.uv2 = tiles;
            return mesh;
        }

        private Material MakeMaterial(string name, Shader shader)
        {
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = name };
            _materials.Add(material);
            return material;
        }

        private MeshRenderer AddMeshObject(string name, Mesh mesh)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform);
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            return renderer;
        }

        private MeshRenderer AddMeshObjectUnder(GameObject parent, string name, Mesh mesh)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            return renderer;
        }

        private AnimationClip CreateClip(string name, string path, Type type, string property, float first, float second)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
            {
                path = path,
                type = type,
                propertyName = property
            }, new AnimationCurve(new Keyframe(0f, first), new Keyframe(1f, second)));
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorTests/" + name + ".anim");
            AssetDatabase.CreateAsset(clip, assetPath);
            _assetPaths.Add(assetPath);
            return clip;
        }

        private AnimationClip CreateObjectClip(string name, string path, Type type, string property, Material material)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetObjectReferenceCurve(clip, new EditorCurveBinding
            {
                path = path,
                type = type,
                propertyName = property
            }, new[] { new ObjectReferenceKeyframe { time = 0f, value = material } });
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorTests/" + name + ".anim");
            AssetDatabase.CreateAsset(clip, assetPath);
            _assetPaths.Add(assetPath);
            return clip;
        }

        private AnimationClip CreateEmptyCurveClip(string name, string path, Type type, string property)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
            {
                path = path,
                type = type,
                propertyName = property
            }, new AnimationCurve());
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorTests/" + name + ".anim");
            AssetDatabase.CreateAsset(clip, assetPath);
            _assetPaths.Add(assetPath);
            return clip;
        }

        private void AttachController(GameObject target, AnimationClip clip, bool nestedStateMachine, bool blendTree,
            params AnimationClip[] additionalClips)
        {
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorTests/AnimationController.controller");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            _assetPaths.Add(assetPath);
            if (blendTree)
                controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            if (nestedStateMachine)
                stateMachine = stateMachine.AddStateMachine("Nested Machine");
            AddMotion(stateMachine, "Primary", clip, blendTree);
            foreach (AnimationClip additional in additionalClips)
                AddMotion(stateMachine, additional.name, additional, false);
            target.AddComponent<Animator>().runtimeAnimatorController = controller;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private void AttachOverrideController(GameObject target, AnimationClip baseClip, AnimationClip replacement)
        {
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorTests/AnimationController.controller");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            _assetPaths.Add(assetPath);
            AddMotion(controller.layers[0].stateMachine, "Base", baseClip, false);
            AssetDatabase.SaveAssets();
            var overrideController = new AnimatorOverrideController(controller);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);
            for (int index = 0; index < overrides.Count; index++)
            {
                if (overrides[index].Key == baseClip)
                    overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(baseClip, replacement);
            }
            overrideController.ApplyOverrides(overrides);
            target.AddComponent<Animator>().runtimeAnimatorController = overrideController;
        }

        private static void AddMotion(AnimatorStateMachine stateMachine, string name, AnimationClip clip, bool blendTree)
        {
            AnimatorState state = stateMachine.AddState(name);
            if (!blendTree)
            {
                state.motion = clip;
                return;
            }
            var tree = new BlendTree { name = "Test Blend Tree" };
            tree.AddChild(clip);
            state.motion = tree;
        }

        private sealed class FixedDetector : IShaderUvTileDetector
        {
            private readonly string _id;
            internal FixedDetector(string id) { _id = id; }
            public string Id => _id;
            public string DisplayName => _id;
            public bool CanAnalyze(Material material) => true;
            public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context) =>
                new ShaderAnalysisResult(Id, DisplayName, "Test", ShaderFamilyClassification.Confirmed,
                    AnalysisTriState.No, ShaderVersionInfo.Unknown());
        }

        private sealed class FirstTestUvDetector : IShaderUvTileDetector
        {
            public string Id => "first-test-uv";
            public string DisplayName => "First test UV detector";
            public bool CanAnalyze(Material material) => true;
            public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context) =>
                CreateTestUvResult(Id, DisplayName, "First detector feature", "_FirstDetectorTile", 0);
        }

        private sealed class SecondTestUvDetector : IShaderUvTileDetector
        {
            public string Id => "second-test-uv";
            public string DisplayName => "Second test UV detector";
            public bool CanAnalyze(Material material) => true;
            public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context) =>
                CreateTestUvResult(Id, DisplayName, "Second detector feature", "_SecondDetectorTile", 1);
        }

        private static ShaderAnalysisResult CreateTestUvResult(string id, string detectorName,
            string featureName, string propertyName, int column)
        {
            var control = new ShaderTileControl(0, column, propertyName, "Float", 0f,
                ShaderTileState.KeptOrInactive, FeatureReadability.Direct, true, true,
                AnalysisConfidence.Confirmed);
            var feature = new ShaderFeatureAnalysis(ShaderFeatureType.UvTileDiscard, featureName,
                AnalysisTriState.Yes, 1, new[] { control }, FeatureReadability.Direct,
                AnalysisConfidence.Confirmed);
            return new ShaderAnalysisResult(id, detectorName, "Synthetic modular shader",
                ShaderFamilyClassification.Confirmed, AnalysisTriState.No, ShaderVersionInfo.Unknown(),
                new[] { feature });
        }

        private sealed class CountingDetector : IShaderUvTileDetector
        {
            private readonly string _id;
            internal CountingDetector(string id) { _id = id; }
            internal int AnalysisCount { get; private set; }
            public string Id => _id;
            public string DisplayName => _id;
            public bool CanAnalyze(Material material) => true;
            public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context)
            {
                AnalysisCount++;
                return new ShaderAnalysisResult(Id, DisplayName, "Test", ShaderFamilyClassification.Confirmed,
                    AnalysisTriState.No, ShaderVersionInfo.Unknown());
            }
        }

        private sealed class ThrowingDetector : IShaderUvTileDetector
        {
            private readonly string _id;
            internal ThrowingDetector(string id) { _id = id; }
            public string Id => _id;
            public string DisplayName => _id;
            public bool CanAnalyze(Material material) => true;
            public ShaderAnalysisResult Analyze(Material material, ShaderAnalysisContext context) =>
                throw new InvalidOperationException("deliberate failure");
        }
    }
}

// Test double for the optional official Poiyomi 10 API. It deliberately returns major/minor
// for the normal fixtures, matching the current official implementation.
namespace Poi.Tools.ShaderTranslator.VersionUpgrade
{
    public static class PoiyomiVersionDetector
    {
        public static bool IsPoiyomiShader(Material material)
        {
            return material != null && material.shader != null &&
                   material.shader.name.StartsWith(".poiyomi/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetVersion(Material material, out Version version)
        {
            version = null;
            if (material == null || material.shader == null)
                return false;
            if (material.name == "Official Exact")
            {
                version = new Version(10, 0, 29);
                return true;
            }
            if (material.shader.name == ".poiyomi/Poiyomi Toon Synthetic")
            {
                version = new Version(10, 0);
                return true;
            }
            if (material.shader.name == "Poiyomi/Legacy Synthetic")
            {
                version = new Version(8, 1);
                return true;
            }
            return false;
        }
    }
}
