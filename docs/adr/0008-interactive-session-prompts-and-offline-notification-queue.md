# 8. Interactive Session Prompts and Offline Notification Queue

## Status
Accepted

## Context
1. **Unidirectional Parent-to-Child Communication**: Administrators previously had no interactive communication channel into active desktop sessions. While `WTSSendMessageW` allowed generic notifications, it was incapable of collecting structured responses (such as "Yes", or "No" with reasons), capturing response turnaround latency, or targeting specific multi-monitor displays.
2. **Multi-Monitor Display Awareness**: In dual-monitor setups (e.g. primary screen playing a fullscreen game or media while secondary screen displays chats or desktop), notifications either appeared on an arbitrary default display or obstructed full-screen gameplay without context.
3. **Offline Session Lifecycle Timestamp Drift**: When a host workstation cold-boots or operates without network connectivity, interactive logon (`OnUserLogon`) and logoff (`OnUserLogoff`) notifications could not reach Telegram immediately. Upon eventual network reconnection, timestamps reflected late dispatch times rather than the true historical moment the user signed in or out, confusing parents and corrupting audit logs.

## Decision
1. **Interactive Session Prompts via Session Agent**:
   - Deliver interactive desktop prompts primarily through `ParentalControl.Agent` using a WPF overlay dialog (`Topmost = true`).
   - If `ParentalControl.Agent` is offline or unreachable, fall back gracefully to Win32 `WTSSendMessageW` with `MB_YESNO`.
   - Support priority tiers (`Normal` and `Urgent`): Urgent prompts trigger audible alert chimes and high-contrast visual styling.
   - Enforce an automatic 120-second countdown timer. If unacknowledged, auto-dismiss the prompt and record a timeout status.

2. **Progressive Response UX and Customizable Presets**:
   - The child prompt UI provides an immediate 1-tap `[ Ya / Siap ]` confirmation.
   - Tapping `[ Tidak ]` reveals configurable response chips (e.g. *"Sebentar lagi selesai game"*, *"Sedang tugas sekolah"*, *"Oke, segera logout"*, *"Tanggung, minta waktu 5 menit lagi"*) plus a custom text box.
   - Tapping a chip populates the input field for instant confirmation or inline editing before sending.
   - Presets persist in `settings` (`prompt_response_presets`), editable via Remote Admin WebUI (`:5050/admin`) and Desktop Admin.
   - Administrators can optionally supply per-message custom options via inline syntax (e.g. `/ask anak Mau makan sekarang? [Sekarang|Nanti 10m]`).

3. **Multi-Monitor Display Targeting**:
   - Dispatch requests accept a target display selector: `Monitor 1` (Primary), `Monitor 2` (Secondary), or `Auto` (active foreground window display).
   - If the requested monitor index is disconnected or unavailable, the Session Agent automatically clamps positioning to `Screen.PrimaryScreen`.

4. **Telegram Adjudication & Parent Quick Actions**:
   - Prompt dispatch is available via Telegram command `/ask [user] [message]` and through the Web Admin active sessions panel.
   - When a child answers or the prompt times out, Telegram receives an audit card detailing the user's answer and Prompt Turnaround Duration (e.g. *"Dijawab dalam 14 detik"*).
   - Telegram cards embed inline action buttons:
     - On "Tidak" / extra time request: `[ +15 Menit ]`, `[ +30 Menit ]`, `[ 🔒 Kunci PC ]`, `[ Abaikan ]`.
     - On "Ya" / acknowledgement: `[ 🔒 Kunci PC ]`, `[ Tutup ]`.

5. **Durable Offline Notification Queue**:
   - Capture immutable `eventTime = DateTime.Now` synchronously at the point of `OnUserLogon` and `OnUserLogoff`.
   - Store unsent Telegram notifications in a persistent SQLite table (`pending_telegram_queue`) whenever host network connectivity is absent.
   - A background queue worker verifies connectivity every 15 seconds and drains queued records.
   - Outgoing messages retain the original `eventTime`. If dispatch latency exceeds 60 seconds, the Telegram card explicitly displays both the event time and the deferred transmission timestamp (`• Waktu Kejadian: 08:00:12 (Tertunda dikirim: 08:45:00)`).

6. **Database Persistence**:
   - Add `session_prompts` table recording prompt payloads, target display, urgency, response status, turnaround duration, and timestamps.
   - Add `pending_telegram_queue` table retaining queued outbound alerts with attempt counters and payloads.

## Consequences
- Parents can communicate interactively with children without leaving Telegram or Web Admin.
- Children can provide fast, low-friction status updates during gameplay or study sessions.
- Reliable event timestamps eliminate misleading logon/logoff alerts during offline boots.
- Graceful multi-monitor clamping prevents lost or invisible dialogs across varied display setups.
