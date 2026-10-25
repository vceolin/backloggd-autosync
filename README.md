# Backloggd AutoSync — Steam Daily Playtime Tracker

A lightweight, zero-dependency Windows System Tray & Dashboard application that automatically tracks every Steam game you open, records daily playtime, and syncs your sessions directly into your **Backloggd** diary/journal.

## Features

- **Real-Time Steam Game Detection**: Monitors `HKCU\Software\Valve\Steam\RunningAppID` in real time. Automatically starts timing when you launch a Steam game and syncs when you close it.
- **Daily Playtime Accumulation**: If you open the same game multiple times in a single day, your playtime is accumulated onto that day's Backloggd diary entry automatically.
- **Midnight Split Support**: Playing past midnight automatically splits the session across calendar days so each day gets its exact hours played.
- **Crash-Safe Checkpoints**: Saves active session progress every 30 seconds to `data/active_session.json` so playtime is never lost if your PC restarts or crashes mid-game.
- **Smart Game Matching + Manual Overrides**: Automatically resolves Steam AppIDs to game titles (via local Steam `appmanifest_<AppID>.acf` files, Registry, or Steam Store API) and matches them to Backloggd entries. Includes a built-in Backloggd search tab to pre-map or override any game.
- **Preserves Existing Backloggd Log Data**: Keeps your existing game rating, like status, platform, and notes intact while enabling `sync_sessions` and updating total hours/minutes played.

---

## Quick Start

1. **Launch the App**:
   Run `BackloggdAutoSync.exe` in the project root.
2. **Connect Your Backloggd Account**:
   - Open [backloggd.com](https://backloggd.com) in your browser and make sure you are signed in.
   - Press **F12** to open Developer Tools.
   - Go to **Application** (Chrome/Edge/Brave) or **Storage** (Firefox) → **Cookies** → `https://backloggd.com`.
   - Copy the **Value** of the `_backloggd_session` cookie.
   - In **Backloggd AutoSync**, open the **Backloggd Account & Settings** tab, paste the cookie, and click **Verify & Connect**.
   - Your CSRF token, username, and User ID are extracted automatically!
3. **Play Any Steam Game**:
   - Minimize or close the window—it stays quietly in your Windows System Tray.
   - Whenever you launch and close a Steam game, it automatically records the duration and pushes the daily session to your Backloggd diary.

---

## Building from Source

No external SDKs or package managers are required—it compiles using the built-in Windows .NET Framework C# compiler (`csc.exe`):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## Project Structure

- `BackloggdAutoSync.exe` — Compiled standalone Windows application
- `build.ps1` — Build script using the built-in Windows .NET compiler
- `src/Models.cs` — Data models & local JSON storage (`data/settings.json`, `data/sessions.json`, `data/mappings.json`, `data/active_session.json`)
- `src/SteamTracker.cs` — Real-time Steam registry & `appmanifest_*.acf` monitor, crash recovery, and midnight splitter
- `src/BackloggdClient.cs` — Backloggd session authenticator, CSRF extractor, game autocomplete search, and diary/session sync client
- `src/MainForm.cs` — System Tray icon & dark-mode Dashboard UI
- `src/Program.cs` — Single-instance entry point (supports `--minimized` for Windows startup)
