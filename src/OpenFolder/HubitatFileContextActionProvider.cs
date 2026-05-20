using Microsoft.VisualStudio.Workspace;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS.OpenFolder
{
    [ExportFileContextActionProvider(
        HubitatContextTypes.ProviderTypeGuidString,
        HubitatContextTypes.ContextTypeGuidString)]
    internal class HubitatFileContextActionProviderFactory : IWorkspaceProviderFactory<IFileContextActionProvider>
    {
        public IFileContextActionProvider CreateProvider(IWorkspace workspace)
            => new HubitatFileContextActionProvider();
    }

    internal class HubitatFileContextActionProvider : IFileContextActionProvider
    {
        public Task<IReadOnlyList<IFileContextAction>> GetActionsAsync(
            string filePath,
            FileContext fileContext,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<IFileContextAction> actions = new[] { new HubitatPublishAction(fileContext, filePath) };
            return Task.FromResult(actions);
        }
    }

    internal class HubitatPublishAction : IFileContextAction, IFileContextActionBase
    {
        private readonly string _filePath;

        public HubitatPublishAction(FileContext source, string filePath)
        {
            Source = source;
            _filePath = filePath;
        }

        public string DisplayName => "Publish to Hubitat\u2026";

        public FileContext Source { get; }

        public async Task<IFileContextActionResult> ExecuteAsync(
            IProgress<IFileContextActionProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            await HubitatPublishService.PublishFileAsync(_filePath);
            return new FileContextActionResult(true);
        }
    }
}
