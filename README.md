# Parental Control for Windows (DarkTama Edition)

An enhanced screen time and schedule curfew management system for Windows 10/11 local accounts. Extended from `robertpin/WindowsParentalControl` with 7-day schedule matrixes, unskippable in-session Win32 warning dialogs, Telegram Bot mobile interactive approvals, an embedded Kestrel Web Admin secured by Authenticator TOTP over LAN/Tailscale, and a lightweight Session Agent for foreground game/app activity logging and tray countdown.

---

## Key Features

### 1. 7-Day Weekly Schedule Matrix
- Configure custom daily minute quotas and permitted schedule windows for every day of the week (Sunday through Saturday).
- Differentiate between school days (e.g. 60 min, curfew 20:00) and weekends (e.g. 180 min, curfew 23:00).
- Automatic fallback to default limits if a specific weekday is omitted.

### 2. Unskippable In-Session Modal Warnings
- Service dispatches native Win32 `WTSSendMessage` modal dialogs directly from `LocalSystem` into the user's active session.
- Displays session welcome banner at logon with daily quota and curfew hours.
- Automatic countdown warnings at 15m, 5m, and 1m remaining before forced logoff.
- Cannot be silenced, hidden, or suppressed by Windows Focus Assist / Do Not Disturb.

### 3. Remote Grace Time Requests via Telegram
- Brother clicks "Request Screen Time..." in the taskbar tray or opens `http://localhost:5050/request`.
- Selects extension (+15m, +30m, +1h) and enters a reason.
- Configurable daily request quota per user (default: 1 submission per calendar day, adjustable or disableable via Admin Settings).
- Offline-safe: request form verifies internet connectivity and disables submission if the home PC is offline.
- Sends an interactive alert to your private Telegram chat with 1-tap buttons: `[Approve 15m]`, `[Approve 30m]`, `[Decline]`.
- Approval automatically credits bonus minutes to today's usage without altering permanent limits and pops an approval notification on the brother's screen.

### 4. Embedded Web Admin & Authenticator 2FA (RFC 6238 TOTP)
- Background service hosts an embedded lightweight Kestrel web server on `http://0.0.0.0:5050` (accessible over local network or Tailscale VPN).
- Protected by Authenticator TOTP (Google Authenticator, Microsoft Authenticator, 1Password) with constant-time equality checks.
- Monitor active sessions, view real-time remaining minutes, trigger manual 1-click bonuses or emergency logoffs from your phone.

### 5. Session Tray Agent & Foreground App Logging
- `ParentalControl.Agent.exe` runs silently in interactive user sessions.
- Taskbar notification tray icon displays a live countdown of remaining screen time.
- Samples `GetForegroundWindow()` every 60s to record active games and apps (e.g., `RobloxPlayerBeta.exe`, `chrome.exe`) and window titles into daily usage logs with 30-day retention.

---

## Architecture

```
                               +-----------------------------+
                               |     Admin Mobile / Laptop   |
                               |    (Tailscale / Telegram)   |
                               +--------------+--------------+
                                              |
                     +------------------------+------------------------+
                     | (Telegram Bot API)                              | (Web Admin :5050 + TOTP)
                     v                                                 v
+-------------------------------------------------------------------------------+
| ParentalControl.Service (Windows Service as LocalSystem, Session 0)           |
|                                                                               |
|  - UsageMonitorWorker (60s tick, schedule curfews, WTSSendMessage warnings)   |
|  - SessionTracker (active session tracking, logon enforcement, clock tamper)  |
|  - TelegramWorker (polls callback queries, grants bonus minutes)              |
|  - WebServerHost (Kestrel :5050 — /request portal & /admin dashboard)        |
+------------------------------------+------------------------------------------+
                                     |
                                     | (Shared SQLite via WAL)
                                     v
+-------------------------------------------------------------------------------+
| ParentalControl.Core                                                          |
|  - Database: C:\ProgramData\ParentalControl\data.db (ACL: SYSTEM & Admins)   |
|  - Repositories: Users, Limits, Schedules, Usage, GraceRequests, AppUsage     |
|  - Security: TotpService (RFC 6238 Base32 + constant-time verification)       |
|  - Platform: SessionManager & NativeMethods (WTSSendMessage, WTSLogoff)       |
+------------------------------------+------------------------------------------+
               ^                     ^                     ^
               |                     |                     |
+--------------+-------------+       |       +-------------+--------------+
| ParentalControl.Admin      |       |       | ParentalControl.Agent      |
| (WPF Desktop Application)  |       |       | (Session Tray Helper)      |
|                            |       |       |                            |
| - Dashboard & Events       |       |       | - Live Tray Countdown      |
| - 7-Day Schedule Matrix    |       |       | - 1-Click Request Launcher |
| - Quick Grace Buttons      |       |       | - GetForegroundWindow      |
| - Telegram & 2FA Settings  |       |       |   Active App Logger        |
+----------------------------+       |       +----------------------------+
                                     |
                                     v
                 +---------------------------------------+
                 | Brother's Interactive Session (User)  |
                 | - Unskippable Modal System Dialogs    |
                 | - Browser Request Portal (/request)   |
                 +---------------------------------------+
```

---

## Prerequisites & System Requirements

