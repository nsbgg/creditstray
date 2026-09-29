using System.Windows;
using Microsoft.Win32;
using System.IO;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace CreditsTray;

public partial class UsageWindow : Window
{
    private const string UsageDomScript = """
        (() => {
          const marker = /(usage|billing|credit|allowance|limit|remaining|reset|weekly|monthly|5-hour|5 hours|nutzung|limit|verfügbar|zurücksetzen|wöchentlich|monatlich|5-stunden)/i;
          const selectors = [
            'main', '[role="main"]', 'section', 'article', '[role="region"]',
            'h1', 'h2', 'h3', '[data-testid]', '[aria-label]'
          ];
          const seen = new Set();
          const results = [];

          for (const element of document.querySelectorAll(selectors.join(','))) {
            const text = (element.innerText || element.textContent || '')
              .split(/\r?\n/)
              .map(line => line.replace(/\s+/g, ' ').trim())
              .filter(Boolean)
              .join('\n');
            if (!text || text.length > 2000 || !marker.test(text) || seen.has(text)) {
              continue;
            }
            seen.add(text);
            results.push(text);
          }

          return results.join('\n');
        })()
        """;
    private readonly IUsageProvider _provider;
    private bool _closeImmediately;
    private bool _browserReady;
    private bool _refreshInProgress;
    private bool _backgroundRefresh;
    private int? _lastFiveHourPercent;
    private DateTimeOffset? _lastUsageSampleAt;
    private bool _rapidUsageMode;
    private int _stableRapidFetches;
    private DispatcherTimer? _refreshTimer;
    private const string UsageUrl = "https://chatgpt.com/settings/usage?tab=overview";
    private static readonly TimeSpan NormalRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RapidRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinimumRapidSampleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumNormalSampleInterval = TimeSpan.FromMinutes(4);
    private const double RapidDropRateThreshold = 1;
    private const int RapidDropPercentageThreshold = 5;
    private const int StableRapidFetchThreshold = 5;
    private MediaBrush _buttonBackground = new(MediaColor.FromRgb(58, 58, 58));
    private MediaBrush _buttonHoverBackground = new(MediaColor.FromRgb(75, 75, 75));
    private MediaBrush _buttonForeground = new(MediaColor.FromRgb(245, 245, 245));
    private MediaBrush _closeButtonForeground = new(MediaColor.FromRgb(245, 245, 245));
    private MediaBrush _progressAccent = new(MediaColor.FromRgb(0, 120, 215));
    private MediaBrush _rapidProgressAccent = new(MediaColor.FromRgb(220, 70, 70));
    public event EventHandler<UsageSnapshot>? UsageUpdated;

