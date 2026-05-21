using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace HubitatVS
{
    internal sealed class HubitatAdornment
    {
        private static readonly Lazy<BitmapImage> _iconSource = new Lazy<BitmapImage>(() =>
        {
            var img = new BitmapImage(new Uri(
                "pack://application:,,,/HubitatVS;component/Resources/Icons/logo.24.24.png",
                UriKind.Absolute));
            img.Freeze();
            return img;
        });

        private readonly IWpfTextView _textView;
        private readonly string _filePath;
        private readonly IAdornmentLayer _layer;
        private readonly ITextDocument _document;
        private readonly Border _element;
        private readonly StackPanel _textStack;

        private readonly HubitatRefreshCoordinator _refreshCoordinator;
        private readonly Dictionary<string, CachedAdornmentInfo> _adornmentInfoCache =
            new Dictionary<string, CachedAdornmentInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, System.Threading.Tasks.Task<HubitatHubInfoEntry>> _adornmentInfoInFlight =
            new Dictionary<string, System.Threading.Tasks.Task<HubitatHubInfoEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheLock = new object();
        private bool _pendingInitialLayoutRefresh = true;
        private bool _hasRenderedContent;

        private static readonly TimeSpan AdornmentInfoCacheTtl = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan HubInfoTimeout = TimeSpan.FromSeconds(4);
        private const int MaxAdornmentCacheEntries = 256;
        private const double RightMargin = 10.0;
        private const double BottomMargin = 10.0;

        public HubitatAdornment(IWpfTextView textView, ITextDocument document)
        {
            _textView = textView;
            _document = document;
            _filePath = document.FilePath;
            _layer = textView.GetAdornmentLayer("HubitatAdornment");

            _textStack = new StackPanel();

            var icon = new Image
            {
                Source = _iconSource.Value,
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var outer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            outer.Children.Add(icon);
            outer.Children.Add(_textStack);

            _element = new Border
            {
                Child = outer,
                CornerRadius = new CornerRadius(5),
                Opacity = 0.96,
                BorderThickness = new Thickness(1),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Visibility = Visibility.Hidden,
            };
            _element.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowContentGridBrushKey);
            _element.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

            _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, null, _element, null);

            _refreshCoordinator = new HubitatRefreshCoordinator(LoadAsync);

            _element.PreviewMouseRightButtonUp += OnAdornmentRightClick;
            _textView.LayoutChanged += OnLayoutChanged;
            _textView.GotAggregateFocus += OnViewGotAggregateFocus;
            _document.FileActionOccurred += OnDocumentFileActionOccurred;
            _textView.Closed += OnViewClosed;
            HubitatConnectionTracker.HubsChanged += OnHubsChanged;

            TriggerRefresh();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_pendingInitialLayoutRefresh)
            {
                _pendingInitialLayoutRefresh = false;
                TriggerRefresh();
            }

            if (_element.Visibility == Visibility.Visible)
            {
                if (!UpdatePosition())
                    _element.Visibility = Visibility.Hidden;
                return;
            }

            if (_hasRenderedContent && UpdatePosition())
            {
                _element.Visibility = Visibility.Visible;
            }
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            _element.PreviewMouseRightButtonUp -= OnAdornmentRightClick;
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.GotAggregateFocus -= OnViewGotAggregateFocus;
            _document.FileActionOccurred -= OnDocumentFileActionOccurred;
            _textView.Closed -= OnViewClosed;
            HubitatConnectionTracker.HubsChanged -= OnHubsChanged;
            _refreshCoordinator.Dispose();
        }

        private void OnHubsChanged(object sender, EventArgs e) => TriggerRefresh(cancelRunning: false);

        private void OnViewGotAggregateFocus(object sender, EventArgs e) => TriggerRefresh(cancelRunning: false);

        private void OnDocumentFileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if ((e.FileActionType & FileActionTypes.ContentSavedToDisk) != 0)
                TriggerRefresh();
        }

        private void OnAdornmentRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (shell == null)
                return;

            shell.UpdateCommandUI(1);

            var screenPoint = _element.PointToScreen(e.GetPosition(_element));
            var points = new[]
            {
                new POINTS { x = (short)screenPoint.X, y = (short)screenPoint.Y }
            };

            var cmdSet = PackageGuids.HubitatVS;
            shell.ShowContextMenu(0, ref cmdSet, PackageIds.HubitatAdornmentContextMenu, points, null);
            e.Handled = true;
        }

        private void TriggerRefresh(bool cancelRunning = true)
            => _refreshCoordinator.RequestRefresh(cancelRunning);

        private bool UpdatePosition()
        {
            _element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = _element.ActualWidth > 0 ? _element.ActualWidth : _element.DesiredSize.Width;
            var height = _element.ActualHeight > 0 ? _element.ActualHeight : _element.DesiredSize.Height;
            if (width <= 0 || height <= 0)
                return false;

            var left = _textView.ViewportLeft + _textView.ViewportWidth - width - RightMargin;
            var top = _textView.ViewportTop + _textView.ViewportHeight - height - BottomMargin;

            left = Math.Max(_textView.ViewportLeft + RightMargin, left);
            top = Math.Max(_textView.ViewportTop + BottomMargin, top);

            if (double.IsNaN(left) || double.IsInfinity(left) || double.IsNaN(top) || double.IsInfinity(top))
                return false;

            Canvas.SetLeft(_element, Math.Round(left, MidpointRounding.AwayFromZero));
            Canvas.SetTop(_element, Math.Round(top, MidpointRounding.AwayFromZero));
            return true;
        }

        private async Task LoadAsync(int refreshVersion, CancellationToken ct)
        {
            try
            {
                if (_textView.IsClosed) return;

                string localSource;
                HubitatCodeCandidate candidate;
                try
                {
                    // Use the current editor buffer so unsaved changes are reflected immediately.
                    localSource = _textView.TextSnapshot.GetText();
                    candidate = HubitatGroovyAnalyzer.AnalyzeSource(localSource, _filePath);
                }
                catch
                {
                    return;
                }

                if (candidate.Kind == HubitatCodeKind.Unknown || string.IsNullOrWhiteSpace(candidate.DisplayName))
                    return;

                var hubs = await HubitatHubSettings.GetHubsCachedAsync();

                if (hubs.Count == 0)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    _element.Visibility = Visibility.Hidden;
                    return;
                }

                await HubitatConnectionTracker.EnsureConnectionsTestedAsync(hubs, ct);
                ct.ThrowIfCancellationRequested();

                var connectedHubs = hubs
                    .Where(h => HubitatConnectionTracker.GetConnected(h.Name) == true)
                    .ToList();

                var hasPendingHubState = hubs.Any(h =>
                    HubitatConnectionTracker.IsTesting(h.Name) || HubitatConnectionTracker.GetConnected(h.Name) == null);

                if (connectedHubs.Count == 0)
                {
                    // Keep the current adornment visible while connection state is still being established.
                    if (hasPendingHubState)
                        return;

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    if (refreshVersion == _refreshCoordinator.CurrentVersion)
                    {
                        _hasRenderedContent = false;
                        _element.Visibility = Visibility.Hidden;
                    }
                    return;
                }

                var sourceFingerprint = StringComparer.Ordinal.GetHashCode(localSource);
                var results = await Task.WhenAll(connectedHubs.Select(hub =>
                    GetAdornmentInfoCachedAsync(hub, candidate, localSource, sourceFingerprint, ct)));

                if (_textView.IsClosed || refreshVersion != _refreshCoordinator.CurrentVersion) return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                if (_textView.IsClosed || refreshVersion != _refreshCoordinator.CurrentVersion)
                    return;

                var hasContent = Render(results, connectedHubs.Count > 1);
                _hasRenderedContent = hasContent;
                _element.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_textView.IsClosed || refreshVersion != _refreshCoordinator.CurrentVersion)
                        return;

                    _element.Visibility = hasContent && UpdatePosition()
                        ? Visibility.Visible
                        : Visibility.Hidden;
                }), DispatcherPriority.Loaded);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ex.Log();
            }
        }

        private bool Render(HubitatHubInfoEntry[] entries, bool showHubName)
        {
            _textStack.Children.Clear();

            if (entries.Length == 0)
                return false;

            _textStack.VerticalAlignment = entries.Length == 1
                ? VerticalAlignment.Center
                : VerticalAlignment.Top;

            foreach (var entry in entries)
            {
                var line = new TextBlock
                {
                    Text = FormatEntry(entry, showHubName),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 420,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                line.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                line.SetResourceReference(TextBlock.FontFamilyProperty, VsFonts.EnvironmentFontFamilyKey);
                line.SetResourceReference(TextBlock.FontSizeProperty, VsFonts.EnvironmentFontSizeKey);

                if (entry.Found && entry.UsedByNames.Count > 0)
                    ToolTipService.SetToolTip(line, string.Join(", ", entry.UsedByNames));

                _textStack.Children.Add(line);
            }

            return true;
        }

        private static string FormatEntry(HubitatHubInfoEntry entry, bool showHubName)
        {
            if (entry.ConnectionError)
                return showHubName ? $"{entry.HubName} · unavailable" : "unavailable";

            if (!entry.Found)
                return showHubName ? $"{entry.HubName} · not published" : "not published";

            var parts = new List<string>();
            if (showHubName) parts.Add(entry.HubName);
            parts.Add($"v{entry.Version}");

            if (entry.InstalledCount > 0)
            {
                var noun = entry.Kind == HubitatCodeKind.App
                    ? (entry.InstalledCount == 1 ? "instance" : "instances")
                    : (entry.InstalledCount == 1 ? "device" : "devices");
                parts.Add($"{entry.InstalledCount} {noun}");
            }

            if (entry.LastModified.HasValue)
                parts.Add(FormatRelativeDate(entry.LastModified.Value));

            parts.Add(entry.IsInSync ? "in sync" : "differs");

            return string.Join(" · ", parts);
        }

        private System.Threading.Tasks.Task<HubitatHubInfoEntry> GetAdornmentInfoCachedAsync(
            HubitatHubConfig hub,
            HubitatCodeCandidate candidate,
            string localSource,
            int sourceFingerprint,
            CancellationToken ct)
        {
            var key = BuildCacheKey(hub, candidate, sourceFingerprint);
            var now = DateTimeOffset.UtcNow;

            lock (_cacheLock)
            {
                PruneAdornmentInfoCache_NoLock(now);

                if (_adornmentInfoCache.TryGetValue(key, out var cached) && now - cached.Timestamp <= AdornmentInfoCacheTtl)
                {
                    return Task.FromResult(cached.Entry);
                }

                if (_adornmentInfoInFlight.TryGetValue(key, out var pending))
                {
                    return pending;
                }

                var work = FetchAdornmentInfoAsync(key, hub, candidate, localSource, ct);
                _adornmentInfoInFlight[key] = work;
                return work;
            }
        }

        private async System.Threading.Tasks.Task<HubitatHubInfoEntry> FetchAdornmentInfoAsync(
            string key,
            HubitatHubConfig hub,
            HubitatCodeCandidate candidate,
            string localSource,
            CancellationToken ct)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(HubInfoTimeout);

                using var client = new HubitatHubClient(hub);
                var entry = await client.GetAdornmentInfoAsync(
                    candidate.Kind, candidate.DisplayName, candidate.NamespaceName, localSource, timeoutCts.Token);

                lock (_cacheLock)
                {
                    _adornmentInfoCache[key] = new CachedAdornmentInfo(entry, DateTimeOffset.UtcNow);
                }

                return entry;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timedOutEntry = CreateUnavailableEntry(hub.Name, candidate.Kind);
                lock (_cacheLock)
                {
                    _adornmentInfoCache[key] = new CachedAdornmentInfo(timedOutEntry, DateTimeOffset.UtcNow);
                }

                return timedOutEntry;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                var entry = CreateUnavailableEntry(hub.Name, candidate.Kind);

                lock (_cacheLock)
                {
                    _adornmentInfoCache[key] = new CachedAdornmentInfo(entry, DateTimeOffset.UtcNow);
                }

                return entry;
            }
            finally
            {
                lock (_cacheLock)
                {
                    _adornmentInfoInFlight.Remove(key);
                }
            }
        }

        private static string BuildCacheKey(HubitatHubConfig hub, HubitatCodeCandidate candidate, int sourceFingerprint)
            => string.Join("|",
                hub.Name,
                hub.Host,
                candidate.Kind,
                candidate.DisplayName,
                candidate.NamespaceName,
                sourceFingerprint.ToString());

        private static HubitatHubInfoEntry CreateUnavailableEntry(string hubName, HubitatCodeKind kind)
            => new HubitatHubInfoEntry
            {
                HubName = hubName,
                Found = false,
                ConnectionError = true,
                Kind = kind
            };

        private void PruneAdornmentInfoCache_NoLock(DateTimeOffset now)
        {
            foreach (var expired in _adornmentInfoCache
                .Where(kvp => now - kvp.Value.Timestamp > AdornmentInfoCacheTtl)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _adornmentInfoCache.Remove(expired);
            }

            if (_adornmentInfoCache.Count <= MaxAdornmentCacheEntries)
                return;

            var overflow = _adornmentInfoCache.Count - MaxAdornmentCacheEntries;
            foreach (var key in _adornmentInfoCache
                .OrderBy(kvp => kvp.Value.Timestamp)
                .Take(overflow)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _adornmentInfoCache.Remove(key);
            }
        }

        private static string FormatRelativeDate(DateTimeOffset date)
        {
            var age = DateTimeOffset.UtcNow - date.ToUniversalTime();
            if (age.TotalDays < 1) return "today";
            if (age.TotalDays < 2) return "yesterday";
            if (age.TotalDays < 7) return $"{(int)age.TotalDays}d ago";
            return date.LocalDateTime.ToString("MMM d");
        }

        private sealed class CachedAdornmentInfo
        {
            public CachedAdornmentInfo(HubitatHubInfoEntry entry, DateTimeOffset timestamp)
            {
                Entry = entry;
                Timestamp = timestamp;
            }

            public HubitatHubInfoEntry Entry { get; }

            public DateTimeOffset Timestamp { get; }
        }
    }
}
