using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using Forms = System.Windows.Forms;

namespace CreditsTray;

public sealed class TrayIcon : IDisposable
{
    private readonly IUsageProvider _provider;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private UsageWindow? _usageWindow;
    private Icon? _percentageIcon;

    public TrayIcon(IUsageProvider provider)
    {
        _provider = provider;
        _statusItem = new Forms.ToolStripMenuItem { Enabled = false };
        _icon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CreditsTray",
            Visible = true,
            ContextMenuStrip = CreateMenu()
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowUsageWindow();
            }
        };
        UpdateStatus();
    }

    private Forms.ContextMenuStrip CreateMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Show usage", null, (_, _) => ShowUsageWindow());
        menu.Items.Add("Open official usage page", null, (_, _) => _provider.OpenUsagePage());
        var startupItem = new Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled,
            Enabled = StartupManager.IsSupported
        };
        startupItem.Click += (_, _) => StartupManager.SetEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void UpdateStatus()
    {
        var snapshot = _provider.GetSnapshot();
        UpdateTrayUsage(snapshot);
    }

    private void ShowUsageWindow()
    {
        _usageWindow ??= CreateUsageWindow();
        if (_usageWindow.IsVisible)
        {
            _usageWindow.Hide();
            return;
        }

        _usageWindow.ShowPopup();
    }

    private UsageWindow CreateUsageWindow()
    {
        var window = new UsageWindow(_provider);
        window.UsageUpdated += (_, snapshot) => UpdateTrayUsage(snapshot);
        return window;
    }

    private void UpdateTrayUsage(UsageSnapshot snapshot)
    {
        _statusItem.Text = $"5h limit: {snapshot.FiveHourSummary}";
        var text = $"5h limit: {snapshot.FiveHourSummary}";
        _icon.Text = text.Length <= 63 ? text : text[..60] + "...";

        var nextIcon = CreatePercentageIcon(snapshot.FiveHourSummary);
        var previousIcon = _percentageIcon;
        _percentageIcon = nextIcon;
        _icon.Icon = nextIcon;
        previousIcon?.Dispose();
    }

    private static Icon CreatePercentageIcon(string summary)
    {
        var match = Regex.Match(summary, @"(?<!\d)(\d{1,3})\s*%");
        var text = match.Success ? match.Groups[1].Value : "—";
        var percent = match.Success && int.TryParse(match.Groups[1].Value, out var parsedPercent)
            ? Math.Clamp(parsedPercent, 0, 100)
            : 0;

        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.Transparent);

        using var font = new System.Drawing.Font("Segoe UI", text.Length > 2 ? 16 : 23, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, brush, (32 - size.Width) / 2, (32 - size.Height) / 2);

        const int barX = 3;
        const int barY = 27;
        const int barWidth = 26;
        const int barHeight = 5;
        using var trackBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
        using var fillBrush = new SolidBrush(Color.FromArgb(0, 120, 215));
        graphics.FillRectangle(trackBrush, barX, barY, barWidth, barHeight);
        graphics.FillRectangle(fillBrush, barX, barY, (int)Math.Round(barWidth * percent / 100d), barHeight);

        var handle = bitmap.GetHicon();
        using var temporaryIcon = Icon.FromHandle(handle);
        var icon = (Icon)temporaryIcon.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _usageWindow?.CloseImmediately();
        _percentageIcon?.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
