# Parental Control for Windows (DarkTama Edition)

An enhanced screen time, application telemetry, and schedule curfew management system for Windows 10/11 local accounts. Extended from `robertpin/WindowsParentalControl` with 7-day schedule matrixes, unskippable in-session Win32 warning popups, Telegram Bot mobile interactive approvals, an embedded Kestrel Web Admin with ActivityWatch-style visual timelines and TOTP 2FA, a floating desktop countdown widget, and a resilient Session Agent with automated process spawning.

---

## Key Features

### 1. 7-Day Weekly Schedule Matrix, Sparse Overrides & Curfew Clamping
- **Baseline Limits & Sparse Overrides**: Default quotas and curfew windows are defined once per user. Specific weekdays only store overrides when explicitly marked as custom (`IsCustom = true`), keeping database footprint lean and clean.
- **Quick Presets**: 1-click presets for **School Days (Mon–Fri)**, **Weekend (Sat–Sun)**, or **Reset to Baseline** available in both WPF Admin and Web Admin.
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
- Sends an interactive alert to your private Telegram chat with dynamic 1-tap buttons scaling to the requested minutes (e.g. `[Approve 60m]`, `[Approve 30m]`, `[Approve 15m]`, `[Decline]`).
- **Interactive Decline Feedback Loop**: Declining offers instant 1-tap presets (`[⏱️ Belum Waktunya]`, `[📚 Selesaikan PR Dulu]`, `[🌙 Sudah Malam]`) or `[✏️ Alasan Lain]` opening a 3-minute text reply window. The decline reason is immediately dispatched to the child's desktop as an in-session toast notification and pinned on their `/request` portal.
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
  - **Adaptive Fullscreen & Multi-Monitor Docking**: Automatically detects foreground fullscreen games and applications, docking cleanly to configurable corner presets (`TopRight`, `TopLeft`, `BottomRight`, `BottomLeft`) or collapsing to a mini pill mode.
  - Configurable monitor targeting and interactive visual corner preview in Settings, persisted per-user in `%LOCALAPPDATA%\ParentalControl\widget.json` without requiring administrator elevation.
- **Session Tray Agent (`ParentalControl.Agent.exe`)**:
  - Native WPF message loop running in standard user sessions without dispatcher lag.
  - Samples `GetForegroundWindow()` every 60s and posts telemetry to the service API.
  - Automatic admin bypass (exits immediately if run under an elevated Administrator account).

### 6. Anti-Bypass, Session Tracking & Auto-Heal Architecture
- **Non-Destructive Enforced Workstation Lock**: When daily quotas or curfew limits expire, the service enforces a workstation lock (`WTSDisconnectSession`) rather than a destructive logoff (`WTSLogoffSession`), preserving open documents, school work, and game state safely in memory.
- **Enforcement on Unlock**: Re-evaluates curfew and daily quota immediately upon unlock (`SessionUnlock`, `ConsoleConnect`, `RemoteConnect`). If expired while locked, displays an unskippable 5-second modal warning and immediately re-locks the desktop.
- **Monotonic Clock Tampering Watchdog**: `UsageMonitorWorker` continuously compares monotonic CPU uptime (`Environment.TickCount64`) against wall-clock time (`DateTime.UtcNow`). Detected clock rollbacks or advances > 60s trigger security audit events, instant session lock, and Telegram alerts.
- **Disconnected Session Monitoring**: Captures `WTSDisconnected` sessions across fast user switching and lock screens; service maintains state without accumulating usage during lock.
- **LocalSystem Agent Auto-Spawner**: Service uses `WTSQueryUserToken`, `DuplicateTokenEx`, and `CreateProcessAsUserW` targeting `winsta0\default` to spawn or revive `ParentalControl.Agent.exe` on service startup, user unlock, and monitor ticks (recovers automatically even if killed via Task Manager or during upgrades).
- **Non-Destructive Installer Upgrades**: Inno Setup installer stops the service, updates files, and restarts without deleting the service registration, eliminating Windows error 1072 (`ERROR_SERVICE_MARKED_FOR_DELETE`).

