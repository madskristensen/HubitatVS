using Microsoft.VisualStudio.Workspace;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS.OpenFolder
{
    internal static class HubitatContextTypes
    {
        public const string ContextTypeGuidString = "2C8F4E1A-7B3D-4F9E-C5A2-8D1B6E3F7A4C";
        public static readonly Guid ContextTypeGuid = new Guid(ContextTypeGuidString);

        public const string ProviderTypeGuidString = "8F4E2A1C-3B7D-4F9E-A2C5-1D8B6E3F7A4C";
    }

    [ExportFileContextProvider(
        HubitatContextTypes.ProviderTypeGuidString,
        HubitatContextTypes.ContextTypeGuidString)]
    internal class HubitatFileContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider>
    {
        public IFileContextProvider CreateProvider(IWorkspace workspace)
            => new HubitatFileContextProvider();
    }

    internal class HubitatFileContextProvider : IFileContextProvider
    {
        public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetExtension(filePath), ".groovy", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IReadOnlyCollection<FileContext>>(Array.Empty<FileContext>());

            var context = new FileContext(
                new Guid(HubitatContextTypes.ProviderTypeGuidString),
                HubitatContextTypes.ContextTypeGuid,
                null,
                new[] { filePath },
                string.Empty,
                null);

            return Task.FromResult<IReadOnlyCollection<FileContext>>(new[] { context });
        }
    }
}
