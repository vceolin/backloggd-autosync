# Backloggd AutoSync

<p align="center">
  <strong>Automatic daily playtime tracker & diary synchronizer for Steam & Nintendo Switch to Backloggd.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white" alt="Windows Platform" />
  <img src="https://img.shields.io/badge/.NET_Framework-4.5%2B-512BD4?logo=dotnet&logoColor=white" alt=".NET Framework" />
  <img src="https://img.shields.io/badge/Dependencies-Zero-success" alt="Zero External Dependencies" />
  <img src="https://img.shields.io/badge/Steam-Real--Time-1b2838?logo=steam&logoColor=white" alt="Steam Tracking" />
  <img src="https://img.shields.io/badge/Nintendo_Switch-Cloud_Sync-E60012?logo=nintendoswitch&logoColor=white" alt="Nintendo Switch Sync" />
  <img src="https://img.shields.io/badge/Backloggd-Diary_Journal-ea377a" alt="Backloggd Sync" />
</p>

---

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Quick Start Guide](#quick-start-guide)
  - [1. Backloggd Account Setup](#1-backloggd-account-setup)
  - [2. Steam Playtime Tracking](#2-steam-playtime-tracking)
  - [3. Nintendo Switch & Switch 2 Cloud Sync](#3-nintendo-switch--switch-2-cloud-sync)
- [Application Dashboard & Tabs](#application-dashboard--tabs)
  - [Sessions & Daily Summary](#sessions--daily-summary)
  - [Game Mappings & Auto-Matching](#game-mappings--auto-matching)
  - [Nintendo Switch Sync](#nintendo-switch-sync)
  - [Account & Settings](#account--settings)
- [Command-Line Arguments & Startup](#command-line-arguments--startup)
- [Building from Source](#building-from-source)
- [Project Architecture & Data Storage](#project-architecture--data-storage)
- [Security & Privacy](#security--privacy)
- [Troubleshooting & FAQ](#troubleshooting--faq)

---

## Overview

[Backloggd](https://backloggd.com) features a **Journal / Diary** mode where users can track which games they played on each calendar day and how long they played. However, logging games, durations, and platforms manually after every gaming session can be cumbersome.

**Backloggd AutoSync** is a lightweight, zero-dependency Windows desktop application that runs quietly in your System Tray. It automatically records your gaming sessions and syncs exact daily playtimes directly into your Backloggd diary:

- **Steam (PC)**: Detects active Steam titles in real-time on your PC, accumulates playtime across sessions, splits gameplay across midnight, and pushes synced diary entries when games close.
- **Nintendo Switch & Switch 2**: Connects directly to Nintendo's official cloud APIs to pull daily playtime and historical gameplay recorded on your console—**even when your PC was turned off** or while playing on the go.

All of this happens without overwriting your existing reviews, ratings, notes, or master statuses.

---

## Key Features

### 🎮 Real-Time Steam Tracking
- **Zero-Overhead Detection**: Monitors `HKCU\Software\Valve\Steam\RunningAppID` via lightweight polling. Starts timing instantly when you launch a game and finalizes the session when the process exits.
- **Daily Playtime Accumulation**: Playing the same game multiple times in one day aggregates into a single Backloggd daily journal entry with accurate accumulated hours and minutes.
- **Midnight Session Splitting**: Long gaming sessions crossing 00:00 automatically split into separate calendar day entries with precise duration for each day.
- **Crash-Proof Heartbeats**: Active sessions write checkpoints every 30 seconds to `data/active_session.json`. If your PC restarts or crashes during a game, uncommitted playtime is automatically recovered on next launch.
- **Smart Metadata Resolution**: Matches Steam AppIDs using local Steam installation files (`appmanifest_<AppID>.acf`), Registry keys, or the Steam Store Web API.
- **Junk & Runtime Filtering**: Ignores background utilities such as Wallpaper Engine, SteamVR, and Steam Linux Runtime by default.
- **Minimum Duration Filter**: Configurable threshold (e.g., 60 seconds) prevents accidental misclicks or quick benchmark runs from spamming your diary.

### 🕹️ Official Nintendo Switch Cloud Sync
- **Server-Side Tracking**: Syncs directly from Nintendo’s cloud servers. Play on your Switch docked or handheld anywhere; playtime updates on your PC later.
- **Daily / Monthly Calendar Summaries**: Connects via the official Nintendo Switch Parental Controls API to fetch exact per-game minutes played for any day. *(Note: You can leave parental restriction levels set to "None" so your console is completely unrestricted).*
- **All-Time Play History**: Connects via the Nintendo Store / eShop API to retrieve complete gameplay histories dating back to 2017 (including first-played date, total days active, and total hours).
- **Scheduled Background Sync**: Periodically polls Nintendo’s servers (default: every 30 minutes) and syncs new sessions without requiring manual clicks.
- **Switch 2 Ready**: Compatible with current Nintendo Switch titles and forward-compatible title IDs.

### 📖 Non-Destructive Backloggd Diary Sync
- **Preserves Your Data**: Backloggd AutoSync safely logs playtime and dates under `sync_sessions` without altering your pre-existing star ratings, reviews, played statuses, or custom journal notes.
- **Auto-Suggest Mapping**: Real-time debounced search queries the Backloggd database to easily link games to their canonical Backloggd titles.
- **One-Click Library Matching**: Automatically scans all installed Steam library folders across all your storage drives and maps matching Backloggd IDs in bulk.
- **Manual Session Logger**: Allows manually adding retro, emulated, or non-tracked game sessions to your Backloggd diary for any historical date.
- **Offline Resiliency**: Queues pending sessions locally in `data/sessions.json` if offline or if Backloggd is unreachable, with automated retry handling.

### 🪟 Native Windows Tray App
- **Modern Dark UI**: Clean, responsive interface matching Backloggd's dark theme.
- **Low Memory & CPU Footprint**: Written in pure C# with native Windows API integration.
- **Tray Resident**: Minimizes to the Windows System Tray with dynamic tooltips and optional desktop notifications.
- **Single Instance & IPC**: Launching a second instance automatically brings the existing background application to the foreground.

---

## Quick Start Guide

### 1. Backloggd Account Setup

Backloggd AutoSync communicates with your Backloggd account using your session cookie:

1. Open [backloggd.com](https://backloggd.com) in your web browser and ensure you are signed in.
2. Press <kbd>F12</kbd> (or right-click anywhere and choose **Inspect**) to open Developer Tools.
3. Navigate to the storage tab:
   - **Chrome / Edge / Brave / Opera**: Go to **Application** &rarr; **Storage** &rarr; **Cookies** &rarr; `https://backloggd.com`.
   - **Firefox**: Go to **Storage** &rarr; **Cookies** &rarr; `https://backloggd.com`.
4. Locate the cookie named `_backloggd_session` and copy its entire **Value** string.
5. In **Backloggd AutoSync**, navigate to the **Backloggd Account & Settings** tab:
   - Paste the cookie into the **Backloggd Session Cookie** field.
   - Click **Verify & Connect Session**.
6. The application will validate the cookie, test connectivity, and automatically pull your **Username**, **User ID**, and security **CSRF Token**.

> [!TIP]
> The session cookie will remain valid for several weeks or months unless you explicitly click "Log Out" on the website. If syncs ever report an authentication failure, simply update the cookie in this tab.

---

### 2. Steam Playtime Tracking

Once your Backloggd account is connected, Steam tracking is fully automatic:

1. Launch `BackloggdAutoSync.exe` and let it minimize to the System Tray.
2. Play any Steam game normally.
3. When you exit the game:
   - If the game has already been mapped to a Backloggd title, your session is synced immediately to Backloggd.
   - If the game is new or unmapped, the dashboard will prompt you to confirm or pick the matching Backloggd game title. Once confirmed, the mapping is saved permanently in `data/mappings.json`.

---

### 3. Nintendo Switch & Switch 2 Cloud Sync

Nintendo Switch play history can be imported in two ways:

#### A. Daily / Monthly Tracking (Parental Controls API)
Provides exact daily minutes per game:

1. Link your Switch console to the free **Nintendo Switch Parental Controls** mobile app ([iOS](https://apps.apple.com/app/nintendo-switch-parental-controls/id1190074407) / [Android](https://play.google.com/store/apps/details?id=com.nintendo.znma)).
   > *Tip: In the app, you can set the Restriction Level to **None**. The app will simply act as a daily playtime logger without enforcing any restrictions.*
2. In Backloggd AutoSync, go to the **Nintendo Switch Sync** tab.
3. Click **1a. Open Daily/Monthly Login (Parental App)**. A browser window will open to the official Nintendo login page.
4. Sign in to your Nintendo Account.
5. When presented with the red button that says **"Select this person"** (or your username):
   - **Right-click** the button &rarr; select **Copy link address**.
6. Paste the copied link into the **Step 2** text box in Backloggd AutoSync and click **2. Connect Nintendo Account**.
7. Click **🔄 Fetch & Sync Recent Playtime** to import recent daily play sessions. You can adjust the **Recent days** slider (1–60 days).

#### B. All-Time History Tracking (Nintendo Store API)
Retrieves your full play history from 2017 to present:

1. In the **Nintendo Switch Sync** tab, click **1b. Open Nintendo Store Login (All-Time History)**.
2. Sign in, right-click the red confirmation button &rarr; **Copy link address**, and paste it into the application.
3. Click **📚 Sync Entire Available History (All Months/Years)**.

---

## Application Dashboard & Tabs

```
┌────────────────────────────────────────────────────────────────────────┐
│ Backloggd AutoSync                                            [_][X]   │
│ Live Status: Playing Bastion (00:42:15)  |  Account: skylean [Connected]│
├────────────────────────────────────────────────────────────────────────┤
│ [Sessions & Summary]  [Game Mappings]  [Nintendo Switch]  [Settings]   │
├────────────────────────────────────────────────────────────────────────┤
│ Today's Total: 1h 45m across 2 games                                   │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │ Date       | Platform | Title        | Duration | Synced           │ │
│ │ 2026-10-25 | Steam    | Bastion      | 42m      | [Synced]         │ │
│ │ 2026-10-25 | Switch   | Mario Kart 8 | 1h 03m   | [Synced]         │ │
│ └────────────────────────────────────────────────────────────────────┘ │
│ [🔄 Sync Pending]  [🔍 Pick Game for Session]  [➕ Manual Log]  [❌ Delete] │
└────────────────────────────────────────────────────────────────────────┘
```

### Sessions & Daily Summary
- **Live Status Header**: Displays the currently active game and real-time session duration.
- **Session History Table**: Chronological list of recorded sessions showing platform, date, title, duration, and sync status.
- **Sync Pending**: Immediately flushes any un-synced or retried sessions to Backloggd.
- **Pick Game for Session**: Reassign an existing session to a different Backloggd title.
- **Manual Log Entry**: Add arbitrary game sessions (retro games, other platforms) by entering game title, date, duration, and platform.

### Game Mappings & Auto-Matching
- **Installed Games Dropdown**: Lists all Steam games found across all Steam library directories on your computer.
- **Auto-Match All Installed**: Scans your entire installed Steam library in one click and queries Backloggd to automatically resolve and cache mappings.
- **Live Search & Autocomplete**: Search Backloggd titles with instant cover/release year suggestions to override or manually link any Steam AppID or Switch Application ID.

### Nintendo Switch Sync
- Authenticate Parental Controls and Nintendo Store session tokens.
- Select sync lookback window (1 to 60 days) or run full lifetime library synchronization.
- Toggle **Automatic 30-minute background sync**.

### Account & Settings
- **Backloggd Cookie Verification**: Validate session health and view logged-in username and User ID.
- **Minimum Session Duration**: Threshold in seconds (e.g. 60s or 600s) to ignore brief game launches.
- **Split Sessions at Midnight**: Automatically divides overnight play sessions at 00:00 into distinct calendar days.
- **System Notifications**: Enable or disable Windows tray balloon tip alerts on game start, exit, and sync.
- **Run on Startup**: Toggle auto-launch on Windows boot (adds an entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

---

## Command-Line Arguments & Startup

Backloggd AutoSync supports command-line arguments for system automation and boot scripts:

| Argument | Description |
|---|---|
| *(none)* | Launches the application and opens the main dashboard window. |
| `--minimized` or `-m` | Launches the application silently in the background, minimizing directly to the Windows System Tray. |

### Adding to Windows Startup Manually

You can enable auto-start directly in the **Settings** tab. Alternatively, you can create a shortcut in your Windows Startup directory:

1. Press <kbd>Win</kbd> + <kbd>R</kbd>, type `shell:startup`, and press <kbd>Enter</kbd>.
2. Right-click &rarr; **New** &rarr; **Shortcut**.
3. Point the target to:
   ```cmd
   "C:\Path\To\BackloggdAutoSync.exe" --minimized
   ```
4. Click **Finish**.

---

## Building from Source

Backloggd AutoSync is designed with **zero external dependencies**. It compiles directly using the native C# compiler (`csc.exe`) included with Windows .NET Framework 4.5+:

### Prerequisites
- Windows 10 or Windows 11
- PowerShell 5.1+ (default on Windows)
- .NET Framework 4.5+ (pre-installed on Windows)

### Compilation Steps

1. Clone or download this repository:
   ```powershell
   git clone https://github.com/your-username/backloggd-autosync.git
   cd backloggd-autosync
   ```

2. Run the provided build script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```

3. The script will:
   - Generate multi-resolution icons (`app.ico`) dynamically if not already present.
   - Invoke `csc.exe` with optimizations enabled (`/optimize+`).
   - Produce a standalone, portable binary: `BackloggdAutoSync.exe`.

### Running Unit Tests

Backloggd AutoSync includes a comprehensive, zero-dependency unit test suite covering title matching, cookie normalization, CSRF extraction, Nintendo Switch OAuth & AppID hashing, playtime calculations, and data serialization:

```powershell
powershell -ExecutionPolicy Bypass -File .\test.ps1
```

---

## Project Architecture & Data Storage

```
backloggd-autosync/
│
├── BackloggdAutoSync.exe        # Standalone compiled application binary
├── build.ps1                   # Standalone PowerShell build script (csc.exe)
├── test.ps1                    # Zero-dependency unit test runner script
├── app.ico                     # Embedded application icon
│
├── src/
│   ├── Program.cs              # Application entry point, mutex & single-instance IPC
│   ├── MainForm.cs             # Dark-mode dashboard, Tray icon, UI orchestration
│   ├── SteamTracker.cs         # Real-time Steam polling, active checkpoints, midnight split
│   ├── NintendoSwitchClient.cs # Nintendo OAuth token exchange, Parental Controls & Store API client
│   ├── BackloggdClient.cs      # Backloggd authentication, CSRF handling, search, & session sync
│   │
│   ├── Models/                 # Strongly-typed data models
│   │   ├── AppSettings.cs
│   │   ├── GameMapping.cs
│   │   ├── SessionEntry.cs
│   │   ├── ActiveSessionCheckpoint.cs
│   │   ├── BackloggdModels.cs
│   │   ├── NintendoModels.cs
│   │   └── SteamModels.cs
│   │
│   ├── Services/               # Core services
│   │   ├── SteamLibrary.cs     # Steam directory & manifest (.acf, .vdf) scanner, title resolver
│   │   └── SyncService.cs      # Session sync orchestration & Nintendo Switch reconciliation
│   │
│   ├── Storage/                # Disk persistence
│   │   └── DataStore.cs        # Thread-safe JSON persistence & file logging
│   │
│   ├── UI/                     # UI components & styling
│   │   ├── UITheme.cs          # Dark-mode color palette & custom control factories
│   │   └── Dialogs/
│   │       ├── UnmappedGameDialog.cs # Live auto-suggest dialog for game selection
│   │       └── ManualLogDialog.cs    # Manual session logging dialog
│   │
│   └── Utils/                  # Fast pure helper utilities
│       ├── GameTitleMatcher.cs # Compiled regex title cleaner & fuzzy match selector
│       └── TimeUtils.cs        # Playtime duration formatting & threshold calculators
│
├── tests/                      # Unit test suite
│   ├── TestFramework/          # Lightweight assertion & test discovery engine
│   │   ├── Assert.cs
│   │   └── TestRunner.cs
│   └── Suites/                 # Test suites
│       ├── BackloggdClientTests.cs
│       ├── DataStoreTests.cs
│       ├── GameTitleMatcherTests.cs
│       ├── NintendoSwitchTests.cs
│       ├── SyncLogicTests.cs
│       └── TimeUtilsTests.cs
│
└── data/                       # Local configuration and persistence (auto-generated)
    ├── settings.json           # User settings, session cookies, and encrypted auth tokens
    ├── mappings.json           # Steam AppID & Switch AppID to Backloggd game ID mappings
    ├── sessions.json           # Local history of recorded & pending diary sessions
    ├── active_session.json     # 30-second heartbeat checkpoints for crash safety
    └── activity.log            # Diagnostics, sync timestamps, and error logs
```

---

## Security & Privacy

- **Local Storage Only**: All credentials, tokens, session history, and game mappings are stored exclusively on your local machine inside the `data/` folder.
- **No Third-Party Analytics**: No telemetry, analytics, or external third-party tracking libraries are included.
- **Protecting Your Credentials**:
  - The `data/settings.json` file contains your Backloggd session cookie and Nintendo OAuth tokens.
  - The repository's `.gitignore` file excludes `data/` and `*.log` files by default. **Never commit or share your `data/settings.json` file publicly.**

---

## Troubleshooting & FAQ

<details>
<summary><strong>1. Why does my Backloggd connection show "Invalid Session Cookie"?</strong></summary>

Backloggd cookies can expire after prolonged periods or if you clicked "Log Out" on the website. To refresh your connection:
1. Open backloggd.com, make sure you are signed in, and copy a fresh `_backloggd_session` cookie value from your browser's Developer Tools.
2. In Backloggd AutoSync, paste the updated cookie into **Settings** and click **Verify & Connect Session**.
</details>

<details>
<summary><strong>2. A Steam game didn't log when I closed it. Why?</strong></summary>

- Check the **Minimum Session Duration** setting in the Settings tab. By default, sessions shorter than this duration (e.g. under 1 minute or 10 minutes) are disregarded to avoid logging misclicks.
- Ensure the game process actually exited in Steam (if a background launcher remains running, Steam continues reporting the game as active).
- Check `data/activity.log` for any logging details or error messages.
</details>

<details>
<summary><strong>3. How does Nintendo Switch sync work if my PC is asleep or turned off?</strong></summary>

Nintendo Switch playtime is recorded on Nintendo's servers, not your PC. You can play your Switch anywhere in the world. As long as your console connects to Wi-Fi, Nintendo records your daily summary. The next time you open Backloggd AutoSync (or when its 30-minute background auto-sync runs), it queries Nintendo's servers and syncs all missed days to your Backloggd diary.
</details>

<details>
<summary><strong>4. Will this overwrite my existing Backloggd reviews, ratings, or notes?</strong></summary>

**No.** Backloggd AutoSync sends non-destructive updates targeted specifically to the diary/session timeline (`sync_sessions`). Your star ratings, master/completed checkboxes, review text, and custom notes remain intact.
</details>

<details>
<summary><strong>5. What if a game maps to the wrong Backloggd title?</strong></summary>

You can change or override any mapping at any time:
1. Open the **Game Mappings** tab.
2. Search for the game in the list and click **Delete Mapping**, or type the Steam AppID and use the search bar to find the exact Backloggd game entry you want to link.
3. Click **Save / Override Mapping**.
</details>

---

## License

This project is released under the [MIT License](LICENSE).
