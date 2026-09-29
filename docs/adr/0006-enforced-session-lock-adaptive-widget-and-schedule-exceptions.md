# 6. Enforced Session Lock, Adaptive Fullscreen Widget, and Single-Day Schedule Exceptions

## Status
Accepted

## Context
1. **Destructive Session Cutoffs**: The previous session enforcement relied on `NativeMethods.WTSLogoffSession`, abruptly killing open school projects, unpersisted documents, and active gaming states when time expired.
2. **Obtrusive Floating Widget in Games**: A fixed-coordinate floating acrylic countdown card can obstruct heads-up displays, minimaps, crosshairs, or video subtitles during borderless or exclusive fullscreen gameplay.
3. **Advance Schedule Planning**: Restricted users frequently need advance exceptions for upcoming dates (e.g. holidays or weekend study sessions) rather than simple same-day minute extensions (+15m, +30m, +1h).
4. **Transparent Rejection Feedback**: When a parent rejects an extension or schedule request, delivering generic failure notifications causes frustration. Surfacing specific, constructive decline reasons fosters better parent-child coordination.

## Decision
1. **Enforced Session Lock over Hard Logoff**:
   - Replace automatic `WTSLogoffSession` with `NativeMethods.WTSDisconnectSession(sessionId)` (or `LockWorkStation`) when allowance time or curfews expire.
   - Maintain active user processes safely in memory.
   - If an expired user unlocks the workstation, display a 5-second unskippable Win32 `WTSSendMessageW` notification (*"Waktu layar hari ini telah habis. Sesi dikunci kembali."*) and immediately re-lock via `WTSDisconnectSession`.
   - Retain `WTSLogoffSession` strictly for manual emergency administrative logoff commands.

2. **Adaptive Fullscreen Widget with Multi-Monitor Corner Docking**:
   - The Session Agent inspects the active foreground window via `GetForegroundWindow()` and `GetWindowRect()`.
   - If fullscreen geometry is detected:
     - On multi-monitor setups: Move the widget to the user's configured secondary display, docked to a selected corner (`Top-Right`, `Top-Left`, `Bottom-Right`, `Bottom-Left`).
     - On single-monitor setups: Collapse the widget into an unobtrusive semi-transparent mini pill (`[ ⏱️ 24:47 ]`) docked at the top-right corner.
   - Automatically restore original dragged coordinates when exiting fullscreen.
   - Provide a Fullscreen Settings dialog with a live 8-second visual preview button. Settings persist per-user in `%LOCALAPPDATA%\ParentalControl\widget.json`.

3. **Self-Contained Single-Day Schedule Exceptions**:
   - Add a `schedule_exceptions` table storing `user_id`, `exception_date` (`yyyy-MM-dd`), `daily_minutes`, `schedule_start`, and `schedule_end`.
   - Each exception copies full limit parameters at creation time to prevent baseline drift.
   - Precedence: `schedule_exceptions` > `schedule_days` (`IsCustom = true`) > `limits` baseline.
   - Fully manageable (viewable and revocable) in both Web Admin (`:5050/admin`) and WPF Desktop Admin.

4. **Segmented "Jadwal Main" Portal Redesign**:
   - Redesign the `/request` portal using a 7-day pill strip (`[Sen] [Sel •] [Rab]...`) and an active day detail card with instant pill click previews.

5. **Telegram Decline Presets and Feedback Loop**:
   - Telegram inline buttons support 1-tap rejection presets (`[Belum Selesai Tugas]`, `[Waktunya Tidur]`, `[Waktunya Makan]`, `[Melanggar Aturan]`, `[Tulis Alasan]`, `[Batal/Tanpa Alasan]`) with a 3-minute text reply window for custom reasons.
   - Rejection reasons are delivered via an 8-second desktop slide-out acrylic toast banner from the Session Agent and pinned to the `/request` user portal.

6. **Monotonic Clock Tampering Watchdog**:
   - Background service continuously cross-checks `Environment.TickCount64` against `DateTime.UtcNow`.
   - Retroactive or forward clock jumps exceeding 60s trigger administrative Telegram alerts, audit logs, and monotonic quota clamping.

## Consequences
- Open user work and games are preserved safely in memory without allowing unauthorized bypass.
- Seamless, un-obtrusive gaming experience across single and multi-monitor configurations.
- Transparent advance scheduling and structured parent-child communication.
- Robust security posture against system clock rollback exploits.
