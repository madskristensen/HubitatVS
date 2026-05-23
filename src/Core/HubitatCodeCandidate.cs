using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HubitatVS
{
    internal sealed class HubitatCodeCandidate
    {
        public HubitatCodeCandidate(
            string filePath,
            string displayName,
            string namespaceName,
            string author,
            string version,
            HubitatCodeKind kind,
            IReadOnlyList<string> warnings)
        {
            FilePath = filePath;
            DisplayName = displayName;
            NamespaceName = namespaceName;
            Author = author;
            Version = version;
            Kind = kind;
            Warnings = warnings;
        }

        public string FilePath { get; }

        public string FileName => Path.GetFileName(FilePath);

        public string DisplayName { get; }

        public string NamespaceName { get; }

        public string Author { get; }

        public string Version { get; }

        public HubitatCodeKind Kind { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool IsDriver => Kind == HubitatCodeKind.Driver;

        public bool IsApp => Kind == HubitatCodeKind.App;

        public bool IsLibrary => Kind == HubitatCodeKind.Library;

        public bool IsPublishReady => Kind != HubitatCodeKind.Unknown && !Warnings.Any();

        public string KindDisplayName => Kind switch
        {
            HubitatCodeKind.Driver => "Driver",
            HubitatCodeKind.App => "App",
            HubitatCodeKind.Library => "Library",
            _ => "Unknown"
        };

        public string Status => Kind == HubitatCodeKind.Unknown
            ? "Unknown"
            : IsPublishReady ? $"{KindDisplayName} ready" : $"{KindDisplayName} review";

        public string Notes => Warnings.Count == 0 ? string.Empty : string.Join("; ", Warnings);
    }
}
