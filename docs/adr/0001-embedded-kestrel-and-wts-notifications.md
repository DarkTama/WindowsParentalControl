# 1. Embedded Web Host, Native WTS Notifications, and TOTP Authentication

## Status
Accepted

## Context
Restricted users need visibility into remaining allowance and schedule curfews, along with a mechanism to request grace extensions. The administrator needs remote visibility and approval controls over a private network (e.g. Tailscale), plus mobile push notifications and secure authentication.

Existing solutions like Windows Action Center toasts can be muted or suppressed by users (via Focus Assist / Do Not Disturb), and running auxiliary client tray processes inside each user session adds process supervision overhead and vulnerability to user tampering. Additionally, plain PIN-only access to a network-facing admin panel carries credential interception or shoulder-surfing risk.

## Decision
1. **Unskippable In-Session Warnings**: Use native Win32 `WTSSendMessage` directly from the `LocalSystem` service into active session IDs. This displays a system modal dialog that cannot be muted or bypassed.
2. **Co-located Web & Request Server**: Host a lightweight ASP.NET Core Kestrel endpoint within the existing `ParentalControl.Service` Windows Service process on port `5050`.
   - Serves `http://localhost:5050/request` for users to submit grace time requests with reasons. Form is disabled if the PC is offline.
   - Serves an Admin Web UI for remote monitoring over Tailscale.
3. **Authenticator TOTP Security**: Protect the Web Admin Interface with an RFC 6238 Time-based One-Time Password (TOTP) QR setup (matching the design pattern in OpenTerm), verifiable via Google Authenticator, Microsoft Authenticator, or 1Password.
4. **Telegram Bot Alert Channel**: Dispatch grace time requests via Telegram Bot API with inline interactive buttons (`[Approve 15m]`, `[Approve 30m]`, `[Decline]`), allowing 1-tap administrative adjudication directly from a mobile device without port forwarding or custom mobile apps.
5. **Bonus Minutes Accounting**: Grace time extensions are tracked via a `BonusMinutes` column on the current day's usage record rather than modifying the user's permanent limit configuration.

## Consequences
- Single binary deployment for backend service (no separate web service or client tray executable).
- Requires outbound HTTPS access from the service machine to `api.telegram.org` for grace notifications.
- Admin configures Telegram bot token, chat ID, and TOTP secret in global app settings.
- Requests fail gracefully with a user banner if the host PC loses internet connectivity.
