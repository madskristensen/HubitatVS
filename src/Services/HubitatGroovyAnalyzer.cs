using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HubitatVS
{
    internal static class HubitatGroovyAnalyzer
    {
        private static readonly Regex MetadataDefinitionRegex = new(@"metadata\s*\{[\s\S]*?definition\s*\((?<args>.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefinitionRegex = new(@"definition\s*\((?<args>.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex DefinitionNameRegex = new(@"name\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefinitionNamespaceRegex = new(@"namespace\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefinitionAuthorRegex = new(@"author\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?im)^\s*(?:\/\/\s*version\s*[:=]|\*\s*@version\s*|version\s*[:=])\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DriverHintRegex = new(@"\b(capability|attribute|fingerprint|command)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AppHintRegex = new(@"\b(page|section|mappings)\s*\(|\b(menu|installOnOpen|singleInstance|parent)\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<HubitatCodeCandidate> ScanFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return Array.Empty<HubitatCodeCandidate>();
            }

            return Directory.EnumerateFiles(folderPath, "*.groovy", SearchOption.AllDirectories)
                .Select(AnalyzeFile)
                .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<HubitatCodeCandidate> ScanFiles(IEnumerable<string> filePaths)
        {
            if (filePaths is null)
            {
                return Array.Empty<HubitatCodeCandidate>();
            }

            return filePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(AnalyzeFile)
                .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static HubitatCodeCandidate AnalyzeFile(string filePath)
        {
            var source = File.ReadAllText(filePath);
            return AnalyzeSource(source, filePath);
        }

        public static HubitatCodeCandidate AnalyzeSource(string source, string filePath)
        {
            var text = source ?? string.Empty;
            var normalizedPath = filePath ?? string.Empty;
            var driverDefinition = MetadataDefinitionRegex.Match(text);
            var definition = DefinitionRegex.Match(text);
            var kind = DetermineKind(text, driverDefinition, definition);
            var definitionArgs = driverDefinition.Success
                ? driverDefinition.Groups["args"].Value
                : definition.Success ? definition.Groups["args"].Value : text;

            var displayName = ExtractValue(definitionArgs, DefinitionNameRegex)
                ?? Path.GetFileNameWithoutExtension(normalizedPath);
            var namespaceName = ExtractValue(definitionArgs, DefinitionNamespaceRegex) ?? string.Empty;
            var author = ExtractValue(definitionArgs, DefinitionAuthorRegex) ?? string.Empty;
            var version = ExtractValue(text, VersionRegex) ?? string.Empty;

            return new HubitatCodeCandidate(
                normalizedPath,
                displayName,
                namespaceName,
                author,
                version,
                kind,
                BuildWarnings(kind, normalizedPath, displayName, namespaceName, author, definition.Success || driverDefinition.Success));
        }

        private static HubitatCodeKind DetermineKind(string source, Match driverDefinition, Match definition)
        {
            if (driverDefinition.Success)
            {
                return HubitatCodeKind.Driver;
            }

            var hasMetadata = source.IndexOf("metadata", StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasMetadata && DriverHintRegex.IsMatch(source))
            {
                return HubitatCodeKind.Driver;
            }

            if (definition.Success)
            {
                return HubitatCodeKind.App;
            }

            if (AppHintRegex.IsMatch(source))
            {
                return HubitatCodeKind.App;
            }

            return HubitatCodeKind.Unknown;
        }

        private static IReadOnlyList<string> BuildWarnings(
            HubitatCodeKind kind,
            string filePath,
            string displayName,
            string namespaceName,
            string author,
            bool hasDefinition)
        {
            var warnings = new List<string>();
            if (!hasDefinition)
            {
                warnings.Add("No Hubitat definition() block found.");
            }

            if (kind == HubitatCodeKind.Unknown)
            {
                warnings.Add("Could not determine whether this file is a Hubitat app or driver.");
            }

            if (string.IsNullOrWhiteSpace(displayName) || displayName == Path.GetFileNameWithoutExtension(filePath))
            {
                warnings.Add("Missing name.");
            }

            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                warnings.Add("Missing namespace.");
            }

            if (string.IsNullOrWhiteSpace(author))
            {
                warnings.Add("Missing author.");
            }

            return warnings;
        }

        private static string? ExtractValue(string input, Regex regex)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var match = regex.Match(input);
            return match.Success ? match.Groups["value"].Value.Trim() : null;
        }
    }
}