### 7. Silent Screen Capture & Web Admin Watch Mode
- **Completely Silent GDI Capture**: The session agent captures the full virtual desktop (`SystemInformation.VirtualScreen`) covering all monitors in-memory using native GDI without triggering camera sounds, system toasts, clipboard changes, or window flashes.
- **Single-Shot & 10s Watch Mode**: Admins can capture a single snapshot on-demand or toggle 10-second Watch Mode with live viewport preview, thumbnail strip of recent captures, and full-resolution click-to-zoom lightbox modal.
- **Guarded Execution**: Rejects capture requests if the user session is locked or disconnected, avoiding blank/black screen storage.
- **Mobile Telegram Integration**: Capture desktop on-the-go via `/capture [username]`, check real-time status with `/status`, or send any Web Admin capture directly to Telegram with 1 click.
- **Dynamic Telegram Approval Buttons**: Grace request approval keyboards dynamically scale to match requested minutes (e.g., offering `[Approve 60m]`, `[Approve 30m]`, `[Approve 15m]`, `[Decline]`).
- **Storage Auto-Pruning**: JPEGs downscaled to 1920px max width (quality 70, ~150–250 KB) stored in `%ProgramData%\ParentalControl\captures\` with automatic rolling pruning (7-day retention + 500 MB hard storage cap).


### 8. Transparent User Portal ("Jadwal Main") & In-Place GitHub Updater
- **"Jadwal Main" Segmented Day Strip**: Children visiting `http://localhost:5050/request` see a modern 7-day segmented strip with active day indicators, remaining quota, curfew windows, and pinned rejection notices so expectations are clear without exposing admin settings.
- **Touch-Responsive Mobile UI**: Web Admin and User Portal feature touch-friendly horizontal table scrolling and responsive card layouts tailored for phones and tablets.
- **Built-in GitHub Releases Updater**: Desktop Admin and Web Admin check `DarkTama/WindowsParentalControl` for new releases, display changelogs, download `ParentalControlSetup.exe`, and perform automated silent upgrades with service restart.

### 9. Single-Day Schedule Exceptions & Advance Change Requests
- **Self-Contained Single-Day Overrides**: Grant temporary quota or curfew exceptions for holidays, sick days, or exam weeks without modifying baseline 7-day schedules.
- **Independent Schema**: Stored in `schedule_exceptions` with fully copied boundary fields to prevent baseline schedule drift.
- **Advance Requests by Children**: Children can submit advance schedule change requests directly from the `/request` portal.
- **Full Management**: Add, review, and delete exceptions via both WPF Desktop Admin and Kestrel Web Admin.

### 10. Interactive Session Prompts & Multi-Monitor Desktop Delivery
- **Bi-Directional Interactive Communication**: Administrators can dispatch messages into active desktop sessions from both Web Admin and Telegram bot (`/ask [user] [message]`).
- **Targeted Multi-Monitor Presentation**: Dialog can target Monitor 1, Monitor 2, or automatically follow the user's active foreground application display, automatically clamping to the primary screen if disconnected.
- **Urgency Levels & Audible Alerts**: Messages support `Normal` or `Urgent` priority (triggering system exclamation chime and high-contrast red styling).
- **Progressive Child Response UX**: Quick 1-tap `[ Ya / Siap ]` confirmation, or expand `[ Tidak ]` with customizable preset chips (`"Sebentar lagi selesai game"`, `"Sedang tugas sekolah"`, `"Oke, segera logout"`) and custom text notes.
- **Turnaround Timing & Parent Adjudication**: Telegram card reports response with turnaround duration (e.g. *"Dijawab dalam 14 detik"*), plus inline adjudication buttons: `[+15m]`, `[+30m]`, `[🔒 Kunci PC]`.
- **Durable Offline Telegram Notification Queue**: Captures immutable timestamps on cold boot or host network disconnection, draining queued alerts when network restores and displaying deferred dispatch tags (`• Waktu: 08:00:12 (Terkirim tertunda: 08:45:00)`), eliminating timestamp drift.

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
|  - UsageMonitorWorker (60s tick, curfew warnings, agent watchdog, lock)         |
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
|  - Repositories: Users, Limits, Schedules, Usage, GraceRequests, AppUsage, Captures|
|  - Security: TotpService (RFC 6238 Base32 + QR PNG + constant-time verify)    |
|  - Platform: SessionManager & NativeMethods (WTSSendMessage, WTSDisconnect)     |
+------------------------------------+------------------------------------------+
               ^                     ^                     ^
               |                     |                     |
