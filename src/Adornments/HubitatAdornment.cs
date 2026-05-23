using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace HubitatVS
{
    internal sealed class HubitatAdornment
    {
        private readonly IWpfTextView _textView;
        private readonly string _filePath;
        private readonly IAdornmentLayer _layer;
        private readonly ITextDocument _document;
        private readonly Border _element;
        private readonly StackPanel _textStack;
        private CrispImage _logoIcon;
        private ScaleTransform _iconScale;
        private RotateTransform _iconRotate;
        private CancellationTokenSource _iconAnimationCts;

        private readonly HubitatRefreshCoordinator _refreshCoordinator;
        private readonly Dictionary<string, CachedAdornmentInfo> _adornmentInfoCache =
            new Dictionary<string, CachedAdornmentInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, System.Threading.Tasks.Task<HubitatHubInfoEntry>> _adornmentInfoInFlight =
            new Dictionary<string, System.Threading.Tasks.Task<HubitatHubInfoEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheLock = new object();
        private bool _pendingInitialLayoutRefresh = true;
        private bool _hasRenderedContent;
        private int _publishOnSaveInProgress;

        private static readonly TimeSpan _adornmentInfoCacheTtl = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _hubInfoTimeout = TimeSpan.FromSeconds(4);
        private const int _maxAdornmentCacheEntries = 256;
        private const double _rightMargin = 10.0;
        private const double _bottomMargin = 10.0;

        public HubitatAdornment(IWpfTextView textView, ITextDocument document)
        {
            _textView = textView;
            _document = document;
            _filePath = document.FilePath;
            _layer = textView.GetAdornmentLayer("HubitatAdornment");

            _textStack = new StackPanel();

            _logoIcon = new CrispImage
            {
                Width = 24,
                Height = 24,
                Moniker = RuntimeMonikers.HubitatLogoMoniker,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetIconToolTipIfNeeded(RuntimeMonikers.HubitatLogoMoniker, null);
            _iconScale = new ScaleTransform(1, 1);
            _iconRotate = new RotateTransform(0);
            var iconTransform = new TransformGroup();
            iconTransform.Children.Add(_iconRotate);
            iconTransform.Children.Add(_iconScale);
            _logoIcon.RenderTransform = iconTransform;
            _logoIcon.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

            var outer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            outer.Children.Add(_logoIcon);
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
            HubitatPublishTracker.PublishStateChanged += OnPublishStateChanged;
            RuntimeMonikers.HubitatLogoMonikerUpdated += OnHubitatLogoMonikerUpdated;

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
            HubitatPublishTracker.PublishStateChanged -= OnPublishStateChanged;
            RuntimeMonikers.HubitatLogoMonikerUpdated -= OnHubitatLogoMonikerUpdated;
            _iconAnimationCts?.Cancel();
            _refreshCoordinator.Dispose();
        }

        private void OnHubsChanged(object sender, EventArgs e) => TriggerRefresh(cancelRunning: false);

        private void OnPublishStateChanged(object sender, HubitatPublishStateEventArgs e)
        {
            if (!string.Equals(e.FilePath, _filePath, StringComparison.OrdinalIgnoreCase))
                return;

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_textView.IsClosed) return;

                _iconAnimationCts?.Cancel();
                _iconAnimationCts = new CancellationTokenSource();
                var cts = _iconAnimationCts;

                var isPublishing = e.State == HubitatPublishState.Publishing;
                var newMoniker = isPublishing
                    ? KnownMonikers.Refresh
                    : e.State == HubitatPublishState.Success
                        ? KnownMonikers.StatusOKOutline
                        : KnownMonikers.StatusErrorOutline;
                var iconTooltip = isPublishing
                    ? "Publishing to Hubitat"
                    : e.State == HubitatPublishState.Success
                        ? "Published to Hubitat successfully"
                        : "Publish to Hubitat failed";

                await SwapLogoIconAsync(newMoniker, iconTooltip, cts.Token);

                if (isPublishing)
                {
                    StartIconSpinner();
                    return;
                }

                try { await Task.Delay(2000, cts.Token); }
                catch (OperationCanceledException) { return; }

                await SwapLogoIconAsync(RuntimeMonikers.HubitatLogoMoniker, null, cts.Token);
            });
        }

        private void StartIconSpinner()
        {
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(950)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _iconRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        private async Task SwapLogoIconAsync(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker newMoniker, string? iconToolTip, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_textView.IsClosed) return;

            // Clear any ongoing animation before starting a fresh one
            ResetIconState();

            // Phase 1: fade out + shrink to centre (150 ms)
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd,
            };
            var shrinkX = new DoubleAnimation(1, 0.65, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd,
            };
            var shrinkY = new DoubleAnimation(1, 0.65, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd,
            };

            _logoIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
            _iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);

            try { await Task.Delay(160, ct); }
            catch (OperationCanceledException) { return; }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_textView.IsClosed) return;

            // Swap moniker while invisible; latch local values and release phase-1 hold
            _logoIcon.Moniker = newMoniker;
            SetIconToolTipIfNeeded(newMoniker, iconToolTip);
            _logoIcon.Opacity = 0;
            _iconScale.ScaleX = 0.65;
            _iconScale.ScaleY = 0.65;
            _logoIcon.BeginAnimation(UIElement.OpacityProperty, null);
            _iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            // Phase 2: fade in + grow with a small overshoot bounce (300 ms)
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            var growX = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
            growX.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            growX.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
            growX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            var growY = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
            growY.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            growY.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
            growY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

            _logoIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            _iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
            _iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);

            try { await Task.Delay(310, ct); }
            catch (OperationCanceledException) { return; }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!_textView.IsClosed)
                ResetIconState();
        }

        private void ResetIconState()
        {
            _logoIcon.Opacity = 1;
            _logoIcon.BeginAnimation(UIElement.OpacityProperty, null);
            _iconRotate.Angle = 0;
            _iconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _iconScale.ScaleX = 1;
            _iconScale.ScaleY = 1;
            _iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void OnHubitatLogoMonikerUpdated(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker logoMoniker)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_textView.IsClosed)
                    return;

                // Keep transient publish/failure/success icons until their own flow restores the logo.
                if (ToolTipService.GetToolTip(_logoIcon) != null)
                    return;

                _logoIcon.Moniker = logoMoniker;
                SetIconToolTipIfNeeded(logoMoniker, null);
            });
        }

        private void SetIconToolTipIfNeeded(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker, string? iconToolTip)
        {
            if (RuntimeMonikers.IsHubitatLogoMoniker(moniker))
            {
                ToolTipService.SetToolTip(_logoIcon, null);
                return;
            }

            ToolTipService.SetToolTip(_logoIcon, string.IsNullOrWhiteSpace(iconToolTip) ? "Hubitat status" : iconToolTip);
        }

        private void OnViewGotAggregateFocus(object sender, EventArgs e) => TriggerRefresh(cancelRunning: false);

        private void OnDocumentFileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if ((e.FileActionType & FileActionTypes.ContentSavedToDisk) == 0)
                return;

            TriggerRefresh();
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(PublishOnSaveIfEnabledAsync);
        }

        private async Task PublishOnSaveIfEnabledAsync()
        {
            if (Interlocked.Exchange(ref _publishOnSaveInProgress, 1) == 1)
                return;

            try
            {
                var settings = await HubitatHubSettings.GetLiveInstanceAsync();
                if (!settings.PublishOnSaveEnabled)
                    return;

                await HubitatPublishService.PublishFileIfNotInProgressAsync(_filePath);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _publishOnSaveInProgress, 0);
            }
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

            var left = _textView.ViewportLeft + _textView.ViewportWidth - width - _rightMargin;
            var top = _textView.ViewportTop + _textView.ViewportHeight - height - _bottomMargin;

            left = Math.Max(_textView.ViewportLeft + _rightMargin, left);
            top = Math.Max(_textView.ViewportTop + _bottomMargin, top);

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
                string noun;
                if (entry.Kind == HubitatCodeKind.App)
                    noun = entry.InstalledCount == 1 ? "instance" : "instances";
                else if (entry.Kind == HubitatCodeKind.Library)
                    noun = entry.InstalledCount == 1 ? "consumer" : "consumers";
                else
                    noun = entry.InstalledCount == 1 ? "device" : "devices";

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

                if (_adornmentInfoCache.TryGetValue(key, out var cached) && now - cached.Timestamp <= _adornmentInfoCacheTtl)
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
                timeoutCts.CancelAfter(_hubInfoTimeout);

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
            catch (Exception ex)
            {
                ex.Log();
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
                .Where(kvp => now - kvp.Value.Timestamp > _adornmentInfoCacheTtl)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _adornmentInfoCache.Remove(expired);
            }

            if (_adornmentInfoCache.Count <= _maxAdornmentCacheEntries)
                return;

            var overflow = _adornmentInfoCache.Count - _maxAdornmentCacheEntries;
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
