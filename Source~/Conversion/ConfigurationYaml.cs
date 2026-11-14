using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Nvh.UvTileScanner.Conversion
{
    public static class GeometryConfigurationYaml
    {
        public const int SupportedSchemaVersion = 1;

        public static ConfigurationParseResult Parse(string yaml)
        {
            var diagnostics = new List<ConversionDiagnostic>();
            if (string.IsNullOrWhiteSpace(yaml))
            {
                diagnostics.Add(Error("YAML_EMPTY", "The YAML configuration is empty."));
                return new ConfigurationParseResult(null, diagnostics);
            }

            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));
                if (stream.Documents.Count != 1)
                    throw new ConfigurationException(null, "Exactly one YAML document is required.");
                YamlNode rootNode = stream.Documents[0].RootNode;
                ValidateSafeNode(rootNode);
                YamlMappingNode root = RequireMapping(rootNode, "configuration root");
                Dictionary<string, YamlNode> rootFields = Fields(root,
                    "schemaVersion", "avatar", "geometryGroups", "features", "settings");

                var configuration = new AvatarGeometryConfiguration
                {
                    SchemaVersion = RequiredInt(rootFields, "schemaVersion", root),
                    Avatar = ParseAvatar(Required(rootFields, "avatar", root)),
                    GeometryGroups = ParseGroups(Required(rootFields, "geometryGroups", root)),
                    Features = rootFields.TryGetValue("features", out YamlNode features)
                        ? ParseFeatures(features) : new List<FeatureConfiguration>(),
                    Settings = rootFields.TryGetValue("settings", out YamlNode settings)
                        ? ParseSettings(settings) : new ConversionSettings()
                };
                if (configuration.SchemaVersion != SupportedSchemaVersion)
                    diagnostics.Add(At(ConversionDiagnosticSeverity.Error, "SCHEMA_UNSUPPORTED",
                        $"Schema version {configuration.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.",
                        rootFields["schemaVersion"]));
                return new ConfigurationParseResult(configuration, diagnostics);
            }
            catch (ConfigurationException exception)
            {
                diagnostics.Add(At(ConversionDiagnosticSeverity.Error, "YAML_SCHEMA",
                    exception.Message, exception.Node));
            }
            catch (YamlException exception)
            {
                diagnostics.Add(new ConversionDiagnostic(ConversionDiagnosticSeverity.Error, "YAML_PARSE",
                    exception.Message, checked((int)exception.Start.Line + 1), checked((int)exception.Start.Column + 1)));
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("YAML_PARSE", exception.Message));
            }
            return new ConfigurationParseResult(null, diagnostics);
        }

        public static string Serialize(AvatarGeometryConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var text = new StringBuilder();
            text.AppendLine("schemaVersion: " + configuration.SchemaVersion);
            text.AppendLine("avatar:");
            text.AppendLine("  name: " + Quote(configuration.Avatar?.Name));
            if (!string.IsNullOrEmpty(configuration.Avatar?.Description))
                text.AppendLine("  description: " + Quote(configuration.Avatar.Description));
            List<GeometryGroupConfiguration> groups = configuration.GeometryGroups ?? new List<GeometryGroupConfiguration>();
            text.AppendLine(groups.Count == 0 ? "geometryGroups: []" : "geometryGroups:");
            foreach (GeometryGroupConfiguration group in groups)
            {
                text.AppendLine("  - id: " + Quote(group.Id));
                text.AppendLine("    renderer: " + Quote(group.Renderer));
                text.AppendLine("    materialSlots: " + Inline(group.MaterialSlots));
                text.AppendLine("    uvChannel: " + group.UvChannel);
                text.AppendLine("    tiles: " + Inline(group.Tiles));
                if (!string.IsNullOrEmpty(group.Description))
                    text.AppendLine("    description: " + Quote(group.Description));
            }
            List<FeatureConfiguration> features = configuration.Features ?? new List<FeatureConfiguration>();
            text.AppendLine(features.Count == 0 ? "features: []" : "features:");
            foreach (FeatureConfiguration feature in features)
            {
                text.AppendLine("  - id: " + Quote(feature.Id));
                text.AppendLine("    type: " + (feature.Type == GeometryFeatureType.Toggle ? "toggle" : "exclusive"));
                text.AppendLine("    defaultState: " + Quote(feature.DefaultState));
                text.AppendLine("    ownedGroups: " + InlineQuoted(feature.OwnedGroups));
                List<FeatureStateConfiguration> states = feature.States ?? new List<FeatureStateConfiguration>();
                text.AppendLine(states.Count == 0 ? "    states: []" : "    states:");
                foreach (FeatureStateConfiguration state in states)
                {
                    text.AppendLine("      - id: " + Quote(state.Id));
                    text.AppendLine("        visibleGroups: " + InlineQuoted(state.VisibleGroups));
                }
            }
            ConversionSettings settings = configuration.Settings ?? new ConversionSettings();
            text.AppendLine("settings:");
            text.AppendLine("  outputDirectory: " + Quote(settings.OutputDirectory));
            text.AppendLine("  generatePreview: " + settings.GeneratePreview.ToString().ToLowerInvariant());
            text.AppendLine("  unassignedGeometry: " +
                            (settings.UnassignedGeometry == UnassignedGeometryHandling.Preserve ? "preserve" : "error"));
            text.AppendLine("  triangleBoundaries: " +
                            (settings.TriangleBoundaries == TriangleBoundaryPolicy.Reject
                                ? "reject" : "preserveAsUnassigned"));
            return text.ToString();
        }

        private static AvatarConfiguration ParseAvatar(YamlNode node)
        {
            YamlMappingNode mapping = RequireMapping(node, "avatar");
            Dictionary<string, YamlNode> fields = Fields(mapping, "name", "description");
            return new AvatarConfiguration
            {
                Name = RequiredString(fields, "name", mapping),
                Description = OptionalString(fields, "description")
            };
        }

        private static List<GeometryGroupConfiguration> ParseGroups(YamlNode node)
        {
            YamlSequenceNode sequence = RequireSequence(node, "geometryGroups");
            var result = new List<GeometryGroupConfiguration>();
            foreach (YamlNode item in sequence.Children)
            {
                YamlMappingNode mapping = RequireMapping(item, "geometry group");
                Dictionary<string, YamlNode> fields = Fields(mapping,
                    "id", "renderer", "materialSlots", "uvChannel", "tiles", "description");
                result.Add(new GeometryGroupConfiguration
                {
                    Id = RequiredString(fields, "id", mapping),
                    Renderer = RequiredString(fields, "renderer", mapping),
                    MaterialSlots = IntList(Required(fields, "materialSlots", mapping), "materialSlots"),
                    UvChannel = RequiredInt(fields, "uvChannel", mapping),
                    Tiles = IntList(Required(fields, "tiles", mapping), "tiles"),
                    Description = OptionalString(fields, "description")
                });
            }
            return result;
        }

        private static List<FeatureConfiguration> ParseFeatures(YamlNode node)
        {
            YamlSequenceNode sequence = RequireSequence(node, "features");
            var result = new List<FeatureConfiguration>();
            foreach (YamlNode item in sequence.Children)
            {
                YamlMappingNode mapping = RequireMapping(item, "feature");
                Dictionary<string, YamlNode> fields = Fields(mapping,
                    "id", "type", "defaultState", "ownedGroups", "states");
                string type = RequiredString(fields, "type", mapping);
                GeometryFeatureType parsedType;
                if (string.Equals(type, "toggle", StringComparison.OrdinalIgnoreCase))
                    parsedType = GeometryFeatureType.Toggle;
                else if (string.Equals(type, "exclusive", StringComparison.OrdinalIgnoreCase))
                    parsedType = GeometryFeatureType.Exclusive;
                else
                    throw new ConfigurationException(fields["type"],
                        "Feature type must be 'toggle' or 'exclusive'.");
                result.Add(new FeatureConfiguration
                {
                    Id = RequiredString(fields, "id", mapping),
                    Type = parsedType,
                    DefaultState = RequiredString(fields, "defaultState", mapping),
                    OwnedGroups = StringList(Required(fields, "ownedGroups", mapping), "ownedGroups"),
                    States = ParseStates(Required(fields, "states", mapping))
                });
            }
            return result;
        }

        private static List<FeatureStateConfiguration> ParseStates(YamlNode node)
        {
            YamlSequenceNode sequence = RequireSequence(node, "states");
            var result = new List<FeatureStateConfiguration>();
            foreach (YamlNode item in sequence.Children)
            {
                YamlMappingNode mapping = RequireMapping(item, "feature state");
                Dictionary<string, YamlNode> fields = Fields(mapping, "id", "visibleGroups");
                result.Add(new FeatureStateConfiguration
                {
                    Id = RequiredString(fields, "id", mapping),
                    VisibleGroups = StringList(Required(fields, "visibleGroups", mapping), "visibleGroups")
                });
            }
            return result;
        }

        private static ConversionSettings ParseSettings(YamlNode node)
        {
            YamlMappingNode mapping = RequireMapping(node, "settings");
            Dictionary<string, YamlNode> fields = Fields(mapping,
                "outputDirectory", "generatePreview", "unassignedGeometry", "triangleBoundaries");
            var settings = new ConversionSettings();
            if (fields.TryGetValue("outputDirectory", out YamlNode output)) settings.OutputDirectory = Scalar(output, "outputDirectory");
            if (fields.TryGetValue("generatePreview", out YamlNode preview)) settings.GeneratePreview = Boolean(preview, "generatePreview");
            if (fields.TryGetValue("unassignedGeometry", out YamlNode unassigned))
            {
                string value = Scalar(unassigned, "unassignedGeometry");
                if (string.Equals(value, "preserve", StringComparison.OrdinalIgnoreCase))
                    settings.UnassignedGeometry = UnassignedGeometryHandling.Preserve;
                else if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
                    settings.UnassignedGeometry = UnassignedGeometryHandling.Error;
                else throw new ConfigurationException(unassigned, "unassignedGeometry must be 'preserve' or 'error'.");
            }
            if (fields.TryGetValue("triangleBoundaries", out YamlNode boundaries))
            {
                string value = Scalar(boundaries, "triangleBoundaries");
                if (string.Equals(value, "reject", StringComparison.OrdinalIgnoreCase))
                    settings.TriangleBoundaries = TriangleBoundaryPolicy.Reject;
                else if (string.Equals(value, "preserveAsUnassigned", StringComparison.OrdinalIgnoreCase))
                    settings.TriangleBoundaries = TriangleBoundaryPolicy.PreserveAsUnassigned;
                else throw new ConfigurationException(boundaries,
                    "triangleBoundaries must be 'reject' or 'preserveAsUnassigned'.");
            }
            return settings;
        }

        private static Dictionary<string, YamlNode> Fields(YamlMappingNode mapping, params string[] allowed)
        {
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
            foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
            {
                string key = Scalar(pair.Key, "mapping key");
                if (!allowedSet.Contains(key))
                    throw new ConfigurationException(pair.Key, $"Unknown configuration field '{key}'.");
                if (result.ContainsKey(key))
                    throw new ConfigurationException(pair.Key, $"Duplicate mapping key '{key}'.");
                result.Add(key, pair.Value);
            }
            return result;
        }

        private static YamlNode Required(Dictionary<string, YamlNode> fields, string name, YamlNode owner)
        {
            if (!fields.TryGetValue(name, out YamlNode value))
                throw new ConfigurationException(owner, $"Required field '{name}' is missing.");
            return value;
        }

        private static string RequiredString(Dictionary<string, YamlNode> fields, string name, YamlNode owner) =>
            Scalar(Required(fields, name, owner), name);
        private static string OptionalString(Dictionary<string, YamlNode> fields, string name) =>
            fields.TryGetValue(name, out YamlNode value) ? Scalar(value, name) : null;
        private static int RequiredInt(Dictionary<string, YamlNode> fields, string name, YamlNode owner) =>
            Integer(Required(fields, name, owner), name);

        private static string Scalar(YamlNode node, string context)
        {
            if (!(node is YamlScalarNode scalar) || scalar.Value == null)
                throw new ConfigurationException(node, $"{context} must be a scalar value.");
            return scalar.Value;
        }

        private static int Integer(YamlNode node, string context)
        {
            string value = Scalar(node, context);
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationException(node, $"{context} must be an integer.");
            return parsed;
        }

        private static bool Boolean(YamlNode node, string context)
        {
            string value = Scalar(node, context);
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ConfigurationException(node, $"{context} must be true or false.");
        }

        private static List<int> IntList(YamlNode node, string context)
        {
            YamlSequenceNode sequence = RequireSequence(node, context);
            var result = new List<int>();
            foreach (YamlNode item in sequence.Children) result.Add(Integer(item, context));
            return result;
        }

        private static List<string> StringList(YamlNode node, string context)
        {
            YamlSequenceNode sequence = RequireSequence(node, context);
            var result = new List<string>();
            foreach (YamlNode item in sequence.Children) result.Add(Scalar(item, context));
            return result;
        }

        private static YamlMappingNode RequireMapping(YamlNode node, string context)
        {
            if (node is YamlMappingNode mapping) return mapping;
            throw new ConfigurationException(node, $"{context} must be a mapping.");
        }

        private static YamlSequenceNode RequireSequence(YamlNode node, string context)
        {
            if (node is YamlSequenceNode sequence) return sequence;
            throw new ConfigurationException(node, $"{context} must be a sequence.");
        }

        private static void ValidateSafeNode(YamlNode node)
        {
            if (!node.Anchor.IsEmpty)
                throw new ConfigurationException(node, "YAML anchors and aliases are not supported.");
            if (!node.Tag.IsEmpty && !node.Tag.IsNonSpecific && !IsSupportedCoreTag(node.Tag.Value))
                throw new ConfigurationException(node, $"Unsupported YAML tag '{node.Tag.Value}'.");
            if (node is YamlMappingNode mapping)
                foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
                {
                    ValidateSafeNode(pair.Key);
                    ValidateSafeNode(pair.Value);
                }
            else if (node is YamlSequenceNode sequence)
                foreach (YamlNode child in sequence.Children) ValidateSafeNode(child);
            else if (!(node is YamlScalarNode))
                throw new ConfigurationException(node, $"Unsupported YAML node type '{node.NodeType}'.");
        }

        private static bool IsSupportedCoreTag(string tag) =>
            tag == "tag:yaml.org,2002:str" || tag == "tag:yaml.org,2002:int" ||
            tag == "tag:yaml.org,2002:bool" || tag == "tag:yaml.org,2002:seq" ||
            tag == "tag:yaml.org,2002:map";

        private static ConversionDiagnostic At(ConversionDiagnosticSeverity severity, string code,
            string message, YamlNode node)
        {
            return node == null
                ? new ConversionDiagnostic(severity, code, message)
                : new ConversionDiagnostic(severity, code, message,
                    checked((int)node.Start.Line + 1), checked((int)node.Start.Column + 1));
        }
        private static ConversionDiagnostic Error(string code, string message) =>
            new ConversionDiagnostic(ConversionDiagnosticSeverity.Error, code, message);

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                       .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
        private static string Inline(IEnumerable<int> values) =>
            "[" + string.Join(", ", values ?? Array.Empty<int>()) + "]";
        private static string InlineQuoted(IEnumerable<string> values)
        {
            var quoted = new List<string>();
            foreach (string value in values ?? Array.Empty<string>()) quoted.Add(Quote(value));
            return "[" + string.Join(", ", quoted) + "]";
        }

        private sealed class ConfigurationException : Exception
        {
            internal ConfigurationException(YamlNode node, string message) : base(message) { Node = node; }
            internal YamlNode Node { get; }
        }
    }
}
