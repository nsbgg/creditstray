namespace CreditsTray;

public sealed record UsageSnapshot(
    string Summary,
    string Details,
    DateTimeOffset UpdatedAt,
    bool IsPlaceholder = false,
    string FiveHourSummary = "not connected",
    string FiveHourReset = "",
    string WeeklySummary = "not detected",
    string WeeklyReset = "See details for reset time",
    string CreditsSummary = "not detected",
    int FiveHourPercent = 0,
    int WeeklyPercent = 0,
    bool IsRapidRefresh = false);

public interface IUsageProvider
{
    UsageSnapshot GetSnapshot();
    void OpenUsagePage();
}

public sealed class ManualUsageProvider : IUsageProvider
{
    private const string UsageUrl = "https://chatgpt.com/settings/usage?tab=overview";

    public UsageSnapshot GetSnapshot() => new(
        "Check usage online",
        "Your Codex usage is securely shown on the official Usage & Billing page.",
        DateTimeOffset.Now,
        IsPlaceholder: true,
        FiveHourSummary: "—",
        FiveHourReset: "Not connected yet",
        WeeklySummary: "—",
        WeeklyReset: "Not connected yet",
        CreditsSummary: "—",
        FiveHourPercent: 0,
        WeeklyPercent: 0);

    public void OpenUsagePage() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = UsageUrl,
            UseShellExecute = true
        });
}
