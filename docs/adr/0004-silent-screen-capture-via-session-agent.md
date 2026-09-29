# 4. Silent Screen Capture via Session Agent and Web Admin Watch Mode

## Status
Accepted

## Context
Administrators need visual verification of what restricted children are viewing or playing on their screens (e.g., verifying homework vs. gaming or detecting unmonitored browser activities).

On Windows, the background monitoring service runs in Session 0 (`LocalSystem`). Due to Windows session isolation (introduced in Windows Vista), Session 0 cannot access interactive user desktops (`winsta0\default`) or invoke desktop GDI capture APIs (`BitBlt`, `CopyFromScreen`) directly. Attempting to capture from Session 0 yields a black rectangle or throws security exceptions.

Furthermore, conventional screenshot utilities or keyboard shortcuts (like `PrtScn` or Windows Snipping Tool) trigger audio-visual indicators (screen flash, notifications, clipboard modifications). Stealth capture requires completely silent in-session execution.

## Decision
1. **In-Session Agent Capture**: Execute screen capture inside `ParentalControl.Agent.exe` within the target user's interactive session. Use `System.Drawing.Graphics.CopyFromScreen` across the complete virtual desktop (`SystemInformation.VirtualScreen`), capturing all connected monitors into a single image.
2. **Silent Execution**: Capture directly into memory without modifying the clipboard, playing audio cues, creating toasts, or altering tray/widget states.
3. **HTTP Polling Dispatch**: Transmit capture triggers from `ParentalControl.Service` to `ParentalControl.Agent` via the existing 5-second `/api/agent/status` polling response. The agent executes the capture, proportionally downscales the image to a maximum dimension of 1920px width, compresses to JPEG (quality 70), and transmits the payload back to the service via `POST /api/agent/capture`.
4. **Capture Modes**:
   - **Single-Shot**: One-time capture invoked on demand from the Web Admin dashboard.
   - **Watch Mode**: Continuous capture refreshed at 10-second intervals while the administrator keeps the dashboard view active. Heartbeat-guarded with a 10-minute safety timeout to prevent accidental disk saturation.
5. **Storage and Pruning**: Store image files on disk at `%ProgramData%\ParentalControl\captures\{userId}\` and index metadata in SQLite table `screen_captures` (`id`, `user_id`, `timestamp`, `file_path`, `width`, `height`, `file_size_bytes`, `trigger_type`). Auto-prune captures older than 7 days or if total capture storage exceeds 500 MB.
6. **Session State Guard**: Reject capture requests immediately if the target session is in `WTSDisconnected` or locked state to prevent storing black frames.
7. **TOTP Guarded Endpoints**: Screen capture management and image retrieval (`GET /api/admin/captures/{id}`) require RFC 6238 TOTP session authentication.

## Consequences
- Complete invisibility to restricted users while preserving multi-monitor visibility for administrators.
- No new external network ports, firewall entries, or named pipe ACLs; piggybacks on existing HTTP agent communication.
- Efficient storage footprint (~150-250 KB per capture) with automatic rolling disk pruning.
- Cannot capture the Windows Secure Desktop (UAC elevation prompts, Ctrl+Alt+Del, or logon screen) by design.
