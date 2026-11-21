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
            Assert.That(output.FallbackPrefabPath, Is.Null.Or.Empty);
            Assert.That(output.GeneratedFallbackMaterialPaths, Is.Empty);

            ManifestParseResult parsedManifest = GeometryConversionManifestJson.Parse(manifest);
            Assert.That(parsedManifest.Success, Is.True, JoinDiagnostics(parsedManifest.Diagnostics));
            Assert.That(parsedManifest.Manifest.renderers.Single().subMeshMappings, Has.Length.EqualTo(2));
        }

        [Test]
        public void Conversion_OptionalFallbackVariantCreatesPersistentSharedStandardMaterials()
        {
            MeshRenderer sourceRenderer = CreateStaticAvatar(MakeSharedBoundaryMesh());
            Object.DestroyImmediate(_material);
            _material = null;

            string rootFolder = "Assets/EditorTests/FallbackConversion_" + Guid.NewGuid().ToString("N");
            _generatedFolders.Add(rootFolder);
            Assert.That(AssetDatabase.CreateFolder("Assets/EditorTests",
                rootFolder.Substring(rootFolder.LastIndexOf('/') + 1)), Is.Not.Empty);
            var sourceMaterial = new Material(Shader.Find("Standard"))
            {
                name = "Locked Source",
                color = new Color(.2f, .4f, .6f, .8f)
            };
            sourceMaterial.SetFloat("_Metallic", .35f);
            sourceMaterial.SetFloat("_Glossiness", .6f);
            string sourceMaterialPath = rootFolder + "/Locked Source.mat";
            AssetDatabase.CreateAsset(sourceMaterial, sourceMaterialPath);
            _previewMaterials.Add(sourceMaterial);
            sourceRenderer.sharedMaterial = sourceMaterial;

            AvatarGeometryConfiguration configuration = MakeConfiguration(includeSecondTile: true);
            configuration.Settings.OutputDirectory = rootFolder + "/Output";
            GeometryConversionReport output = GeometryConversionOutput.Convert(
                _root, configuration, null, true);

            Assert.That(output.HasErrors, Is.False, JoinDiagnostics(output.Diagnostics));
            Assert.That(output.FallbackPrefabPath, Is.Not.Null.And.Not.Empty);
            Assert.That(output.FallbackPrefabPath, Is.Not.EqualTo(output.OutputPrefabPath));
            Assert.That(output.GeneratedFallbackMaterialPaths, Has.Count.EqualTo(1),
                "Repeated output submeshes using one source material should share one persistent fallback asset.");

            GameObject originalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(output.OutputPrefabPath);
            GameObject fallbackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(output.FallbackPrefabPath);
            Assert.That(originalPrefab, Is.Not.Null);
            Assert.That(fallbackPrefab, Is.Not.Null);
            Material[] originalMaterials = originalPrefab.transform.Find("Body")
                .GetComponent<MeshRenderer>().sharedMaterials;
            Material[] fallbackMaterials = fallbackPrefab.transform.Find("Body")
                .GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(originalMaterials.All(material => material == sourceMaterial), Is.True);
            Assert.That(fallbackMaterials, Has.Length.EqualTo(2));
            Assert.That(fallbackMaterials[0], Is.SameAs(fallbackMaterials[1]));
            Assert.That(fallbackMaterials[0], Is.Not.SameAs(sourceMaterial));
            Assert.That(fallbackMaterials[0].shader.name, Is.EqualTo("Standard"));
            Assert.That(fallbackMaterials[0].color, Is.EqualTo(sourceMaterial.color));
            Assert.That(fallbackMaterials[0].hideFlags, Is.EqualTo(HideFlags.None));
            Assert.That(AssetDatabase.GetAssetPath(fallbackMaterials[0]),
                Is.EqualTo(output.GeneratedFallbackMaterialPaths.Single()));
            Assert.That(fallbackMaterials[0].name,
                Is.EqualTo(Path.GetFileNameWithoutExtension(output.GeneratedFallbackMaterialPaths.Single())));
            Assert.That(fallbackPrefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Any(material => material != null && material.shader != null &&
                                 material.shader.name == "Hidden/NVH/UVTileScanner/NoRender"), Is.False);

            ManifestParseResult manifest = GeometryConversionManifestJson.Parse(
                File.ReadAllText(ToAbsolutePath(output.ManifestPath)));
            Assert.That(manifest.Success, Is.True, JoinDiagnostics(manifest.Diagnostics));
            Assert.That(manifest.Manifest.fallbackPrefabAssetPath, Is.EqualTo(output.FallbackPrefabPath));
            Assert.That(manifest.Manifest.fallbackPrefabGuid,
                Is.EqualTo(AssetDatabase.AssetPathToGUID(output.FallbackPrefabPath)));
            CollectionAssert.AreEqual(output.GeneratedFallbackMaterialPaths,
                manifest.Manifest.fallbackMaterialAssetPaths);
            Assert.That(sourceMaterial.color, Is.EqualTo(new Color(.2f, .4f, .6f, .8f)),
                "Fallback generation must not modify the source material asset.");
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
                _root, configuration, manifest, noRender, PreviewMaterialMode.Original);
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
            foreach (PreviewMaterialMode mode in Enum.GetValues(typeof(PreviewMaterialMode)))
            {
                started.Session.SetMaterialMode(mode);
                started.Session.SetFeatureState("multi-toggle", "on");
            }
            started.Session.EndPreview();

            GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Renderer prefabRenderer = reloaded.transform.Find("Body").GetComponent<MeshRenderer>();
            CollectionAssert.AreEqual(originals, prefabRenderer.sharedMaterials);
            CollectionAssert.AreEqual(sourceIndices,
                prefabRenderer.GetComponent<MeshFilter>().sharedMesh.GetIndices(0));
            CollectionAssert.AreEqual(originals, sourceRenderer.sharedMaterials);
        }

        [TestCase(PreviewMaterialMode.Original)]
        [TestCase(PreviewMaterialMode.Fallback)]
        [TestCase(PreviewMaterialMode.Diagnostic)]
        public void PreviewModes_PreserveStatesManifestOrderingAndUnownedGeometry(PreviewMaterialMode mode)
        {
            Renderer source = CreatePreviewAvatar(5);
            Material[] original = source.sharedMaterials;
            // Shared sources must still produce independent diagnostic groups.
            source.sharedMaterials = Enumerable.Repeat(original[0], 5).ToArray();
            var manifest = MakePreviewManifest(5);
            manifest.renderers[0].subMeshMappings[0].outputSubMesh = 1;
            manifest.renderers[0].subMeshMappings[1].outputSubMesh = 0;
            Material noRender = LoadNoRenderMaterial();
            var started = FeatureVisibilityPreviewSession.Start(_root, MakePreviewConfiguration(), manifest, noRender);
            Assert.That(started.Success, Is.True, JoinDiagnostics(started.Diagnostics));
            using (var session = started.Session)
            {
                Assert.That(session.MaterialMode, Is.EqualTo(PreviewMaterialMode.Fallback));
                var renderer = session.PreviewInstance.transform.Find("Body").GetComponent<Renderer>();
                session.SetFeatureState("multi-toggle", "on");
                session.SetFeatureState("mode", "right");
                session.SetMaterialMode(mode);
                Assert.That(session.GetActiveState("multi-toggle"), Is.EqualTo("on"));
                Assert.That(session.GetActiveState("mode"), Is.EqualTo("right"));
                Material[] materials = renderer.sharedMaterials;
                Assert.That(materials[2], Is.SameAs(noRender));
                foreach (int slot in new[] { 0, 1, 3, 4 }) Assert.That(materials[slot], Is.Not.SameAs(noRender));
                if (mode == PreviewMaterialMode.Original) Assert.That(materials[0], Is.SameAs(original[0]));
                if (mode == PreviewMaterialMode.Fallback)
                {
                    Assert.That(materials[0], Is.SameAs(materials[1]));
                    Assert.That(materials[0].shader.name, Is.EqualTo("Standard"));
                }
                if (mode == PreviewMaterialMode.Diagnostic)
                {
                    Assert.That(materials[0], Is.Not.SameAs(materials[1]));
                    Assert.That(materials[1].color, Is.EqualTo(PreviewMaterialCache.GroupColour("group-a")));
                    Assert.That(materials[0].HasProperty("_MainTex"), Is.False);
                    Assert.That(session.Legend.First(e => e.GroupId == "group-a").OutputSubMesh, Is.EqualTo(1));
                }
                session.SetFeatureState("multi-toggle", "off");
                Assert.That(renderer.sharedMaterials[0], Is.SameAs(noRender));
                Assert.That(renderer.sharedMaterials[1], Is.SameAs(noRender));
                session.SetFeatureState("multi-toggle", "on");
                Assert.That(renderer.sharedMaterials[0], Is.SameAs(materials[0]));
                session.EndPreview(); session.EndPreview();
                if (mode != PreviewMaterialMode.Original) Assert.That(materials[0] == null, Is.True);
                Assert.That(original[0] != null, Is.True);
                CollectionAssert.AreEqual(Enumerable.Repeat(original[0], 5), source.sharedMaterials);
            }
        }

        [Test]
        public void PreviewMaterials_CopyStandardAppearanceAndReleaseOnlyGeneratedMaterials()
        {
            var source = new Material(Shader.Find("Standard"));
            var texture = new Texture2D(2, 2);
            Material generated = null;
            Material diagnostic = null;
            try
            {
                source.color = new Color(.2f, .4f, .6f, .5f);
                source.SetTexture("_MainTex", texture);
                source.SetTextureScale("_MainTex", new Vector2(2, 3));
                source.SetTextureOffset("_MainTex", new Vector2(.1f, .2f));
                source.SetFloat("_Metallic", .7f); source.SetFloat("_Glossiness", .3f);
                source.SetTexture("_BumpMap", texture); source.SetTexture("_MetallicGlossMap", texture);
                source.SetTexture("_EmissionMap", texture); source.SetColor("_EmissionColor", Color.red);
                source.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                source.SetFloat("_Mode", 1); source.SetFloat("_Cutoff", .4f);
                using (var cache = new PreviewMaterialCache())
                {
                    generated = cache.Fallback(source);
                    Assert.That(cache.Fallback(source), Is.SameAs(generated));
                    Assert.That(generated.color, Is.EqualTo(source.color));
                    Assert.That(generated.GetTexture("_MainTex"), Is.SameAs(texture));
                    Assert.That(generated.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2, 3)));
                    Assert.That(generated.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(.1f, .2f)));
                    Assert.That(generated.GetFloat("_Metallic"), Is.EqualTo(.7f));
                    Assert.That(generated.GetFloat("_Glossiness"), Is.EqualTo(.3f));
                    Assert.That(generated.IsKeywordEnabled("_NORMALMAP"), Is.True);
                    Assert.That(generated.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.True);
                    Assert.That(generated.IsKeywordEnabled("_EMISSION"), Is.True);
                    Assert.That(generated.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                    Assert.That(generated.GetFloat("_Cutoff"), Is.EqualTo(.4f));
                    Assert.That(AssetDatabase.GetAssetPath(generated), Is.Empty);
                    diagnostic = cache.Diagnostic("group-a");
                    Assert.That(cache.Diagnostic("group-a"), Is.SameAs(diagnostic));
                    using (var other = new PreviewMaterialCache())
                        Assert.That(other.Diagnostic("group-a").color, Is.EqualTo(diagnostic.color));
                    Assert.That(cache.Diagnostic("group-b").color, Is.Not.EqualTo(diagnostic.color));
                }
                Assert.That(generated == null && diagnostic == null, Is.True);
                Assert.That(source.color, Is.EqualTo(new Color(.2f, .4f, .6f, .5f)));
                Assert.That(source.GetTexture("_MainTex"), Is.SameAs(texture));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(texture); }
        }

        [TestCase("Hidden/NVH/UVTileScanner/NoRender")]
        [TestCase(".poiyomi/Poiyomi Toon Synthetic")]
        public void PreviewMaterials_TolerateNullAndUnfamiliarProperties(string shaderName)
        {
            var source = new Material(Shader.Find(shaderName));
            try
            {
                using (var cache = new PreviewMaterialCache())
                {
                    Material missing = cache.Fallback(null);
                    Assert.That(cache.Fallback(null), Is.SameAs(missing));
                    Assert.That(missing.color, Is.EqualTo(Color.magenta));
                    Material unfamiliar = cache.Fallback(source);
                    Assert.That(unfamiliar.shader.name, Is.EqualTo("Standard"));
                    Assert.That(unfamiliar.color, Is.EqualTo(Color.magenta));
                    Assert.That(cache.Warnings.Count, Is.GreaterThanOrEqualTo(2));
                }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [TestCase("Standard", MaterialGlobalIlluminationFlags.EmissiveIsBlack, false)]
        [TestCase("Standard", MaterialGlobalIlluminationFlags.None, true)]
        [TestCase("Standard", MaterialGlobalIlluminationFlags.BakedEmissive, true)]
        [TestCase("Hidden/NVH/Tests/UnfamiliarEmission", MaterialGlobalIlluminationFlags.EmissiveIsBlack, false)]
        [TestCase("Hidden/NVH/Tests/UnfamiliarEmission", MaterialGlobalIlluminationFlags.None, true)]
        public void PreviewEmission_MatchesStandardValidationBeforeFirstAssignment(string shaderName,
            MaterialGlobalIlluminationFlags flags, bool expectedEmission)
        {
            var source = new Material(Shader.Find(shaderName));
            var texture = new Texture2D(2, 2);
            try
            {
                source.SetColor("_EmissionColor", new Color(.75f, 1.24f, 3f, 1));
                source.SetTexture("_EmissionMap", texture);
                source.globalIlluminationFlags = flags;
                string original = EditorJsonUtility.ToJson(source);
                using (var cache = new PreviewMaterialCache())
                {
                    Material material = cache.Fallback(source);
                    Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.EqualTo(expectedEmission),
                        "Stored HDR colours/maps alone must not enable an explicitly disabled emission effect.");
                    Assert.That(material.GetTexture("_EmissionMap"), Is.SameAs(texture));
                    string initialized = EditorJsonUtility.ToJson(material);
                    CreateStandardValidator().ValidateMaterial(material);
                    Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(initialized),
                        "Subsequent Standard validation must not change generated rendering state.");
                    if (!expectedEmission) Assert.That(cache.Warnings.Any(w => w.Contains("emission is disabled")), Is.True);
                }
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(original));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void PreviewEmission_UnrelatedImportInCleanProjectDoesNotChangeInitializedMaterial()
        {
            var source = new Material(Shader.Find("Standard"));
            try
            {
                source.SetColor("_EmissionColor", new Color(.75f, 1.24f, 3f, 1));
                source.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                using (var cache = new PreviewMaterialCache())
                {
                    Material material = cache.Fallback(source);
                    string before = EditorJsonUtility.ToJson(material);
                    AssetDatabase.ImportAsset("Assets/EditorTests/Fixtures/NawvaroFT.yaml", ImportAssetOptions.ForceUpdate);
                    Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
                    Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False);
                }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void PreviewEmission_StandardValidationNormalizesLegacyInconsistentState()
        {
            var material = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                material.SetColor("_EmissionColor", new Color(.75f, 1.24f, 3f, 1));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.EnableKeyword("_EMISSION");
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
                CreateStandardValidator().ValidateMaterial(material);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False,
                    "Control: Standard validation removes the inconsistent keyword without any third-party tooling.");
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void PreviewEmission_BlackBakedEmissionIsNormalizedWithoutChangingSource()
        {
            var source = new Material(Shader.Find("Standard"));
            try
            {
                source.SetColor("_EmissionColor", Color.black);
                source.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                using (var cache = new PreviewMaterialCache())
                {
                    Material material = cache.Fallback(source);
                    Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False);
                    Assert.That((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0, Is.True);
                    string before = EditorJsonUtility.ToJson(material);
                    CreateStandardValidator().ValidateMaterial(material);
                    Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
                }
                Assert.That(source.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.BakedEmissive));
            }
            finally { Object.DestroyImmediate(source); }
        }

        // Reflection is confined to tests: StandardShaderGUI is internal in Unity 2022.3.
        private static ShaderGUI CreateStandardValidator() => (ShaderGUI)Activator.CreateInstance(
            typeof(MaterialEditor).Assembly.GetType("UnityEditor.StandardShaderGUI", true));

        [TestCase(2, "_ALPHABLEND_ON", UnityEngine.Rendering.BlendMode.SrcAlpha)]
        [TestCase(3, "_ALPHAPREMULTIPLY_ON", UnityEngine.Rendering.BlendMode.One)]
        public void PreviewMaterials_PreserveStandardTransparency(int mode, string keyword,
            UnityEngine.Rendering.BlendMode blend)
        {
            var source = new Material(Shader.Find("Standard"));
            try
            {
                source.SetFloat("_Mode", mode);
                using (var cache = new PreviewMaterialCache())
                {
                    var material = cache.Fallback(source);
                    Assert.That(material.IsKeywordEnabled(keyword), Is.True);
                    Assert.That(material.GetInt("_SrcBlend"), Is.EqualTo((int)blend));
                    Assert.That(material.GetInt("_ZWrite"), Is.Zero);
                    Assert.That(material.renderQueue, Is.EqualTo(3000));
                }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void Preview_NullSourcesAndUnassignedGeometryRemainUsable()
        {
            Renderer source = CreatePreviewAvatar(5);
            source.sharedMaterials = new Material[5];
            var manifest = MakePreviewManifest(5);
            manifest.renderers[0].subMeshMappings[4].unassigned = true;
            var noRender = LoadNoRenderMaterial();
            var started = FeatureVisibilityPreviewSession.Start(_root, MakePreviewConfiguration(), manifest, noRender);
            Assert.That(started.Success, Is.True, JoinDiagnostics(started.Diagnostics));
            using (var session = started.Session)
            {
                var renderer = session.PreviewInstance.transform.Find("Body").GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterials[4].color, Is.EqualTo(Color.magenta));
                session.SetMaterialMode(PreviewMaterialMode.Diagnostic);
                Assert.That(renderer.sharedMaterials[4].color, Is.EqualTo(Color.gray));
                Assert.That(renderer.sharedMaterials[0], Is.SameAs(noRender));
                session.SetMaterialMode(PreviewMaterialMode.Original);
                Assert.That(renderer.sharedMaterials[4], Is.Null);
            }
            CollectionAssert.AreEqual(new Material[5], source.sharedMaterials);
        }

        [Test]
        public void Preview_InvalidModeStartupReleasesCloneAndCallbacks()
        {
            CreatePreviewAvatar(5);
            var started = FeatureVisibilityPreviewSession.Start(_root, MakePreviewConfiguration(),
                MakePreviewManifest(5), LoadNoRenderMaterial(), (PreviewMaterialMode)999);
            Assert.That(started.Success, Is.False);
            Assert.That(started.Diagnostics.Any(d => d.Code == "PREVIEW_START_FAILED"), Is.True);
            Assert.That(Object.FindObjectsOfType<GameObject>().Any(o => o.name.Contains("[UV Feature Preview]")), Is.False);
        }

        [Test]
        public void Preview_WindowDisableReleasesSessionMaterials()
        {
            CreatePreviewAvatar(5);
            var started = FeatureVisibilityPreviewSession.Start(_root, MakePreviewConfiguration(),
                MakePreviewManifest(5), LoadNoRenderMaterial());
            Assert.That(started.Success, Is.True, JoinDiagnostics(started.Diagnostics));
            var window = ScriptableObject.CreateInstance<UvTileScannerWindow>();
            var session = started.Session;
            var material = session.PreviewInstance.transform.Find("Body").GetComponent<Renderer>().sharedMaterials[2];
            try
            {
                typeof(UvTileScannerWindow).GetField("_previewSession", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, session);
                typeof(UvTileScannerWindow).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);
                Assert.That(session.IsActive, Is.False);
                Assert.That(material == null, Is.True);
            }
            finally { session.Dispose(); Object.DestroyImmediate(window); }
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