    public UsageWindow(IUsageProvider provider)
    {
        InitializeComponent();
        _provider = provider;
        var snapshot = provider.GetSnapshot();
        UpdateSnapshotUi(snapshot);
        Closing += OnClosing;
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                PositionBottomRight();
            }
            UpdateProgressWidths();
        };
        OpenUsageButton.MouseEnter += (_, _) => OpenUsageButton.Background = _buttonHoverBackground;
        OpenUsageButton.MouseLeave += (_, _) => OpenUsageButton.Background = _buttonBackground;
        CloseButton.MouseEnter += (_, _) => CloseButton.Foreground = new MediaBrush(MediaColor.FromRgb(0, 120, 215));
        CloseButton.MouseLeave += (_, _) => CloseButton.Foreground = _closeButtonForeground;
        ApplySystemTheme();
    }

    public void ShowPopup()
    {
        ApplySystemTheme();
        UpdateLayout();
        PositionBottomRight();
        Show();
        Activate();
    }

    private void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 16;
        Top = SystemParameters.WorkArea.Bottom - Height - 16;
    }

    public void CloseImmediately()
    {
        _closeImmediately = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closeImmediately)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e) => Hide();

    public async Task RestoreSessionAsync()
    {
        try
        {
            if (!IsVisible)
            {
                Opacity = 0;
                ShowActivated = false;
                Show();
                Hide();
                Opacity = 1;
            }

            AuthBrowser.Visibility = Visibility.Visible;
            InfoPanel.Visibility = Visibility.Collapsed;
            await EnsureBrowserReadyAsync();
            _refreshInProgress = true;
            _backgroundRefresh = true;
            AuthBrowser.CoreWebView2.Navigate(UsageUrl);
        }
        catch
        {
            _refreshInProgress = false;
            _backgroundRefresh = false;
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenUsageButton.IsEnabled = false;
            OpenUsageButton.Content = "Loading usage page …";
            AuthBrowser.Visibility = Visibility.Visible;
            InfoPanel.Visibility = Visibility.Collapsed;
            AuthBrowser.Height = 560;
            ShowPopup();

            await EnsureBrowserReadyAsync();
            AuthBrowser.CoreWebView2.Navigate(UsageUrl);
        }
        catch (Exception ex)
        {
            ShowError($"WebView2 could not be started: {ex.Message}");
        }
        finally
        {
            OpenUsageButton.IsEnabled = true;
        }
    }

    private async Task EnsureBrowserReadyAsync()
    {
        if (_browserReady)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreditsTray", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await AuthBrowser.EnsureCoreWebView2Async(environment);
        AuthBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        AuthBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        AuthBrowser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
        _browserReady = true;
        StartRefreshTimer();
    }

    private async void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var wasBackgroundRefresh = _backgroundRefresh;
        _backgroundRefresh = false;
        _refreshInProgress = false;
        if (!e.IsSuccess || AuthBrowser.CoreWebView2 is null)
        {
            if (!wasBackgroundRefresh)
            {
                ShowError("The usage page could not be loaded.");
            }
            return;
        }

        try
        {
            // The Usage dashboard is a client-rendered page; wait for its content to appear.
            await Task.Delay(2000);
            var pageText = string.Empty;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var json = await AuthBrowser.CoreWebView2.ExecuteScriptAsync(UsageDomScript);
                pageText = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
                if (ContainsUsageText(pageText))
                {
                    break;
                }

                await Task.Delay(1000);
            }
            var lines = pageText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var usefulLines = new List<string>();
            for (var index = 0; index < lines.Length; index++)
            {
                if (!IsLimitLine(lines[index]))
                {
                    continue;
                }

                // Dashboard labels, values and reset times are often rendered
                // as separate sibling lines. Keep the local neighborhood.
                var start = Math.Max(0, index - 1);
                var end = Math.Min(lines.Length, index + 4);
                for (var neighbor = start; neighbor < end; neighbor++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[neighbor]) &&
                        !usefulLines.Contains(lines[neighbor], StringComparer.OrdinalIgnoreCase))
                    {
                        usefulLines.Add(lines[neighbor]);
                    }
                }
            }

            if (pageText.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                pageText.Contains("log in", StringComparison.OrdinalIgnoreCase))
            {
                if (!wasBackgroundRefresh)
                {
                    ShowError("Please sign in to ChatGPT in the embedded window.");
                }
                return;
            }

            var fiveHourIndex = Array.FindIndex(lines, line =>
                line.Contains("5-hour", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("5 hour", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("5-stunden", StringComparison.OrdinalIgnoreCase));
            if (usefulLines.Count == 0)
            {
                if (!wasBackgroundRefresh)
                {
                    ShowError("The usage dashboard was not loaded. Sign in to ChatGPT in the embedded window, then click “Refresh usage”.");
                }
                return;
            }

            var fiveHourSummary = fiveHourIndex >= 0
                ? FindFollowingValue(lines, fiveHourIndex)
                : "not detected";
            var fiveHourReset = fiveHourIndex >= 0
                ? FindFollowingReset(lines, fiveHourIndex)
                : "not detected";
            var weeklyIndex = Array.FindIndex(lines, line =>
                line.Contains("weekly", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("wöchent", StringComparison.OrdinalIgnoreCase));
            var creditsIndex = Array.FindIndex(lines, line =>
                line.Contains("available", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("verfügbar", StringComparison.OrdinalIgnoreCase));
            fiveHourSummary = TranslateUsageText(fiveHourSummary);
            fiveHourReset = TranslateUsageText(fiveHourReset);
            var weeklySummary = TranslateUsageText(
                weeklyIndex >= 0 ? FindFollowingValue(lines, weeklyIndex) : "not detected");
            var weeklyReset = TranslateUsageText(
                weeklyIndex >= 0 ? FindFollowingReset(lines, weeklyIndex) : "not detected");
            var fiveHourPercent = FindPercent(fiveHourSummary);
            var weeklyPercent = FindPercent(weeklySummary);
            UpdateRefreshInterval(fiveHourSummary, fiveHourPercent, DateTimeOffset.Now);
            var snapshot = new UsageSnapshot(
                "Usage and credit limits",
                string.Join(Environment.NewLine,
                    $"5-hour limit: {fiveHourSummary}",
                    $"Reset: {fiveHourReset}",
                    $"Weekly limit: {weeklySummary}",
                    $"Reset: {weeklyReset}"),
                DateTimeOffset.Now,
                FiveHourSummary: fiveHourSummary,
                FiveHourReset: fiveHourReset,
                WeeklySummary: weeklySummary,
                WeeklyReset: weeklyReset,
                CreditsSummary: creditsIndex >= 0
                    ? TranslateUsageText(FindFollowingValue(lines, creditsIndex))
                    : "not detected",
                FiveHourPercent: fiveHourPercent,
                WeeklyPercent: weeklyPercent,
                IsRapidRefresh: _rapidUsageMode);
            UpdateSnapshotUi(snapshot);
            UsageUpdated?.Invoke(this, snapshot);
            AuthBrowser.Visibility = Visibility.Collapsed;
            AuthBrowser.Height = double.NaN;
            InfoPanel.Visibility = Visibility.Visible;
            OpenUsageButton.Content = "Refresh usage";
            if (!wasBackgroundRefresh)
            {
                ShowPopup();
            }
        }
        catch (Exception ex)
        {
            if (!wasBackgroundRefresh)
            {
                ShowError($"Usage could not be read: {ex.Message}");
            }
        }
    }

    private void StartRefreshTimer()
    {
        if (_refreshTimer is not null)
        {
            return;
        }

        _refreshTimer = new DispatcherTimer { Interval = NormalRefreshInterval };
        _refreshTimer.Tick += (_, _) =>
        {
            if (!_refreshInProgress && AuthBrowser.CoreWebView2 is not null)
            {
                _refreshInProgress = true;
                _backgroundRefresh = true;
                AuthBrowser.CoreWebView2.Navigate(UsageUrl);
            }
        };
        _refreshTimer.Start();
    }

    private void UpdateRefreshInterval(string summary, int currentPercent, DateTimeOffset sampleAt)
    {
        var hasPercent = Regex.IsMatch(summary, @"(?<!\d)\d{1,3}\s*%");
        if (hasPercent && _lastFiveHourPercent.HasValue && _lastUsageSampleAt.HasValue)
        {
            var sampleInterval = sampleAt - _lastUsageSampleAt.Value;
            var percentDrop = _lastFiveHourPercent.Value - currentPercent;
            if (_rapidUsageMode)
            {
                var dropRatePerMinute = sampleInterval.TotalMinutes > 0
                    ? percentDrop / sampleInterval.TotalMinutes
                    : double.MaxValue;
                    if (sampleInterval >= MinimumRapidSampleInterval && dropRatePerMinute < RapidDropRateThreshold)
                    {
                        _stableRapidFetches++;
                        if (_stableRapidFetches >= StableRapidFetchThreshold)
                        {
                            _rapidUsageMode = false;
                            _stableRapidFetches = 0;
                            SetRefreshInterval(NormalRefreshInterval);
                            UpdateProgressColor();
                        }
                }
                else
                {
                    _stableRapidFetches = 0;
                }
            }
            else if (sampleInterval >= MinimumNormalSampleInterval && percentDrop > RapidDropPercentageThreshold)
            {
                _rapidUsageMode = true;
                _stableRapidFetches = 0;
                SetRefreshInterval(RapidRefreshInterval);
                UpdateProgressColor();
            }
        }

        if (hasPercent)
        {
            _lastFiveHourPercent = currentPercent;
            _lastUsageSampleAt = sampleAt;
        }
    }

    private void SetRefreshInterval(TimeSpan interval)
    {
        if (_refreshTimer is not null)
        {
            _refreshTimer.Interval = interval;
        }
    }

    private void ShowError(string message)
    {
        AuthBrowser.Visibility = Visibility.Visible;
        InfoPanel.Visibility = Visibility.Visible;
        SummaryText.Text = message;
        UpdatedText.Text = "No data read";
        AuthBrowser.Height = 560;
        ShowPopup();
    }

    private void ApplySystemTheme()
    {
        var useLightTheme = true;
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
        {
            useLightTheme = lightTheme != 0;
        }

        var background = useLightTheme ? MediaColor.FromRgb(255, 255, 255) : MediaColor.FromRgb(32, 32, 32);
        var foreground = useLightTheme ? MediaColor.FromRgb(32, 32, 32) : MediaColor.FromRgb(245, 245, 245);
        var muted = useLightTheme ? MediaColor.FromRgb(90, 90, 90) : MediaColor.FromRgb(190, 190, 190);
        var border = useLightTheme ? MediaColor.FromRgb(215, 220, 226) : MediaColor.FromRgb(70, 70, 70);
        var control = useLightTheme ? MediaColor.FromRgb(245, 245, 245) : MediaColor.FromRgb(58, 58, 58);
        var hoverControl = useLightTheme ? MediaColor.FromRgb(232, 236, 241) : MediaColor.FromRgb(68, 68, 68);
        var accent = System.Windows.SystemColors.HighlightColor;
        _progressAccent = new MediaBrush(accent);
        _rapidProgressAccent = new MediaBrush(MediaColor.FromRgb(220, 70, 70));

        _buttonBackground = new MediaBrush(control);
        _buttonHoverBackground = new MediaBrush(hoverControl);
        _buttonForeground = new MediaBrush(foreground);
        _closeButtonForeground = new MediaBrush(foreground);

        RootBorder.Background = new MediaBrush(background);
        RootBorder.BorderBrush = new MediaBrush(border);
        TitleText.Foreground = new MediaBrush(foreground);
        SummaryText.Foreground = new MediaBrush(foreground);
        CloseButton.Foreground = new MediaBrush(foreground);
        FiveHourLabel.Foreground = new MediaBrush(foreground);
        FiveHourValueText.Foreground = new MediaBrush(foreground);
        FiveHourResetText.Foreground = new MediaBrush(muted);
        WeeklyLabel.Foreground = new MediaBrush(foreground);
        WeeklyValueText.Foreground = new MediaBrush(foreground);
        WeeklyResetText.Foreground = new MediaBrush(muted);
        UpdatedText.Foreground = new MediaBrush(muted);
        OpenUsageButton.Background = _buttonBackground;
        OpenUsageButton.Foreground = _buttonForeground;
        OpenUsageButton.BorderBrush = new MediaBrush(accent);
        StatusText.Foreground = new MediaBrush(accent);
        FiveHourCard.Background = new MediaBrush(control);
        WeeklyCard.Background = new MediaBrush(control);
        UpdateProgressColor();
        FiveHourProgressTrack.Background = new MediaBrush(border);
        WeeklyProgressTrack.Background = new MediaBrush(border);
    }

    private void UpdateSnapshotUi(UsageSnapshot snapshot)
    {
        SummaryText.Text = snapshot.Summary;
        FiveHourValueText.Text = snapshot.FiveHourSummary;
        FiveHourResetText.Text = snapshot.FiveHourReset;
        WeeklyValueText.Text = snapshot.WeeklySummary;
        WeeklyResetText.Text = snapshot.WeeklyReset;
        UpdateProgressWidths();
        UpdatedText.Text = $"Last updated: {snapshot.UpdatedAt:HH:mm:ss}";
    }

    private void UpdateProgressColor()
    {
        var progressBrush = _rapidUsageMode ? _rapidProgressAccent : _progressAccent;
        FiveHourProgressFill.Background = progressBrush;
        WeeklyProgressFill.Background = progressBrush;
    }

    private static string FindFollowingValue(string[] lines, int labelIndex)
    {
        for (var index = labelIndex + 1; index < Math.Min(lines.Length, labelIndex + 5); index++)
        {
            var line = lines[index];
            if ((line.Any(char.IsDigit) && !IsResetLine(line)) || line.Contains("remaining", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("übrig", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("verbleib", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return "not detected";
    }

    private static bool IsResetLine(string line) =>
        line.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("zurück", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("wieder", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("in ", StringComparison.OrdinalIgnoreCase);

    private static int FindPercent(string text)
    {
        var match = Regex.Match(text, @"(?<!\d)(\d{1,3})\s*%");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

    private static string TranslateUsageText(string text)
    {
        var translated = text
            .Replace("5-Stunden-Limit", "5-hour limit", StringComparison.OrdinalIgnoreCase)
            .Replace("Wochenlimit", "weekly limit", StringComparison.OrdinalIgnoreCase)
            .Replace("wöchentlich", "weekly", StringComparison.OrdinalIgnoreCase)
            .Replace("monatlich", "monthly", StringComparison.OrdinalIgnoreCase)
            .Replace("verfügbar", "available", StringComparison.OrdinalIgnoreCase)
            .Replace("übrig", "remaining", StringComparison.OrdinalIgnoreCase)
            .Replace("nicht erkannt", "not detected", StringComparison.OrdinalIgnoreCase)
            .Replace("nicht verbunden", "not connected", StringComparison.OrdinalIgnoreCase)
            .Replace("zurücksetzen", "reset", StringComparison.OrdinalIgnoreCase)
            .Replace("Std.", "h", StringComparison.OrdinalIgnoreCase)
            .Replace("Min.", "m", StringComparison.OrdinalIgnoreCase);
        translated = Regex.Replace(translated, @"(?i)\bwird\s+in\s+", "Resets in ");
        translated = Regex.Replace(translated, @"(?i)\bzurückgesetzt\b", string.Empty);
        return Regex.Replace(translated, @"\s+", " ").Trim();
    }

    private void UpdateProgressWidths()
    {
        if (FiveHourProgressTrack.ActualWidth > 0)
        {
            FiveHourProgressFill.Width = FiveHourProgressTrack.ActualWidth * FindPercent(FiveHourValueText.Text) / 100d;
        }

        if (WeeklyProgressTrack.ActualWidth > 0)
        {
            WeeklyProgressFill.Width = WeeklyProgressTrack.ActualWidth * FindPercent(WeeklyValueText.Text) / 100d;
        }
    }

    private static string FindFollowingReset(string[] lines, int labelIndex)
    {
        for (var index = labelIndex + 1; index < Math.Min(lines.Length, labelIndex + 5); index++)
        {
            var line = lines[index];
            if (line.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("zurück", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("wieder", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return "See details for reset time";
    }

    private static bool ContainsUsageText(string text) =>
        text.Contains("monthly usage", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("weekly usage", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("usage & billing", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("credit balance", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("remaining allowance", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("5-hour", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("5 hours", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("nutzungslimit", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("monatliche nutzung", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("wöchentliche nutzung", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("5-stunden", StringComparison.OrdinalIgnoreCase);

    private static bool IsLimitLine(string line) =>
        line.Contains("monthly usage", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("weekly usage", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("usage & billing", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("credit balance", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("remaining allowance", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("5-hour", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("5 hours", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("nutzungslimit", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("monatliche nutzung", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("wöchentliche nutzung", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("5-stunden", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("zurücksetzen", StringComparison.OrdinalIgnoreCase);
}
