using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Nvh.UvTileScanner.Conversion;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nvh.UvTileScanner.Tests
{
    public sealed class GeometryConversionTests
    {
        private GameObject _root;
        private Mesh _mesh;
        private Material _material;
        private readonly List<Material> _previewMaterials = new List<Material>();
        private readonly List<string> _generatedFolders = new List<string>();

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_material != null) Object.DestroyImmediate(_material);
            foreach (Material material in _previewMaterials)
                if (material != null && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
            _previewMaterials.Clear();
            foreach (string folder in _generatedFolders)
                AssetDatabase.DeleteAsset(folder);
            _generatedFolders.Clear();
        }

        [Test]
        public void Yaml_RoundTripsCommentsQuotedStatesAndRejectsUnsafeMappings()
        {
            AvatarGeometryConfiguration configuration = MakeConfiguration();
            configuration.Features.Add(new FeatureConfiguration
            {
                Id = "visibility",
                Type = GeometryFeatureType.Toggle,
                DefaultState = "on",
                OwnedGroups = new List<string> { "tile-a" },
                States = new List<FeatureStateConfiguration>
                {
                    new FeatureStateConfiguration { Id = "on", VisibleGroups = new List<string> { "tile-a" } },
                    new FeatureStateConfiguration { Id = "off", VisibleGroups = new List<string>() }
                }
            });

            string yaml = "# comments are allowed\n" + GeometryConfigurationYaml.Serialize(configuration);
            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(yaml);

            Assert.That(parsed.Success, Is.True, JoinDiagnostics(parsed.Diagnostics));
            Assert.That(parsed.Configuration.Features.Single().DefaultState, Is.EqualTo("on"));
            Assert.That(GeometryConfigurationYaml.Serialize(parsed.Configuration),
                Is.EqualTo(GeometryConfigurationYaml.Serialize(configuration)));
            StringAssert.Contains("defaultState: \"on\"", yaml);

            AssertParseFails(yaml.Replace("schemaVersion: 1", "schemaVersion: 1\nschemaVersion: 1"), "Duplicate");
            AssertParseFails(yaml.Replace("schemaVersion: 1", "schemaVersion: 1\nunknownField: true"), "Unknown");
            AssertParseFails(yaml.Replace("schemaVersion: 1", "schemaVersion: 99"), "not supported");
            AssertParseFails(yaml.Replace("name: \"Avatar\"", "name: !unsafe \"Avatar\""), "tag");
            AssertParseFails(yaml.Replace("name: \"Avatar\"", "name: &avatar \"Avatar\""), "anchor");

            configuration.GeometryGroups.Clear();
            configuration.Features.Clear();
            ConfigurationParseResult emptyLists = GeometryConfigurationYaml.Parse(
                GeometryConfigurationYaml.Serialize(configuration));
            Assert.That(emptyLists.Success, Is.True, JoinDiagnostics(emptyLists.Diagnostics));
            Assert.That(emptyLists.Configuration.GeometryGroups, Is.Empty);
            Assert.That(emptyLists.Configuration.Features, Is.Empty);
        }

        [Test]
        public void NawvaroFixture_ParsesWithExplicitBaseMappingsAndQuotedToggleStates()
        {
            TextAsset fixture = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/EditorTests/Fixtures/NawvaroFT.yaml");
            Assert.That(fixture, Is.Not.Null);

            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(fixture.text);

            Assert.That(parsed.Success, Is.True, JoinDiagnostics(parsed.Diagnostics));
            Assert.That(parsed.Configuration.Avatar.Name, Is.EqualTo("Nawvaro FT"));
            Assert.That(parsed.Configuration.GeometryGroups.Select(group => group.Id),
                Is.EquivalentTo(new[] { "base-shared", "base-furry", "base-human", "base-horns" }));
            FeatureConfiguration horns = parsed.Configuration.Features.Single(feature => feature.Id == "horns");
            Assert.That(horns.DefaultState, Is.EqualTo("on"));
            Assert.That(horns.States.Select(state => state.Id), Is.EquivalentTo(new[] { "on", "off" }));
        }

        [Test]
        public void UdimAndTriangleClassification_HandleBoundariesAndSpanningTriangles()
        {
            Assert.That(GeometryConversionPlanner.TryUdimToTile(1001, out UvTile first), Is.True);
            Assert.That(first, Is.EqualTo(new UvTile(0, 0)));
            Assert.That(GeometryConversionPlanner.TryUdimToTile(1012, out UvTile twelfth), Is.True);
            Assert.That(twelfth, Is.EqualTo(new UvTile(1, 1)));
            Assert.That(GeometryConversionPlanner.TryTileToUdim(1, 1, out int udim), Is.True);
            Assert.That(udim, Is.EqualTo(1012));
            Assert.That(GeometryConversionPlanner.TryUdimToTile(1000, out _), Is.False);
            Assert.That(GeometryConversionPlanner.TryTileToUdim(-1, 0, out _), Is.False);

            Assert.That(GeometryConversionPlanner.TryClassifyTriangleTile(
                new Vector2(.2f, .2f), new Vector2(1f, .2f), new Vector2(1f, .8f),
                out UvTile boundaryTile, out _), Is.True);
            Assert.That(boundaryTile, Is.EqualTo(new UvTile(0, 0)));
            Assert.That(GeometryConversionPlanner.TryClassifyTriangleTile(
                new Vector2(.2f, .2f), new Vector2(1.2f, .2f), new Vector2(.2f, .8f),
                out _, out string spanning), Is.False);
            StringAssert.Contains("span", spanning);
            Assert.That(GeometryConversionPlanner.TryClassifyTriangleTile(
                new Vector2(-.8f, .2f), new Vector2(-.2f, .2f), new Vector2(-.2f, .8f),
                out _, out string negative), Is.False);
            StringAssert.Contains("negative", negative);
        }

        [Test]
        public void Planner_ResolvesRendererAndPartitionsSharedVerticesExactlyOnce()
        {
            MeshRenderer renderer = CreateStaticAvatar(MakeSharedBoundaryMesh());
            AvatarGeometryConfiguration configuration = MakeConfiguration(includeSecondTile: true);

            GeometryConversionReport report = GeometryConversionPlanner.ValidateAndPlan(_root, configuration);

            Assert.That(report.HasErrors, Is.False, JoinDiagnostics(report.Diagnostics));
            Assert.That(GeometryConversionPlanner.TryResolveRenderer(_root, "Body", out Renderer resolved,
                out Mesh resolvedMesh), Is.True);
            Assert.That(resolved, Is.SameAs(renderer));
            Assert.That(resolvedMesh, Is.SameAs(_mesh));
            RendererConversionPlan plan = report.Renderers.Single();
            Assert.That(plan.Assignments, Has.Count.EqualTo(2));
            Assert.That(plan.OutputSubMeshes, Has.Count.EqualTo(2));
            Assert.That(plan.OutputSubMeshes.Sum(item => item.TriangleCount), Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, plan.OutputSubMeshes[0].Indices);
            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, plan.OutputSubMeshes[1].Indices);
            Assert.That(plan.OutputSubMeshes[0].Indices.Intersect(plan.OutputSubMeshes[1].Indices).Count(),
                Is.EqualTo(2), "Output groups should retain shared source vertices.");
        }

        [Test]
        public void Planner_PreservesUnassignedTrianglesAndMaterialSlotProvenance()
        {
            CreateStaticAvatar(MakeTwoMaterialMesh(), materialSlots: 2);
            AvatarGeometryConfiguration configuration = MakeConfiguration();

            GeometryConversionReport report = GeometryConversionPlanner.ValidateAndPlan(_root, configuration);

            Assert.That(report.HasErrors, Is.False, JoinDiagnostics(report.Diagnostics));
            RendererConversionPlan plan = report.Renderers.Single();
            Assert.That(plan.TriangleCount, Is.EqualTo(2));
            Assert.That(plan.UnassignedTriangleCount, Is.EqualTo(1));
            Assert.That(plan.OutputSubMeshes, Has.Count.EqualTo(2));
            Assert.That(plan.OutputSubMeshes.Single(item => item.IsUnassigned).SourceMaterialSlot, Is.EqualTo(1));
            Assert.That(plan.OutputSubMeshes.Sum(item => item.TriangleCount), Is.EqualTo(plan.TriangleCount));
        }

        [Test]
        public void Validator_RejectsOverlapsFeatureConflictsAndInvalidDefaults()
        {
            CreateStaticAvatar(MakeSharedBoundaryMesh());
            AvatarGeometryConfiguration configuration = MakeConfiguration();
            configuration.GeometryGroups.Add(new GeometryGroupConfiguration
            {
                Id = "overlap", Renderer = "Body", MaterialSlots = new List<int> { 0 },
                UvChannel = 1, Tiles = new List<int> { 1001 }
            });
            configuration.Features.Add(new FeatureConfiguration
            {
                Id = "one", Type = GeometryFeatureType.Toggle, DefaultState = "missing",
                OwnedGroups = new List<string> { "tile-a" },
                States = new List<FeatureStateConfiguration>
                {
                    new FeatureStateConfiguration { Id = "on", VisibleGroups = new List<string> { "tile-a" } }
                }
            });
            configuration.Features.Add(new FeatureConfiguration
            {
                Id = "two", Type = GeometryFeatureType.Toggle, DefaultState = "off",
                OwnedGroups = new List<string> { "tile-a" },
                States = new List<FeatureStateConfiguration>
                {
                    new FeatureStateConfiguration { Id = "off", VisibleGroups = new List<string>() }
                }
            });

            GeometryConversionReport report = GeometryConversionPlanner.ValidateAndPlan(_root, configuration);

            Assert.That(report.HasErrors, Is.True);
            AssertCode(report, "GROUP_OVERLAP");
            AssertCode(report, "FEATURE_OWNERSHIP_CONFLICT");
            AssertCode(report, "DEFAULT_STATE_INVALID");
        }

        [Test]
        public void Validator_ReportsMissingRendererUvChannelInvalidSlotAndBoundaryPolicy()
        {
            CreateStaticAvatar(MakeSharedBoundaryMesh());
            AvatarGeometryConfiguration missing = MakeConfiguration();
            missing.GeometryGroups[0].Renderer = "Missing";
            AssertCode(GeometryConversionPlanner.ValidateAndPlan(_root, missing), "RENDERER_NOT_FOUND");

            AvatarGeometryConfiguration uv = MakeConfiguration();
            uv.GeometryGroups[0].UvChannel = 2;
            AssertCode(GeometryConversionPlanner.ValidateAndPlan(_root, uv), "UV_CHANNEL_MISSING");

            AvatarGeometryConfiguration slot = MakeConfiguration();
            slot.GeometryGroups[0].MaterialSlots[0] = 3;
            AssertCode(GeometryConversionPlanner.ValidateAndPlan(_root, slot), "MATERIAL_SLOT_OUT_OF_RANGE");

            _mesh.uv2 = new[]
            {
                new Vector2(.2f, .2f), new Vector2(1.2f, .2f),
                new Vector2(.2f, .8f), new Vector2(1.8f, .8f)
            };
            GeometryConversionReport boundary = GeometryConversionPlanner.ValidateAndPlan(_root, MakeConfiguration());
            AssertCode(boundary, "TRIANGLE_TILE_AMBIGUOUS");
            Assert.That(boundary.HasErrors, Is.True);
        }

        [Test]
        public void BuildMesh_PreservesSkinningBlendShapesBoundsAndVertexBuffers()
        {
            Mesh source = MakeSharedBoundaryMesh();
            source.normals = Enumerable.Repeat(Vector3.forward, source.vertexCount).ToArray();
            source.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), source.vertexCount).ToArray();
            source.colors = Enumerable.Repeat(Color.cyan, source.vertexCount).ToArray();
            source.bindposes = new[] { Matrix4x4.identity };
            source.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                source.vertexCount).ToArray();
            Vector3[] deltas = Enumerable.Repeat(new Vector3(0, .1f, 0), source.vertexCount).ToArray();
            source.AddBlendShapeFrame("Smile", 100f, deltas,
                new Vector3[source.vertexCount], new Vector3[source.vertexCount]);
            source.bounds = new Bounds(Vector3.one, Vector3.one * 4f);
            CreateSkinnedAvatar(source);

            GeometryConversionReport report = GeometryConversionPlanner.ValidateAndPlan(
                _root, MakeConfiguration(includeSecondTile: true));
            Assert.That(report.HasErrors, Is.False, JoinDiagnostics(report.Diagnostics));
            Mesh output = GeometryConversionOutput.BuildMesh(report.Renderers.Single());
            try
            {
                Assert.That(output.vertexCount, Is.EqualTo(source.vertexCount));
                CollectionAssert.AreEqual(source.vertices, output.vertices);
                CollectionAssert.AreEqual(source.normals, output.normals);
                CollectionAssert.AreEqual(source.tangents, output.tangents);
                CollectionAssert.AreEqual(source.colors, output.colors);
                CollectionAssert.AreEqual(source.bindposes, output.bindposes);
                Assert.That(output.boneWeights[0].weight0, Is.EqualTo(1f));
                Assert.That(output.blendShapeCount, Is.EqualTo(1));
                Assert.That(output.GetBlendShapeName(0), Is.EqualTo("Smile"));
                Assert.That(output.bounds, Is.EqualTo(source.bounds));
                Assert.That(source.subMeshCount, Is.EqualTo(1), "The original mesh must remain unchanged.");
            }
            finally { Object.DestroyImmediate(output); }
        }

        [Test]
        public void Conversion_CreatesPrefabMeshesAndVersionedManifestWithoutMutatingSource()
        {
            CreateStaticAvatar(MakeSharedBoundaryMesh());
            int[] originalIndices = _mesh.GetIndices(0);
            string outputFolder = "Assets/EditorTests/GeometryConversion_" + Guid.NewGuid().ToString("N");
            _generatedFolders.Add(outputFolder);
            AvatarGeometryConfiguration configuration = MakeConfiguration(includeSecondTile: true);
            configuration.Features.Add(new FeatureConfiguration
            {
                Id = "mode", Type = GeometryFeatureType.Exclusive, DefaultState = "a",
                OwnedGroups = new List<string> { "tile-a", "tile-b" },
                States = new List<FeatureStateConfiguration>
                {
                    new FeatureStateConfiguration { Id = "a", VisibleGroups = new List<string> { "tile-a" } },
                    new FeatureStateConfiguration { Id = "b", VisibleGroups = new List<string> { "tile-b" } }
                }
            });
            configuration.Settings.OutputDirectory = outputFolder;

            GeometryConversionReport output = GeometryConversionOutput.Convert(_root, configuration);

            Assert.That(output.HasErrors, Is.False, JoinDiagnostics(output.Diagnostics));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(output.OutputPrefabPath), Is.Not.Null);
            Mesh convertedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(output.GeneratedMeshPaths.Single());
            Assert.That(convertedMesh, Is.Not.Null);
            Assert.That(convertedMesh, Is.Not.SameAs(_mesh));
            Assert.That(convertedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(_mesh.subMeshCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(originalIndices, _mesh.GetIndices(0));
            string manifest = File.ReadAllText(ToAbsolutePath(output.ManifestPath));
            StringAssert.Contains("\"manifestVersion\": 1", manifest);
            StringAssert.Contains("\"sourceMaterialSlot\": 0", manifest);
            StringAssert.Contains("\"geometryGroup\": \"tile-a\"", manifest);
            StringAssert.Contains("\"defaultState\": \"a\"", manifest);
            Assert.That(AssetDatabase.AssetPathToGUID(output.OutputPrefabPath), Is.Not.Empty);

            ManifestParseResult parsedManifest = GeometryConversionManifestJson.Parse(manifest);
            Assert.That(parsedManifest.Success, Is.True, JoinDiagnostics(parsedManifest.Diagnostics));
            Assert.That(parsedManifest.Manifest.renderers.Single().subMeshMappings, Has.Length.EqualTo(2));
        }

        [Test]
        public void NoRenderAsset_IsBuiltInOnlyAndContainsNoVisibleOrDepthWritingPass()
        {
            string materialPath = AssetDatabase.GUIDToAssetPath("a8e39e8773144624967f9cd02fc4ecaf");
            Material noRender = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(noRender, Is.Not.Null);
            Assert.That(noRender.name, Is.EqualTo(Path.GetFileNameWithoutExtension(materialPath)),
                "Unity requires the material's main object name to match its asset filename.");
            Assert.That(noRender.shader, Is.Not.Null);
            Assert.That(noRender.shader.name, Is.EqualTo("Hidden/NVH/UVTileScanner/NoRender"));
            Assert.That(noRender.shader.passCount, Is.EqualTo(1));
            Assert.That(noRender.FindPass("NoRender"), Is.EqualTo(0));
            Assert.That(noRender.GetTag("ForceNoShadowCasting", false).ToLowerInvariant(), Is.EqualTo("true"));

            string shaderPath = AssetDatabase.GUIDToAssetPath("cb0c32702f284cc0afdfbc6e2e860540");
            string source = File.ReadAllText(ToAbsolutePath(shaderPath));
            StringAssert.Contains("ColorMask 0", source);
            StringAssert.Contains("ZWrite Off", source);
            StringAssert.Contains("clip(-1)", source);
            StringAssert.DoesNotContain("ShadowCaster", source);
            StringAssert.Contains("Fallback Off", source);
        }

        [Test]
        public void FeaturePreview_SwitchesToggleAndExclusiveStatesFromOriginalMaterialArrays()
        {
            Renderer sourceRenderer = CreatePreviewAvatar(5);
            Material[] sourceMaterials = (Material[])sourceRenderer.sharedMaterials.Clone();
            AvatarGeometryConfiguration configuration = MakePreviewConfiguration();
            GeometryConversionManifest manifest = MakePreviewManifest(5);
            Material noRender = LoadNoRenderMaterial();

            FeaturePreviewStartResult started = FeatureVisibilityPreviewSession.Start(
                _root, configuration, manifest, noRender);
            Assert.That(started.Success, Is.True, JoinDiagnostics(started.Diagnostics));
            FeatureVisibilityPreviewSession session = started.Session;
            GameObject preview = session.PreviewInstance;
            Renderer previewRenderer = preview.transform.Find("Body").GetComponent<MeshRenderer>();
            try
            {
                Assert.That((preview.hideFlags & HideFlags.DontSaveInEditor) != 0, Is.True);
                Assert.That(preview.transform.position, Is.Not.EqualTo(_root.transform.position),
                    "A scene-source clone must not overlap and visually mask the untouched source avatar.");
                Assert.That(started.Diagnostics.Any(item => item.Code == "PREVIEW_SCENE_CLONE_OFFSET"), Is.True);
                Material[] defaults = previewRenderer.sharedMaterials;
                Assert.That(defaults[0], Is.SameAs(noRender));
                Assert.That(defaults[1], Is.SameAs(noRender));
                Assert.That(defaults[2], Is.SameAs(sourceMaterials[2]));
                Assert.That(defaults[3], Is.SameAs(noRender));
                Assert.That(defaults[4], Is.SameAs(sourceMaterials[4]), "Unowned geometry must stay untouched.");

                Assert.That(session.SetFeatureState("multi-toggle", "on"), Is.True);
                Assert.That(session.SetFeatureState("mode", "right"), Is.True);
                Material[] switched = previewRenderer.sharedMaterials;
                Assert.That(switched[0], Is.SameAs(sourceMaterials[0]));
                Assert.That(switched[1], Is.SameAs(sourceMaterials[1]));
                Assert.That(switched[2], Is.SameAs(noRender));
                Assert.That(switched[3], Is.SameAs(sourceMaterials[3]));
                Assert.That(switched[4], Is.SameAs(sourceMaterials[4]));

                session.RestoreAllMaterials();
                CollectionAssert.AreEqual(sourceMaterials, previewRenderer.sharedMaterials);
                session.SetFeatureState("multi-toggle", "off");
                Material[] reapplied = previewRenderer.sharedMaterials;
                Assert.That(reapplied[0], Is.SameAs(noRender));
                Assert.That(reapplied[1], Is.SameAs(noRender));
                Assert.That(reapplied[2], Is.SameAs(noRender));
                Assert.That(reapplied[3], Is.SameAs(sourceMaterials[3]));

                session.ResetToDefaults();
                Assert.That(session.GetActiveState("multi-toggle"), Is.EqualTo("off"));
                Assert.That(session.GetActiveState("mode"), Is.EqualTo("left"));
            }
            finally
            {
                session.EndPreview();
            }

            Assert.That(preview == null, Is.True, "End Preview must destroy its disposable instance.");
            CollectionAssert.AreEqual(sourceMaterials, sourceRenderer.sharedMaterials,
                "The selected source avatar must never receive preview materials.");
        }

        [Test]
        public void FeaturePreview_ReportsMissingGroupsInvalidSlotsAndCleansFailedClone()
        {
            Renderer sourceRenderer = CreatePreviewAvatar(2);
            Material[] originals = (Material[])sourceRenderer.sharedMaterials.Clone();
            AvatarGeometryConfiguration configuration = MakePreviewConfiguration();
            GeometryConversionManifest manifest = MakePreviewManifest(2);
            manifest.renderers[0].subMeshMappings[1].outputSubMesh = 99;

            FeaturePreviewStartResult failed = FeatureVisibilityPreviewSession.Start(
                _root, configuration, manifest, LoadNoRenderMaterial());

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Diagnostics.Any(item => item.Code == "PREVIEW_SUBMESH_INVALID"), Is.True,
                JoinDiagnostics(failed.Diagnostics));
            Assert.That(Object.FindObjectsOfType<GameObject>().Any(item =>
                item.name.Contains("[UV Feature Preview]")), Is.False);
            CollectionAssert.AreEqual(originals, sourceRenderer.sharedMaterials);

            manifest = MakePreviewManifest(2);
            manifest.renderers[0].subMeshMappings = new[] { manifest.renderers[0].subMeshMappings[0] };
            FeaturePreviewStartResult missing = FeatureVisibilityPreviewSession.Start(
                _root, configuration, manifest, LoadNoRenderMaterial());
            Assert.That(missing.Success, Is.True, JoinDiagnostics(missing.Diagnostics));
            Assert.That(missing.Diagnostics.Any(item => item.Code == "PREVIEW_GROUP_MAPPING_MISSING"), Is.True);
            Object.DestroyImmediate(missing.Session.PreviewInstance);
            Assert.DoesNotThrow(() => missing.Session.EndPreview(),
                "End Preview should tolerate a clone that the user deleted manually.");
        }

        [Test]
        public void FeaturePreview_FromPrefabAssetLeavesPrefabMaterialsAndMeshUnchanged()
        {
            Renderer sourceRenderer = CreatePreviewAvatar(5);
            Material[] originals = (Material[])sourceRenderer.sharedMaterials.Clone();
            int[] sourceIndices = _mesh.GetIndices(0);
            string folder = "Assets/EditorTests/FeaturePreview_" + Guid.NewGuid().ToString("N");
            _generatedFolders.Add(folder);
            AssetDatabase.CreateFolder("Assets/EditorTests", folder.Substring(folder.LastIndexOf('/') + 1));
            Mesh assetMesh = Object.Instantiate(_mesh);
            string meshPath = folder + "/ConvertedMesh.asset";
            AssetDatabase.CreateAsset(assetMesh, meshPath);
            sourceRenderer.GetComponent<MeshFilter>().sharedMesh = assetMesh;
            sourceIndices = assetMesh.GetIndices(0);
            for (int index = 0; index < originals.Length; index++)
                AssetDatabase.CreateAsset(originals[index], folder + "/Material" + index + ".mat");
            string prefabPath = folder + "/Converted.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, prefabPath);
            Assert.That(prefab, Is.Not.Null);

            FeaturePreviewStartResult started = FeatureVisibilityPreviewSession.Start(prefab,
                MakePreviewConfiguration(), MakePreviewManifest(5), LoadNoRenderMaterial());
            Assert.That(started.Success, Is.True, JoinDiagnostics(started.Diagnostics));
            started.Session.SetFeatureState("multi-toggle", "on");
            started.Session.EndPreview();

            GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Renderer prefabRenderer = reloaded.transform.Find("Body").GetComponent<MeshRenderer>();
            CollectionAssert.AreEqual(originals, prefabRenderer.sharedMaterials);
            CollectionAssert.AreEqual(sourceIndices,
                prefabRenderer.GetComponent<MeshFilter>().sharedMesh.GetIndices(0));
            CollectionAssert.AreEqual(originals, sourceRenderer.sharedMaterials);
        }

        [Test]
        public void TemplateExporter_UsesScannerFactsAndDoesNotInferFeatures()
        {
            CreateStaticAvatar(MakeSharedBoundaryMesh());
            AvatarUvScanReport scan = AvatarUvScanner.Scan(_root);

            string yaml = ConfigurationTemplateExporter.Export(scan);
            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(yaml);

            Assert.That(parsed.Success, Is.True, JoinDiagnostics(parsed.Diagnostics));
            StringAssert.Contains("renderer: \"Body\"", yaml);
            StringAssert.Contains("materialSlots: [0]", yaml);
            StringAssert.Contains("available UV channels", yaml);
            StringAssert.Contains("features: []", yaml);
            Assert.That(parsed.Configuration.Features, Is.Empty);
        }

        [Test]
        public void TemplateExporter_WithNoMaterialSlotsStillProducesParseableEmptyGroups()
        {
            _root = new GameObject("Avatar");
            _mesh = MakeSharedBoundaryMesh();
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            body.AddComponent<MeshFilter>().sharedMesh = _mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = Array.Empty<Material>();
            AvatarUvScanReport scan = AvatarUvScanner.Scan(_root);

            string yaml = ConfigurationTemplateExporter.Export(scan);
            ConfigurationParseResult parsed = GeometryConfigurationYaml.Parse(yaml);

            Assert.That(parsed.Success, Is.True, JoinDiagnostics(parsed.Diagnostics));
            Assert.That(parsed.Configuration.GeometryGroups, Is.Empty);
            StringAssert.Contains("geometryGroups: []", yaml);
        }

        [Test]
        public void Converter_HasNoPoiyomiDependencyAndAcceptsExistingScanReport()
        {
            CreateStaticAvatar(MakeSharedBoundaryMesh());
            Assembly conversion = typeof(GeometryConversionPlanner).Assembly;
            Assert.That(conversion.GetReferencedAssemblies().Any(
                name => name.Name.IndexOf("Poiyomi", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);

            AvatarUvScanReport scan = AvatarUvScanner.Scan(_root, true,
                new ShaderDetectorRegistry(Array.Empty<IShaderUvTileDetector>()));
            GeometryConversionReport report = GeometryConversionPlanner.ValidateAndPlan(
                _root, MakeConfiguration(includeSecondTile: true), scan);

            Assert.That(report.HasErrors, Is.False, JoinDiagnostics(report.Diagnostics));
            Assert.That(report.Renderers.Single().SourceMesh, Is.SameAs(scan.Meshes.Single().Mesh));
        }

        private Renderer CreatePreviewAvatar(int slotCount)
        {
            _root = new GameObject("Converted Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            _mesh = new Mesh { name = "Converted Preview Mesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.subMeshCount = slotCount;
            for (int slot = 0; slot < slotCount; slot++)
                _mesh.SetTriangles(new[] { 0, 1, 2 }, slot);
            body.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = body.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            var materials = new Material[slotCount];
            for (int slot = 0; slot < slotCount; slot++)
            {
                materials[slot] = new Material(shader) { name = "Preview Source " + slot };
                _previewMaterials.Add(materials[slot]);
            }
            renderer.sharedMaterials = materials;
            return renderer;
        }

        private static AvatarGeometryConfiguration MakePreviewConfiguration()
        {
            return new AvatarGeometryConfiguration
            {
                Avatar = new AvatarConfiguration { Name = "Converted Avatar" },
                GeometryGroups = new List<GeometryGroupConfiguration>
                {
                    new GeometryGroupConfiguration { Id = "group-a" },
                    new GeometryGroupConfiguration { Id = "group-b" },
                    new GeometryGroupConfiguration { Id = "group-left" },
                    new GeometryGroupConfiguration { Id = "group-right" },
                    new GeometryGroupConfiguration { Id = "unowned" }
                },
                Features = new List<FeatureConfiguration>
                {
                    new FeatureConfiguration
                    {
                        Id = "multi-toggle", Type = GeometryFeatureType.Toggle, DefaultState = "off",
                        OwnedGroups = new List<string> { "group-a", "group-b" },
                        States = new List<FeatureStateConfiguration>
                        {
                            new FeatureStateConfiguration
                            {
                                Id = "on", VisibleGroups = new List<string> { "group-a", "group-b" }
                            },
                            new FeatureStateConfiguration { Id = "off", VisibleGroups = new List<string>() }
                        }
                    },
                    new FeatureConfiguration
                    {
                        Id = "mode", Type = GeometryFeatureType.Exclusive, DefaultState = "left",
                        OwnedGroups = new List<string> { "group-left", "group-right" },
                        States = new List<FeatureStateConfiguration>
                        {
                            new FeatureStateConfiguration
                            {
                                Id = "left", VisibleGroups = new List<string> { "group-left" }
                            },
                            new FeatureStateConfiguration
                            {
                                Id = "right", VisibleGroups = new List<string> { "group-right" }
                            }
                        }
                    }
                }
            };
        }

        private static GeometryConversionManifest MakePreviewManifest(int mappingCount)
        {
            string[] groups = { "group-a", "group-b", "group-left", "group-right", "unowned" };
            var mappings = new List<ManifestSubMeshMapping>();
            for (int index = 0; index < mappingCount && index < groups.Length; index++)
                mappings.Add(new ManifestSubMeshMapping
                {
                    outputSubMesh = index,
                    sourceSubMesh = index,
                    sourceMaterialSlot = index,
                    geometryGroup = groups[index],
                    triangleCount = 1
                });
            AvatarGeometryConfiguration configuration = MakePreviewConfiguration();
            var features = new List<ManifestFeature>();
            foreach (FeatureConfiguration feature in configuration.Features)
            {
                var states = new List<ManifestFeatureState>();
                foreach (FeatureStateConfiguration state in feature.States)
                    states.Add(new ManifestFeatureState
                    {
                        id = state.Id,
                        visibleGroups = state.VisibleGroups.ToArray()
                    });
                features.Add(new ManifestFeature
                {
                    id = feature.Id,
                    type = feature.Type == GeometryFeatureType.Toggle ? "toggle" : "exclusive",
                    defaultState = feature.DefaultState,
                    ownedGroups = feature.OwnedGroups.ToArray(),
                    states = states.ToArray()
                });
            }
            return new GeometryConversionManifest
            {
                manifestVersion = GeometryConversionOutput.ManifestVersion,
                configurationSchemaVersion = GeometryConfigurationYaml.SupportedSchemaVersion,
                configuredAvatarName = "Converted Avatar",
                renderers = new[]
                {
                    new ManifestRenderer
                    {
                        rendererPath = "Body",
                        rendererType = "MeshRenderer",
                        subMeshMappings = mappings.ToArray()
                    }
                },
                features = features.ToArray()
            };
        }

        private static Material LoadNoRenderMaterial()
        {
            string path = AssetDatabase.GUIDToAssetPath("a8e39e8773144624967f9cd02fc4ecaf");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null);
            return material;
        }

        private MeshRenderer CreateStaticAvatar(Mesh mesh, int materialSlots = 1)
        {
            _root = new GameObject("Avatar");
            _mesh = mesh;
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = body.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            _material = new Material(shader) { name = "Source Material" };
            renderer.sharedMaterials = Enumerable.Repeat(_material, materialSlots).ToArray();
            return renderer;
        }

        private void CreateSkinnedAvatar(Mesh mesh)
        {
            _root = new GameObject("Avatar");
            _mesh = mesh;
            var bone = new GameObject("Bone");
            bone.transform.SetParent(_root.transform);
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            _material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterial = _material;
        }

        private Mesh MakeSharedBoundaryMesh()
        {
            var mesh = new Mesh { name = "Shared Boundary Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.uv2 = new[]
            {
                new Vector2(.2f, .2f), new Vector2(1f, .2f),
                new Vector2(1f, .8f), new Vector2(1.8f, .5f)
            };
            return mesh;
        }

        private Mesh MakeTwoMaterialMesh()
        {
            Mesh mesh = MakeSharedBoundaryMesh();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 1, 3, 2 }, 1);
            return mesh;
        }

        private static AvatarGeometryConfiguration MakeConfiguration(bool includeSecondTile = false)
        {
            var configuration = new AvatarGeometryConfiguration
            {
                Avatar = new AvatarConfiguration { Name = "Avatar" },
                Settings = new ConversionSettings
                {
                    OutputDirectory = "Assets/UVTileScannerOutput",
                    UnassignedGeometry = UnassignedGeometryHandling.Preserve,
                    TriangleBoundaries = TriangleBoundaryPolicy.Reject
                }
            };
            configuration.GeometryGroups.Add(new GeometryGroupConfiguration
            {
                Id = "tile-a", Renderer = "Body", MaterialSlots = new List<int> { 0 },
                UvChannel = 1, Tiles = new List<int> { 1001 }
            });
            if (includeSecondTile)
                configuration.GeometryGroups.Add(new GeometryGroupConfiguration
                {
                    Id = "tile-b", Renderer = "Body", MaterialSlots = new List<int> { 0 },
                    UvChannel = 1, Tiles = new List<int> { 1002 }
                });
            return configuration;
        }

        private static void AssertParseFails(string yaml, string expected)
        {
            ConfigurationParseResult result = GeometryConfigurationYaml.Parse(yaml);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item =>
                item.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                JoinDiagnostics(result.Diagnostics));
            Assert.That(result.Diagnostics[0].Line, Is.GreaterThan(0));
        }

        private static void AssertCode(GeometryConversionReport report, string code)
        {
            Assert.That(report.Diagnostics.Any(item => item.Code == code), Is.True, JoinDiagnostics(report.Diagnostics));
        }

        private static string JoinDiagnostics(IReadOnlyList<ConversionDiagnostic> diagnostics) =>
            string.Join("\n", diagnostics.Select(item => item.ToString()).ToArray());

        private static string ToAbsolutePath(string assetPath) => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, assetPath);
    }
}
