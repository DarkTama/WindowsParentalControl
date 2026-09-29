# 5. Built-in In-Place Updater and Sparse Weekly Schedule Overrides

## Status
Accepted

## Context
1. **Application Updates**: As new capabilities and bug fixes are committed, administrators need a straightforward way to verify if an update exists, review what has changed, and update the system without manually navigating GitHub releases or manually running installers from the command line.
2. **Weekly Schedule Clarity**: Previously, setting a schedule forced every day of the week to be saved as individual database rows. This obliterated the semantic distinction between the user's default daily limit and custom days (e.g. weekends vs. school days), and cluttered user-facing portals with redundant daily schedules.
3. **User Transparency ("Jadwal Main")**: Restricted children often wonder why they were logged off or when their allowance kicks in. Presenting a clean "Jadwal Main" summary on the `/request` portal promotes transparency without exposing administrative dials.

## Decision
1. **GitHub Releases In-Place Updater**:
   - Background service and Admin interfaces query `https://api.github.com/repos/DarkTama/WindowsParentalControl/releases/latest` using semver comparison against `AppVersion.Current`.
   - Admin UI (Desktop & Web Admin) displays release notes, publication date, and an "Update Now" trigger.
   - When triggered, the service downloads `ParentalControlSetup.exe` into a temporary scratch directory and executes the installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
   - The installer gracefully stops services, updates files, and restarts services/agents.
2. **Sparse Schedule Override Persistence**:
   - The primary `limits` table defines the baseline limit (`daily_minutes`, `schedule_start`, `schedule_end`) for each restricted user.
   - The `schedule_days` table is treated strictly as an override list. Only days where `IsCustom = true` are stored. Untoggled days are deleted via `DELETE FROM schedule_days WHERE user_id = @userId AND day_of_week = @day`.
   - Quick presets (`Set Hari Sekolah (Sen–Jum)` and `Set Akhir Pekan (Sab–Min)`) batch-apply overrides to multiple days simultaneously.
3. **"Jadwal Main" Portal Card (`/request`)**:
   - On `/request?user={username}`, render a dedicated "📅 Jadwal Main Mingguan" card.
   - Highlights the baseline daily quota and active schedule window.
   - Explicitly enumerates only customized days (e.g. `Sabtu` and `Minggu`), omitting duplicate default days.
   - Adds a visual "Hari ini" badge to whichever schedule applies to the current day.
4. **Unified Multi-Interface Management**:
   - Both Web Admin (`/admin`) and WPF Desktop Admin (`ParentalControl.Admin`) provide full CRUD management over baseline limits and sparse 7-day overrides.

## Consequences
- Administrators can audit and update the installation with 1 click over LAN or Tailscale.
- Clean database footprint and simplified schedule maintenance.
- Children have full transparency over when they are allowed to use the computer.
