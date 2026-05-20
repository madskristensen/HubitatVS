using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HubitatVS
{
    internal static class HubitatDriverScanner
    {
        private static readonly Regex DefinitionNameRegex = new(@"name\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefinitionNamespaceRegex = new(@"namespace\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefinitionAuthorRegex = new(@"author\s*:\s*['""](?<value>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?im)^\s*(?:\/\/\s*version\s*[:=]|\*\s*@version\s*|version\s*[:=])\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<HubitatDriverCandidate> ScanFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return Array.Empty<HubitatDriverCandidate>();
            }

            return Directory.EnumerateFiles(folderPath, "*.groovy", SearchOption.AllDirectories)
                .Select(AnalyzeFile)
                .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<HubitatDriverCandidate> ScanFiles(IEnumerable<string> filePaths)
        {
            if (filePaths is null)
            {
                return Array.Empty<HubitatDriverCandidate>();
            }

            return filePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(AnalyzeFile)
                .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static HubitatDriverCandidate AnalyzeFile(string filePath)
        {
            var text = File.ReadAllText(filePath);
            var definitionBlock = Regex.Match(text, @"definition\s*\((?<args>.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            var hasDefinition = definitionBlock.Success || text.IndexOf("metadata", StringComparison.OrdinalIgnoreCase) >= 0;

            var displayName = ExtractValue(definitionBlock.Success ? definitionBlock.Groups["args"].Value : text, DefinitionNameRegex)
                ?? Path.GetFileNameWithoutExtension(filePath);
            var namespaceName = ExtractValue(definitionBlock.Success ? definitionBlock.Groups["args"].Value : text, DefinitionNamespaceRegex);
            var author = ExtractValue(definitionBlock.Success ? definitionBlock.Groups["args"].Value : text, DefinitionAuthorRegex);
            var version = ExtractValue(text, VersionRegex) ?? string.Empty;

            var warnings = new List<string>();
            if (!hasDefinition)
            {
                warnings.Add("No Hubitat definition() or metadata block found.");
            }

            if (string.IsNullOrWhiteSpace(displayName) || displayName == Path.GetFileNameWithoutExtension(filePath))
            {
                warnings.Add("Missing driver name.");
            }

            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                warnings.Add("Missing namespace.");
            }

            if (string.IsNullOrWhiteSpace(author))
            {
                warnings.Add("Missing author.");
            }

            return new HubitatDriverCandidate(
                filePath,
                displayName,
                namespaceName ?? string.Empty,
                author ?? string.Empty,
                version,
                hasDefinition,
                warnings);
        }

        private static string ExtractValue(string input, Regex regex)
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
