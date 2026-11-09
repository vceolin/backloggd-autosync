# Backloggd AutoSync

<p align="center">
  <strong>Automatic daily playtime tracker, diary synchronizer & collection manager for Steam & Nintendo Switch to Backloggd.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white" alt="Windows Platform" />
  <img src="https://img.shields.io/badge/.NET_Framework-4.5%2B-512BD4?logo=dotnet&logoColor=white" alt=".NET Framework" />
  <img src="https://img.shields.io/badge/Dependencies-Zero-success" alt="Zero External Dependencies" />
  <img src="https://img.shields.io/badge/Steam-Real--Time-1b2838?logo=steam&logoColor=white" alt="Steam Tracking" />
  <img src="https://img.shields.io/badge/Nintendo_Switch-Cloud_Sync-E60012?logo=nintendoswitch&logoColor=white" alt="Nintendo Switch Sync" />
  <img src="https://img.shields.io/badge/Backloggd-Diary_%26_Library-ea377a" alt="Backloggd Sync" />
</p>

---

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Quick Start Guide](#quick-start-guide)
  - [1. Backloggd Account Setup](#1-backloggd-account-setup)
  - [2. Steam Playtime Tracking](#2-steam-playtime-tracking)
  - [3. Nintendo Switch & Switch 2 Cloud Sync](#3-nintendo-switch--switch-2-cloud-sync)
  - [4. Collection / Library Entries Sync](#4-collection--library-entries-sync)
- [Application Dashboard & Tabs](#application-dashboard--tabs)
  - [Sessions & Daily Summary](#sessions--daily-summary)
  - [Collection / Library](#collection--library)
  - [Game Mappings & Auto-Matching](#game-mappings--auto-matching)
  - [Nintendo Switch Sync](#nintendo-switch-sync)
  - [Account & Settings](#account--settings)
- [Command-Line Arguments & Startup](#command-line-arguments--startup)
- [Building from Source](#building-from-source)
- [Security & Privacy](#security--privacy)
- [Troubleshooting & FAQ](#troubleshooting--faq)

---

## Overview

[Backloggd](https://backloggd.com) features both a **Journal / Diary** mode (where users track which games they played on each calendar day and how long they played) and a **Library Entries / Collection** system (where users catalog the physical and digital games they own across platforms, storefronts, and editions). However, logging games, durations, platforms, and owned collection items manually can be cumbersome.

**Backloggd AutoSync** is a lightweight, zero-dependency Windows desktop application that runs quietly in your System Tray. It automatically records your gaming sessions, syncs exact daily playtimes into your Backloggd diary, and syncs your owned game collection into your Backloggd Library:

- **Steam (PC)**: Detects active Steam titles in real-time on your PC, accumulates playtime across sessions, splits gameplay across midnight, and pushes synced diary entries when games close—plus discovers your **entire owned Steam library** (both installed and uninstalled games, excluding family-shared titles owned by other accounts) directly from your local Steam client cache.
- **Nintendo Switch & Switch 2**: Connects directly to Nintendo's official cloud APIs to pull daily playtime and historical gameplay recorded on your console—**even when your PC was turned off** or while playing on the go.
- **Collection / Library Sync**: Lets you browse and pre-associate any owned Steam or Switch game with Backloggd even before launching it, configure **Digital vs. Physical**, **Storefront**, **Platform** (`Windows PC`, `Nintendo Switch`, `Nintendo Switch 2`), and **Game Edition**, and sync directly to your Backloggd Library without marking unplayed games as "Played".

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

### 🕹️ Official Nintendo Switch & Switch 2 Cloud Sync
- **Server-Side Tracking (No Parental Controls Required)**: Syncs directly from Nintendo's official **Nintendo Store Game History** cloud API (`com.nintendo.znej`). Every Nintendo Account tracks play history automatically—no Parental Controls app or console restrictions needed.
- **Exact Day-by-Day All-Time Breakdown (2017–Present)**: Queries both recent play activity (`/api/v2.0/users/me/play_histories`) and per-game daily breakdowns (`/api/v2.0/users/me/play_histories/game_titles/{titleId}`) with pagination to import every individual calendar day and exact minutes played across all Switch 1 and Switch 2 titles.
- **Smart Incremental Reconciliation**: Automatically compares each game's `totalPlayedDays` and `totalPlayedMinutes` against local sessions so the first sync imports your entire lifetime day-by-day history and subsequent syncs only fetch games with new activity.
- **Once-a-Day Scheduled Background Sync**: Checks Nintendo's servers once per calendar day in the background and syncs new daily sessions automatically.
- **Non-Game App Filtering**: Automatically ignores media apps (such as YouTube, Hulu, Crunchyroll, Twitch) and Nintendo Classics emulator hubs.

### 📚 Collection / Library Entries Auto-Sync
- **Full Owned Steam Library Discovery (Installed & Uninstalled)**: Reads your local Steam client cache (`userdata/<id>/config/licensecache`, `appcache/packageinfo.vdf`, `appcache/appinfo.vdf`, and `cloud-storage-namespace-1.json`) to discover every game you personally own on Steam—even uninstalled titles—while automatically filtering out non-game tools/DLCs and Steam Family Group games owned by other members.
- **Nintendo Switch & Switch 2 Collection**: Automatically lists all your Nintendo Switch & Switch 2 games and lets you configure each title as **Physical (Cartridge)** or **Digital (Nintendo eShop)**, pick its **Platform** (`Nintendo Switch` vs. `Nintendo Switch 2`), and choose a specific **Game Edition** (fetched live from Backloggd's `/library/options/{game_id}`).
- **Pre-Launch Backloggd Association**: Search and associate any game in your collection with its canonical Backloggd entry **even before you ever launch it**. Your association is saved in `data/mappings.json` so future playtime sessions automatically use that exact match.
- **Pure Collection Sync (Never Marks Unplayed Games as Played)**: Syncs directly to Backloggd's `/library/entry` API (`POST` / `PATCH`) and reconciles with your existing Backloggd Library (`/u/<username>/library`) without setting `Played` or `Backlog` flags on unplayed titles.

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

Backloggd AutoSync connects directly to your Nintendo Account's official **Nintendo Store Game History** (covering all Switch 1 & Switch 2 games from 2017 to present with exact day-by-day breakdowns, with **no Parental Controls setup required**):

1. In Backloggd AutoSync, open the **Nintendo Switch Sync** tab.
2. Click **1. Open Nintendo Account Sign-In**. A browser window will open to the official Nintendo Account sign-in page.
3. Sign in to your Nintendo Account.
4. On the confirmation page, **right-click** the red **"Select this person"** button &rarr; choose **Copy link address**.
5. Paste the copied `npf...://auth#...` link into the **Step 2** text box and click **2. Connect Nintendo Account**.
6. Backloggd AutoSync will immediately fetch and sync your day-by-day play history across all Switch 1 & Switch 2 games, and you can click **🔄 Sync Nintendo Switch Playtime Now** anytime or leave **once-a-day automatic background sync** enabled.

---

### 4. Collection / Library Entries Sync

You can also sync your owned Steam and Nintendo Switch collection into your Backloggd **Library** (`/u/<username>/library`) without marking unplayed games as "Played":

1. Open the **Collection / Library** tab and click **🔍 Scan Owned Games** (runs automatically the first time you open the tab).
2. Backloggd AutoSync discovers all your owned Steam games (both installed and uninstalled) and Nintendo Switch titles, and reconciles them against any entries already in your Backloggd Library.
3. To associate unmapped games with Backloggd:
   - Double-click any game (or select it and click **✏️ Map / Edit Options**) to search Backloggd, pick the exact game, and configure **Ownership** (`Digital`, `Physical`, or `Lost / Sold`), **Platform** (`Windows PC`, `Nintendo Switch`, `Nintendo Switch 2`), **Storefront** (`Steam`, `Nintendo eShop`, etc.), and **Game Edition**.
   - Or click **✨ Auto-Match Unmapped** to automatically match unmapped games in bulk.
   - For Nintendo Switch games, you can also select one or more titles and click **💿/☁ Toggle Physical / Digital** to switch between Physical cartridge and Digital eShop ownership in one click.
4. Click **✓ Sync Selected** or **🔄 Sync All Mapped** to push your collection entries to your Backloggd Library.

---

### Sessions & Daily Summary
- **Live Status Header**: Displays the currently active game and real-time session duration.
- **Session History Table**: Chronological list of recorded sessions showing platform, date, title, duration, and sync status.
- **Sync Pending**: Immediately flushes any un-synced or retried sessions to Backloggd.
- **Pick Game for Session**: Reassign an existing session to a different Backloggd title.
- **Manual Log Entry**: Add arbitrary game sessions (retro games, other platforms) by entering game title, date, duration, and platform.

### Collection / Library
- **Full Owned Catalog**: View all your owned Steam games (installed + uninstalled, with an optional toggle to show games hidden in your Steam client) and Nintendo Switch / Switch 2 titles.
- **Pre-Launch Game Association**: Map any owned game to Backloggd before ever launching it; mappings are shared with the Diary tracker so future sessions sync seamlessly.
- **Ownership, Storefront, Platform & Edition Controls**: Steam games default to `Digital` + `Windows PC` + `Steam`; Nintendo Switch games let you choose `Physical` vs. `Digital (Nintendo eShop)`, `Nintendo Switch` vs. `Nintendo Switch 2`, and specific **Game Editions** loaded dynamically from Backloggd.
- **Filter & Search**: Filter by `All Owned Games`, `Steam Only (All Owned)`, `Steam Installed Only`, `Nintendo Switch Only`, `Unsynced Only`, `Unmapped Only`, or `Synced on Backloggd`, plus instant title/AppID search.

### Game Mappings & Auto-Matching
- **Installed Games Dropdown**: Lists all Steam games found across all Steam library directories on your computer.
- **Auto-Match All Installed**: Scans your entire installed Steam library in one click and queries Backloggd to automatically resolve and cache mappings.
- **Live Search & Autocomplete**: Search Backloggd titles with instant cover/release year suggestions to override or manually link any Steam AppID or Switch Application ID.

### Nintendo Switch Sync
- Connect your Nintendo Account in one step using the Nintendo Store Game History OAuth flow.
- Click **🔄 Sync Nintendo Switch Playtime Now** to sync recent and all-time day-by-day play history across all Switch 1 & Switch 2 games.
- Toggle **Automatic once-a-day background sync**.

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
   git clone https://github.com/vceolin/backloggd-autosync.git
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

Nintendo Switch playtime is recorded on Nintendo's servers, not your PC. You can play your Switch 1 or Switch 2 anywhere in the world. As long as your console connects to Wi-Fi, Nintendo records your daily play history per game. The next time you open Backloggd AutoSync (or when its once-a-day background auto-sync runs), it queries Nintendo's servers and syncs all missed days to your Backloggd diary.
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
