using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HubitatVS
{
    internal sealed class HubitatDriverCandidate
    {
        public HubitatDriverCandidate(
            string filePath,
            string displayName,
            string namespaceName,
            string author,
            string version,
            bool isDriver,
            IReadOnlyList<string> warnings)
        {
            FilePath = filePath;
            DisplayName = displayName;
            NamespaceName = namespaceName;
            Author = author;
            Version = version;
            IsDriver = isDriver;
            Warnings = warnings;
        }

        public string FilePath { get; }

        public string FileName => Path.GetFileName(FilePath);

        public string DisplayName { get; }

        public string NamespaceName { get; }

        public string Author { get; }

        public string Version { get; }

        public bool IsDriver { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool IsPublishReady => IsDriver && !Warnings.Any();

        public string Status => !IsDriver ? "Not a driver" : IsPublishReady ? "Ready" : "Review";

        public string Notes => Warnings.Count == 0 ? "" : string.Join("; ", Warnings);
    }
}
