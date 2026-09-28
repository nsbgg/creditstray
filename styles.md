# CreditsTray – styling guidelines

## Goal

CreditsTray should feel like a calm, native Windows popup: easy to scan, compact, and free of dashboard overload.

## Layout

- Width: approximately 440 px; enough for short values and reset messages.
- Outer padding: 18 px.
- Card grid: two equal-width cards.
- Do not communicate important information through hover or color alone.
- Keep the popup focused on the two usage limits and their reset times.

## Typography

- Title: 20 px, semibold.
- Section title: 16 px, semibold.
- Card labels: 10 px, semibold, uppercase.
- Card values: 17 px, semibold, wrapping allowed.
- Supporting information: 11 px, muted foreground color.
- Always show percentages and reset times as text, not only graphically.
- Remaining-limit progress bars: 100% means fully available, 0% means depleted.
- Pair every progress bar with a percentage value and reset time.

## Color and contrast

- Light mode: dark foreground on a light window background.
- Dark mode: light foreground on a dark window background.
- Cards may be lighter than the background, but never use black text in dark mode.
- Use the accent color only for the status indicator, status label, and primary outline.
- Hover colors must remain within the same light/dark color family.
- Explicitly set foreground colors on all dynamically generated controls.

## Behavior

- The tray popup opens at the bottom right and has no taskbar entry.
- A left tray click toggles the popup.
- “Refresh usage” shows a loading state without moving the cards.
- Missing values are shown as “not detected” or “—”, never as invented values.
