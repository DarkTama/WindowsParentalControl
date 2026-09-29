# Parental Control for Windows (DarkTama Edition)

An enhanced screen time, application telemetry, and schedule curfew management system for Windows 10/11 local accounts. Extended from `robertpin/WindowsParentalControl` with 7-day schedule matrixes, unskippable in-session Win32 warning popups, Telegram Bot mobile interactive approvals, an embedded Kestrel Web Admin with ActivityWatch-style visual timelines and TOTP 2FA, a floating desktop countdown widget, and a resilient Session Agent with automated process spawning.

---

## Key Features

### 1. 7-Day Weekly Schedule Matrix & Curfew Clamping
- Configure distinct daily minute quotas and permitted schedule windows for every day of the week (Sunday through Saturday).
- Differentiate between school days (e.g. 60 min, curfew 20:00) and weekends (e.g. 180 min, curfew 23:00) per restricted user.
- Automatic fallback to default limits if a specific weekday schedule is omitted.
- **Curfew Countdown Clamping**: Widget and user portal timers automatically count down to `Math.Min(dailyRemaining, curfewRemaining)`, visually warning users when curfew arrives before daily quota runs out.

### 2. Unskippable In-Session Modal Warnings & Curfew Alerts
- Service dispatches native Win32 `WTSSendMessageW` modal dialogs directly from `LocalSystem` into the user's active session.
- Displays session welcome banner at logon with daily quota and curfew boundaries.
- Automatic countdown warnings at configurable intervals (default: 15m, 5m, 1m) before forced logoff.
- Dedicated **Curfew Warning** dialogs if schedule cutoffs approach before quota is exhausted.
- Cannot be silenced, minimized, or suppressed by Windows Focus Assist / Do Not Disturb.

### 3. Remote Grace Time Requests via Telegram Bot
- Restricted user clicks "Request Screen Time..." in the tray, on the floating widget, or visits `http://localhost:5050/request`.
- Selects extension (+15m, +30m, +1h) and enters an optional justification.
- Configurable daily request quota per user (default: 1 submission per calendar day; customizable or disableable via Admin Settings).
- **Offline-Safe**: Request form verifies internet connectivity and disables submission if the home PC is offline.
- Sends an interactive alert to your private Telegram chat with 1-tap buttons: `[Approve 15m]`, `[Approve 30m]`, `[Decline]`.
- Approval immediately credits bonus minutes to today's usage without altering baseline limits and displays an instant notification on the user's screen.

### 4. Embedded Web Admin & ActivityWatch Visual Telemetry
- Embedded Kestrel web host at `http://0.0.0.0:5050` accessible over LAN or Tailscale VPN.
- Protected by RFC 6238 Authenticator TOTP (Google Authenticator, Microsoft Authenticator, 1Password) with constant-time equality checks and setup QR code generator.
- **ActivityWatch-Style Dashboard**:
  - **24-Hour Visual Timeline**: Interactive hourly distribution bar chart highlighting active gaming/browsing hours.
  - **Live App Pulse**: Real-time foreground app indicator displaying active window titles and process names.
  - **Filters & Date Presets**: Quick filters for all restricted users, today, yesterday, last 7 days, and last 30 days.
  - **Ranked Process Table & Stacked Share Bar**: Visual distribution of app usage time (e.g., `RobloxPlayerBeta.exe`, `chrome.exe`).
- **Remote Session Management**: 1-click bonus grants, emergency force logoffs, or remote session locking with custom warning messages and 15-second countdowns.

### 5. Floating Acrylic Desktop Countdown Widget & Tray Agent
- **Floating Acrylic Countdown Widget**:
  - Smooth, frameless, dark acrylic widget showing live HH:MM:SS countdown, active session indicator, and curfew window.
  - Draggable with position persistence across reboots.
  - Double-click or tray click to toggle visibility.
- **Session Tray Agent (`ParentalControl.Agent.exe`)**:
  - Native WPF message loop running in standard user sessions without dispatcher lag.
  - Samples `GetForegroundWindow()` every 60s and posts telemetry to the service API.
  - Automatic admin bypass (exits immediately if run under an elevated Administrator account).

### 6. Anti-Bypass, Session Tracking & Auto-Heal Architecture
- **Disconnected Session Monitoring**: Captures `WTSDisconnected` sessions across fast user switching and lock screens; service maintains state without accumulating usage during lock.
- **Enforcement on Unlock**: Re-evaluates curfew and daily quota immediately upon unlock (`SessionUnlock`, `ConsoleConnect`, `RemoteConnect`). If expired while locked, forces logoff instantly.
- **LocalSystem Agent Auto-Spawner**: Service uses `WTSQueryUserToken`, `DuplicateTokenEx`, and `CreateProcessAsUserW` targeting `winsta0\default` to spawn or revive `ParentalControl.Agent.exe` on service startup, user unlock, and monitor ticks (recovers automatically even if killed via Task Manager or during upgrades).
- **Non-Destructive Installer Upgrades**: Inno Setup installer stops the service, updates files, and restarts without deleting the service registration, eliminating Windows error 1072 (`ERROR_SERVICE_MARKED_FOR_DELETE`).

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
|  - UsageMonitorWorker (60s tick, curfew warnings, agent watchdog, logoff)     |
|  - SessionTracker (tracks active/disconnected sessions, unlock verification)  |
|  - AgentSpawner (WTSQueryUserToken + CreateProcessAsUserW into winsta0\default)|
|  - TelegramWorker (polls callback queries, grants bonus minutes)              |
|  - WebServerHost (Kestrel :5050 — /request portal, /admin ActivityWatch UI)   |
+------------------------------------+------------------------------------------+
                                     |
                                     | (Shared SQLite via WAL)
                                     v
