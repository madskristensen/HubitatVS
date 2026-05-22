namespace HubitatVS
{
    internal static class HubitatPublishTracker
    {
        public static event EventHandler<HubitatPublishStateEventArgs>? PublishStateChanged;

        public static void NotifyPublishStarted(string filePath)
            => PublishStateChanged?.Invoke(null, new HubitatPublishStateEventArgs(filePath, HubitatPublishState.Publishing));

        public static void NotifyPublishCompleted(string filePath, bool success)
            => PublishStateChanged?.Invoke(null, new HubitatPublishStateEventArgs(filePath,
                success ? HubitatPublishState.Success : HubitatPublishState.Failed));
    }

    internal enum HubitatPublishState { Publishing, Success, Failed }

    internal sealed class HubitatPublishStateEventArgs : EventArgs
    {
        public HubitatPublishStateEventArgs(string filePath, HubitatPublishState state)
        {
            FilePath = filePath;
            State = state;
        }

        public string FilePath { get; }
        public HubitatPublishState State { get; }
    }
}
