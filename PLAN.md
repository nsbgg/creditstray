# CreditsTray – technical plan

## Goal

A portable Windows application that runs in the system tray without administrator rights and makes Codex usage and credits easy to check.

## MVP

1. Tray icon in the Windows notification area.
2. Usage popup with status, last update time, and a link to the official Usage & Billing page.
3. Clean `IUsageProvider` interface as the data-source boundary.
4. No storage of passwords, cookies, or session tokens.
5. Portable self-contained single-file release.

## Architecture

```text
CreditsTray.exe
 ├─ App / TrayIcon       WPF lifecycle and tray menu
 ├─ UsageWindow           compact display
 ├─ IUsageProvider        data-source contract
 ├─ ManualUsageProvider   safe MVP display and official link
 └─ Settings              future refresh and startup settings
```

## Data source

The officially documented Codex Analytics API is not available for Business workspaces. CreditsTray therefore uses a local WebView2 session: sign-in happens in the embedded browser, visible usage-page text is read locally, and the session is stored only in the user profile. No session data is sent to an own server.

## Non-admin operation

- Publish to a user folder, for example `%LOCALAPPDATA%\CreditsTray`.
- Optional startup is configured through the per-user registry area (`HKCU`).
- No Windows service and no machine-wide registry keys.

## Next improvements

1. Official API integration with an explicit authentication model.
2. More resilient DOM parser and refresh interval.
3. Settings page for additional display options.
4. Signed release builds and an optional per-user installer.
5. Tests for the provider, formatting, and error handling.

## MVP acceptance criteria

- Starts without a UAC prompt.
- Tray icon appears and remains active after closing the popup.
- Usage popup is available from the tray menu.
- Button opens the official ChatGPT usage page.
- Build and publish work with .NET 9.
