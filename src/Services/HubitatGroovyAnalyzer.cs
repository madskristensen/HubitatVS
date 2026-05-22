using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HubitatVS
{
    internal static class HubitatGroovyAnalyzer
    {
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
            var definitionArgs = TryExtractMetadataDefinitionArguments(text);
            var hasMetadataDefinition = definitionArgs != null;
            if (!hasMetadataDefinition)
            {
                definitionArgs = TryExtractDefinitionArguments(text, 0, text.Length);
            }

            var hasDefinition = definitionArgs != null;
            var kind = DetermineKind(text, hasMetadataDefinition, hasDefinition);
            definitionArgs ??= text;

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
                BuildWarnings(kind, normalizedPath, displayName, namespaceName, author, hasDefinition));
        }

        private static HubitatCodeKind DetermineKind(string source, bool hasMetadataDefinition, bool hasDefinition)
        {
            if (hasMetadataDefinition)
            {
                if (DriverHintRegex.IsMatch(source))
                {
                    return HubitatCodeKind.Driver;
                }

                if (AppHintRegex.IsMatch(source))
                {
                    return HubitatCodeKind.App;
                }

                return HubitatCodeKind.Driver;
            }

            var hasMetadataBlock = Regex.IsMatch(source, @"\bmetadata\s*\{", RegexOptions.IgnoreCase);
            if (hasMetadataBlock && DriverHintRegex.IsMatch(source) && !hasDefinition)
            {
                return HubitatCodeKind.Driver;
            }

            if (hasDefinition)
            {
                return HubitatCodeKind.App;
            }

            if (AppHintRegex.IsMatch(source))
            {
                return HubitatCodeKind.App;
            }

            return HubitatCodeKind.Unknown;
        }

        private static string? TryExtractMetadataDefinitionArguments(string source)
        {
            var searchStart = 0;
            while (TryFindIdentifier(source, "metadata", searchStart, source.Length, out var metadataIndex))
            {
                if (TryFindBlock(source, metadataIndex + "metadata".Length, '{', '}', out var blockStart, out var blockEnd))
                {
                    var args = TryExtractDefinitionArguments(source, blockStart, blockEnd);
                    if (args != null)
                    {
                        return args;
                    }

                    searchStart = blockEnd;
                    continue;
                }

                searchStart = metadataIndex + "metadata".Length;
            }

            return null;
        }

        private static string? TryExtractDefinitionArguments(string source, int startIndex, int endIndex)
        {
            var searchStart = startIndex;
            while (TryFindIdentifier(source, "definition", searchStart, endIndex, out var definitionIndex))
            {
                if (TryFindBlock(source, definitionIndex + "definition".Length, '(', ')', out var argsStart, out var argsEnd))
                {
                    return source.Substring(argsStart, argsEnd - argsStart);
                }

                searchStart = definitionIndex + "definition".Length;
            }

            return null;
        }

        private static bool TryFindIdentifier(string source, string identifier, int startIndex, int endIndex, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(source) || startIndex >= endIndex)
            {
                return false;
            }

            var probe = startIndex;
            while (probe < endIndex)
            {
                var found = source.IndexOf(identifier, probe, endIndex - probe, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                var beforeIsIdentifier = found > 0 && (char.IsLetterOrDigit(source[found - 1]) || source[found - 1] == '_');
                var afterIndex = found + identifier.Length;
                var afterIsIdentifier = afterIndex < source.Length && (char.IsLetterOrDigit(source[afterIndex]) || source[afterIndex] == '_');

                if (!beforeIsIdentifier && !afterIsIdentifier)
                {
                    index = found;
                    return true;
                }

                probe = found + identifier.Length;
            }

            return false;
        }

        private static bool TryFindBlock(string source, int searchFrom, char openChar, char closeChar, out int contentStart, out int contentEnd)
        {
            contentStart = -1;
            contentEnd = -1;

            var index = searchFrom;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index >= source.Length || source[index] != openChar)
            {
                return false;
            }

            var depth = 1;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var escaped = false;
            var start = index + 1;

            for (var i = start; i < source.Length; i++)
            {
                var ch = source[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (inSingleQuote)
                {
                    if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '\'')
                    {
                        inSingleQuote = false;
                    }

                    continue;
                }

                if (inDoubleQuote)
                {
                    if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inDoubleQuote = false;
                    }

                    continue;
                }

                if (ch == '\'')
                {
                    inSingleQuote = true;
                    continue;
                }

                if (ch == '"')
                {
                    inDoubleQuote = true;
                    continue;
                }

                if (ch == openChar)
                {
                    depth++;
                    continue;
                }

                if (ch == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        contentStart = start;
                        contentEnd = i;
                        return true;
                    }
                }
            }

            return false;
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
