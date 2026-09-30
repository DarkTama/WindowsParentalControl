# 7. Telegram Session Notifications and Interactive Admin Access Verification

## Status
Accepted

## Context
1. **Parental Visibility on Session Lifecycle**: Administrators need timely awareness when children sign in or sign out of their computers to know when playtime begins and ends without constantly polling the Web Admin dashboard.
2. **Unauthorized Administrative Access**: Family PCs often share an administrator account used for system maintenance. If a child or third party guesses or bypasses the Windows administrator password, parents must be alerted immediately and empowered to terminate or lock the session remotely.
3. **Notification Fatigue & Network Latency**: Firing notifications on every lock/unlock, sleep/wake, or fast user switch causes chat spam. Additionally, during cold boot, the Windows logon event often occurs before the Wi-Fi or DHCP network interface finishes connecting, risking lost Telegram alerts.

## Decision
1. **Explicit Session Logon and Logoff Scoping**:
   - Notifications trigger strictly on interactive session logon (`OnUserLogon`) and logoff (`OnUserLogoff`). Workstation lock/unlock events, screen saver activation, and system sleep/wake transitions are excluded to prevent notification noise.

2. **Configurable Notification Toggles**:
   - Introduce 3 independent toggles in `app_settings` (default enabled once Telegram credentials are set):
     - `telegram_notify_sign_in`: Pushes notifications when managed/restricted accounts sign in.
     - `telegram_notify_sign_out`: Pushes notifications when managed/restricted accounts sign out.
     - `telegram_notify_admin_logon`: Dispatches security alerts when unrestricted/admin accounts sign in.
   - Toggles are configurable via both WPF Desktop Admin (`SettingsView.xaml`) and Web Admin (`:5050/admin`).

3. **Interactive Administrator Verification with Remote Lock**:
   - When an unrestricted/administrator user logs on, the service sends a high-priority security alert (`🚨 PERINGATAN KEAMANAN / SECURITY ALERT`) with an inline keyboard:
     - `✅ Saya Sendiri` (`✅ It's Me`): Confirms authorized access and updates the Telegram message.
     - `🔒 Bukan Saya — Kunci Komputer!` (`🔒 Not Me — Lock PC!`): Dispatches a remote lock command (`SessionManager.LockSession`), logs a `SECURITY_ALERT` audit record, and updates the Telegram card to indicate the workstation was secured.

4. **Structured Card Formatting & Localization**:
   - Notifications adhere to the configured `language_preset` (`id` / `en`).
   - Sign-in alerts include remaining daily quota and allowed curfew hours.
   - Sign-out alerts include total screen time consumed today.

5. **Cold Boot Background Delivery with Retry**:
   - Session alerts run asynchronously in background tasks without delaying the Windows interactive logon experience.
   - Includes automatic retry (3 attempts across 45 seconds: 0s, 15s, 45s) to allow network adapters and internet connections to initialize during boot.

## Consequences
- Parents maintain real-time awareness of PC session lifecycles.
- Immediate defense and containment against unauthorized administrative access from anywhere via Telegram.
- Clean, non-intrusive notification flow with reliable network delivery on boot.