+----------------------------+               +----------------------------+
| ParentalControl.Admin      |               | ParentalControl.Agent      |
| (WPF Desktop Application)  |               | (Session Tray & Widget)    |
|                            |               |                            |
| - Dashboard & Telemetry    |               | - Floating Acrylic Widget  |
| - 7-Day Schedule Matrix    |               |   (Adaptive Fullscreen)    |
| - Schedule Exceptions      |               | - Live Tray Countdown      |
| - Quick Grace Buttons      |               | - 1-Click Request Launcher |
| - Telegram & 2FA Settings  |               | - Foreground App Telemetry |
|                            |               | - Silent Screen Capture    |
|                            |               | - Decline Toast Receiver   |
+----------------------------+               +-------------+--------------+
                                                           |
                                                           v
                                        +---------------------------------------+
                                        | Restricted User Desktop (winsta0)     |
                                        | - Unskippable Modal System Dialogs    |
                                        | - Floating HH:MM:SS Countdown Widget  |
                                        | - Browser Request Portal (/request)   |
                                        +---------------------------------------+
```

---

## Technology Stack

| Layer | Technologies & Frameworks | Description |
| :--- | :--- | :--- |
| **Language & Runtime** | **C# 12**, **.NET 8.0 Windows** (`net8.0-windows`) | High-performance modern managed runtime targeting Windows 10/11 x64 with full native API access. |
| **Windows Service** | **Microsoft.Extensions.Hosting**, `BackgroundService` | Session 0 background daemon running as `NT AUTHORITY\LocalSystem` with resilient worker loops. |
| **Web Server & API** | **ASP.NET Core Kestrel** (Embedded) | In-process HTTP server on `0.0.0.0:5050`, serving responsive HTML5 dashboards, REST endpoints, and SSE/polling. |
| **Desktop Admin UI** | **WPF (Windows Presentation Foundation)**, MVVM, XAML | Modern desktop management console with data-binding, responsive custom controls, and live telemetry cards. |
| **Session Agent & Widget** | **WPF Acrylic UI**, Win32 Tray Context | Per-session tray app and frameless floating acrylic countdown widget with corner docking and DWM blur effects. |
| **Native Interop (P/Invoke)** | `wtsapi32.dll`, `user32.dll`, `advapi32.dll`, `kernel32.dll`, `shell32.dll` | Direct Win32 integration: `WTSSendMessageW`, `WTSDisconnectSession`, `WTSQueryUserToken`, `CreateProcessAsUserW`, `GetForegroundWindow`, `SHAppBarMessage`. |
| **Database & Storage** | **SQLite 3** (`Microsoft.Data.Sqlite`), **WAL Mode** | Embedded ACID database at `%ProgramData%\ParentalControl\data.db` secured with SYSTEM & Admin ACLs; non-blocking concurrent reads. |
| **Security & 2FA** | **RFC 6238 TOTP**, **QRCoder**, `RandomNumberGenerator` | Hardware-agnostic two-factor authentication with QR code generation, `FixedTimeEquals` timing-safe comparison, and HttpOnly cookies. |
| **Messaging & Bot** | **Telegram Bot API** (`Telegram.Bot` / Webhook Fallback) | Bidirectional mobile management with interactive inline keyboards, callback routing, and custom reply window trackers. |
| **Telemetry & Capture** | **GDI+** (`System.Drawing.Common`) | Silent virtual desktop capture across all monitors (`SystemInformation.VirtualScreen`) with JPEG compression and rolling 7-day / 500 MB retention pruning. |
| **Installer & Packaging** | **Inno Setup 6**, Self-Contained Single-File Publish | Non-destructive service upgrade installer with elevation delegation (`runascurrentuser`) and auto-spawning hooks. |
| **CI/CD & Automation** | **GitHub Actions**, SemVer Releases | Automated multi-project compilation, test execution, installer packaging, and automated GitHub Releases publishing. |

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
✅ Screen Capture Repository CRUD & Storage Tracking Passed.
✅ Screen Capture Pruning (Retention Days & Size Limit) Passed.
✅ Telegram Dynamic Approval Buttons Calculation Passed.
✅ Sparse Weekly Schedule & Baseline Fallback Verification Passed.
✅ Schedule Exception Precedence & Extended Grace Requests Verification Passed.
✅ AppVersion & UpdateService SemVer Comparison Verification Passed.
✅ UpdateService CheckForUpdatesAsync Passed (Latest: 1.2.1, HasUpdate: False).
ℹ️ UpdateService DownloadUpdateAsync live 137MB download skipped (set TEST_LIVE_DOWNLOAD=1 to run).
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
