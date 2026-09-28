# 2. Session Agent for Foreground App Tracking and Tray UI

## Status
Accepted

## Context
Windows services execute in Session 0, which is isolated from interactive desktop sessions (Session 1+). While Session 0 can enumerate running process IDs across sessions, Win32 APIs like `GetForegroundWindow()` cannot cross the session boundary to identify which application is actively in focus or read window titles. Furthermore, restricted users need an omnipresent, passive indicator of their remaining allowance time and a seamless way to trigger grace requests without manual URL navigation.

## Decision
Introduce `ParentalControl.Agent.exe`, a lightweight background application launched upon user logon into interactive sessions:
1. **Foreground Sampling**: Periodically queries `GetForegroundWindow()` and `GetWindowThreadProcessId()` to record the active foreground application and window title, reporting activity to the core database or service endpoint.
2. **Tray Status Icon**: Displays a system tray icon showing real-time remaining allowance minutes and curfew time.
3. **Grace Request Launcher**: Provides a right-click tray menu option ("Request Screen Time") that opens the local request dialog or launches the local `/request` web endpoint.

## Consequences
- Requires registering the agent to launch at logon (e.g., via Registry `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` or Task Scheduler).
- The agent runs with standard user privileges inside the brother's session.
- Enforcement and forced logoffs remain strictly authoritative inside the Session 0 `ParentalControl.Service` so killing the agent process does not bypass restrictions.
