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

## Interactive Session Prompt
An administrative message dispatched into an active user session requiring user acknowledgement (confirmation or decline with an optional explanatory reason) within a defined countdown window.

## Prompt Turnaround Duration
The elapsed duration between the initial presentation of an Interactive Session Prompt on the user screen and the receipt of the user response or expiration timeout.

## Offline Notification Queue
A persistent local storage repository retaining system alerts and session lifecycle notifications during periods of host network unavailability, preserving original event occurrence timestamps until network connectivity is restored.

