# Glossary

## User Sign-In (Logon)
A Windows interactive session logon event (`OnUserLogon`) initiated when a user authenticates at the Windows logon screen and enters a new interactive desktop session. Does not include workstation unlock events.

## User Sign-Out (Logoff)
A Windows interactive session termination event (`OnUserLogoff`) initiated when a user explicitly signs out of Windows, closes all running applications, and destroys the active session. Does not include workstation lock events.

## Restricted User
A managed Windows user account subject to daily screen time quotas, scheduled curfew boundaries, and forced session locks enforced by Parental Control Service.

## Admin / Unrestricted User
A Windows user account with administrative privileges or unrestricted status that bypasses quota and curfew enforcement. Monitored optionally for unauthorized access alerts.

## Session Telegram Notification
An automated push message sent to the administrator's Telegram chat upon user sign-in or sign-out, containing identity, timestamp, machine name, and screen time status.
## Admin Access Verification
An interactive Telegram security alert dispatched when an unrestricted or administrator user signs in. Contains machine name, timestamp, and an immediate remote lock action (`LOCK_SESSION:<id>`) allowing the administrator to instantly lock the workstation if the logon was unauthorized.