### For End-Users / Production Deployment
- **OS**: Windows 10 or Windows 11 (64-bit only).
- **Dependencies**: **None.** The compiled installer (`ParentalControlSetup.exe`) and published `.exe` packages are built with `--self-contained true`. All .NET runtime libraries, ASP.NET Core Kestrel web server, SQLite engine, and WPF components are bundled directly inside the executables.

### For Developers Running from Source
- **OS**: Windows 10 or Windows 11 (x64).
- **.NET SDK**: .NET 8.0 or .NET 9.0 SDK (supports automatic major roll-forward).
- **Inno Setup 6**: Optional, only required to generate the setup installer via `build.ps1`.

---
## How to Test Locally

### 1. Run Automated Verification Tests
Run the built-in assert verification suite to test database models, TOTP crypto, 1-per-day grace rules, and weekly schedule fallbacks:

```powershell
dotnet run --project tests/ParentalControl.Tests
```

Expected output:
```
=== Running ParentalControl Self-Checks ===
✅ TOTP Service Verification Passed.
✅ Settings Repository Verification Passed.
✅ Grace Request Repository Verification Passed.
✅ Usage and Bonus Minutes Verification Passed.
✅ Weekly Schedule & Fallback Verification Passed.
✅ Cleanup Completed.

🎉 ALL ASSERTIONS PASSED SUCCESSFULLY!
```

---

### 2. Run in Development Mode

#### Step A: Launch the Desktop Admin UI
```powershell
dotnet run --project src/ParentalControl.Admin
```
- Click on any standard user (e.g. `Dreitama` or `Dwiatama`).
- Set 7-day limits, test quick grace buttons (`+15 min`, `+30 min`, `+1 hr`), or view logged app activity.
- Open **⚙️ Settings** to configure your Telegram Bot credentials or setup Authenticator 2FA.

#### Step B: Configure Telegram Bot (Mobile Notifications)
1. **Create Bot**: Message `@BotFather` on Telegram, send `/newbot`, choose a name and username, then copy the bot token (format: `1234567890:ABC...`).
2. **Start Conversation**: Search for your bot in Telegram and tap **Start** (or send `/start`). Telegram bots cannot initiate contact with users until the user messages them first.
3. **Get Your Numeric Chat ID**:
   - *Option A (Built-in)*: In the Admin UI **⚙️ Settings**, enter the Bot Token and click **Detect ID**.
   - *Option B*: Message `@userinfobot` or `@RawDataBot` on Telegram to get your numeric User ID (e.g. `123456789`).
   - *Note*: Telegram rejects `@username` for bot DMs. You must use the numeric ID.
4. **Test & Save**: Click **Test Telegram Alert** to verify message delivery to your phone, then click **Save Settings**.

#### Step C: Run the Background Service (Elevated)
Open PowerShell as **Administrator** and run:
```powershell
dotnet run --project src/ParentalControl.Service
```
- Starts monitoring active sessions every 60 seconds.
- Launches the embedded web server on `http://localhost:5050`.
- Connects to Telegram updates loop if configured.

#### Step D: Run the Session Tray Agent
In a standard user terminal:
```powershell
dotnet run --project src/ParentalControl.Agent
```
- Shows shield icon in taskbar system tray.
- Hover to view live remaining minutes tooltip.
- Right click -> "🎮 Request Screen Time..." opens `http://localhost:5050/request`.

#### Step E: Test Browser Endpoints
- **Brother Request Portal**: Open [http://localhost:5050/request](http://localhost:5050/request)
  - Displays remaining time, curfew, and request form.
  - Submitting sends a notification to your Telegram and records a pending request.
- **Web Admin Dashboard**: Open [http://localhost:5050/admin](http://localhost:5050/admin)
  - Prompts for TOTP code if 2FA is enabled in Settings.
  - View active sessions, grant extra minutes, or force logoffs remotely over Tailscale.

---

## Building Standalone Binaries Locally

### Option 1: Publish Self-Contained `.exe` Binaries Directly
Run `dotnet publish` for each component to generate standalone Windows x64 executables that run without requiring the .NET SDK installed:

```powershell
# 1. Publish Service
dotnet publish src/ParentalControl.Service -c Release -r win-x64 --self-contained true -o publish/service

# 2. Publish Admin UI
dotnet publish src/ParentalControl.Admin -c Release -r win-x64 --self-contained true -o publish/admin

# 3. Publish Session Agent
dotnet publish src/ParentalControl.Agent -c Release -r win-x64 --self-contained true -o publish/agent
```

Binaries will be placed in:
- `publish/service/ParentalControl.Service.exe`
- `publish/admin/ParentalControl.Admin.exe`
- `publish/agent/ParentalControl.Agent.exe`

---

### Option 2: Build Complete Inno Setup Installer
If [Inno Setup 6](https://jrsoftware.org/isdownload.php) is installed, run the automated packaging script:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1
```

The script publishes all three components and compiles the final setup executable:
- `installer/Output/ParentalControlSetup.exe`

The installer automatically:
1. Installs files to `C:\Program Files\ParentalControl\`
2. Registers and starts `ParentalControl.Service` as an auto-start Windows Service under `LocalSystem`.
3. Configures `ParentalControl.Agent.exe` in `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` to start at user logon.
4. Creates Start Menu and Desktop shortcuts for the Admin UI.

---

## License

This project is licensed under the [MIT License](LICENSE).