+-------------------------------------------------------------------------------+
| ParentalControl.Core                                                          |
|  - Database: C:\ProgramData\ParentalControl\data.db (ACL: SYSTEM & Admins)   |
|  - Repositories: Users, Limits, Schedules, Usage, GraceRequests, AppUsage     |
|  - Security: TotpService (RFC 6238 Base32 + QR PNG + constant-time verify)    |
|  - Platform: SessionManager & NativeMethods (WTSSendMessage, WTSLogoff)       |
+------------------------------------+------------------------------------------+
               ^                     ^                     ^
               |                     |                     |
+--------------+-------------+       |       +-------------+--------------+
| ParentalControl.Admin      |       |       | ParentalControl.Agent      |
| (WPF Desktop Application)  |       |       | (Session Tray & Widget)    |
|                            |       |       |                            |
| - Dashboard & Events       |       |       | - Floating Acrylic Widget  |
| - 7-Day Schedule Matrix    |       |       | - Live Tray Countdown      |
| - Quick Grace Buttons      |       |       | - 1-Click Request Launcher |
| - Telegram & 2FA Settings  |       |       | - GetForegroundWindow      |
+----------------------------+       |       |   Telemetry Reporter       |
                                     |       +----------------------------+
                                     v
                 +---------------------------------------+
                 | Restricted User Desktop (winsta0)     |
                 | - Unskippable Modal System Dialogs    |
                 | - Floating HH:MM:SS Countdown Widget  |
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
Run the built-in assert verification suite to test database models, TOTP crypto, 1-per-day grace rules, weekly schedule fallbacks, hourly activity bucketing, and curfew clamping logic:

```powershell
dotnet run --project tests/ParentalControl.Tests
```

Expected output:
```
=== Running ParentalControl Self-Checks ===
✅ TOTP Service & QR Generation Verification Passed.
✅ Settings Repository Verification Passed.
✅ Grace Request Repository & Dynamic Limits Verification Passed.
✅ Usage and Bonus Minutes Verification Passed.
✅ Weekly Schedule & Fallback Verification Passed.
✅ Session Lock & Unlock Events Verification Passed.
✅ User Discovery by Username & Console Session Detection Passed.
✅ Hourly Activity & App Usage Range Verification Passed.
✅ Session Enumeration & Curfew Clamping Calculation Verification Passed.
✅ Cleanup Completed.

🎉 ALL ASSERTIONS PASSED SUCCESSFULLY!
```

---

### 2. Run in Development Mode

#### Step A: Launch the Desktop Admin UI
```powershell
dotnet run --project src/ParentalControl.Admin
```
- Select any standard user (e.g. `Dreitama` or `Dwiatama`).
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

#### Step D: Run the Session Tray Agent & Floating Widget
In a standard user terminal:
```powershell
dotnet run --project src/ParentalControl.Agent
```
- Displays floating acrylic countdown widget on the desktop.
- Shows shield icon in taskbar system tray.
- Hover to view live remaining minutes tooltip; double-click or right-click to toggle widget or open request portal.

#### Step E: Test Browser Endpoints
- **User Request Portal**: Open [http://localhost:5050/request](http://localhost:5050/request)
  - Displays remaining time (clamped to curfew), curfew schedule, and request form.
  - Submitting sends a notification to your Telegram and records a pending request.
- **ActivityWatch Web Admin Dashboard**: Open [http://localhost:5050/admin](http://localhost:5050/admin)
  - Prompts for TOTP code if 2FA is enabled in Settings.
  - View 24-hour visual activity timeline, live app pulse, ranked processes, and grant extra minutes or force logoffs remotely over Tailscale.

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
4. Auto-spawns the Agent into active user sessions on install/reinstall without requiring user logout.
5. Creates Start Menu and Desktop shortcuts for the Admin UI.

---
## Contributing & Issues

Contributions, suggestions, and issue reports are welcome!

- **Bug Reports**: Open a [Bug Report](https://github.com/DarkTama/WindowsParentalControl/issues/new?template=bug_report.yml) with your Windows version, affected component (Service, Admin UI, Agent/Widget, Web Portal, Telegram), reproduction steps, and sanitized logs from `C:\ProgramData\ParentalControl\logs`.
- **Feature Requests**: Open a [Feature Request](https://github.com/DarkTama/WindowsParentalControl/issues/new?template=feature_request.yml) to suggest enhancements or new parental control features.
- **Questions & Ideas**: Join the conversation in [GitHub Discussions](https://github.com/DarkTama/WindowsParentalControl/discussions).

---

## License

This project is licensed under the [MIT License](LICENSE).
