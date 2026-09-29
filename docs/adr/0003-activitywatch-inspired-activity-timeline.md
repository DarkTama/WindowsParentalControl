# 3. ActivityWatch-Inspired Application Timeline and Live Foreground Tracking

## Status
Accepted

## Context
Administrators require deep visibility into which applications children are currently running, historical app launches across dates (today, yesterday, past 7 days), and daily temporal patterns (e.g. late night or gaming spikes), similar to personal activity telemetry suites like [ActivityWatch](https://github.com/ActivityWatch/activitywatch).

Storing high-frequency raw window events with millisecond timestamps into SQLite risks unbounded database growth, high disk write amplification, and complex rolling cleanups on standard consumer PCs. Conversely, keeping only daily aggregate minutes lacks temporal resolution (cannot see *when* an application was used during the day).

## Decision
1. **Live Foreground App Tracking**: Maintain in-memory active application state (`ConcurrentDictionary<string, UserCurrentActivity>`) in `ParentalControl.Service`, updated every 20 seconds by session agents via `POST /api/agent/activity`. If an update was received within 2 minutes, mark as live; otherwise mark as idle/inactive.
2. **Hourly Activity Bucketing**: Introduce `app_activity_hourly` table in SQLite (`user_id`, `date`, `hour`, `process_name`, `minutes`) alongside the existing `app_usage` summary. When an activity tick is received, upsert into both the daily aggregate and the current hour slot (00:00 to 23:00).
3. **ActivityWatch-Inspired WebUI Dashboard**:
   - Date navigation bar: `Today`, `Yesterday`, `Past 7 Days`, and custom `<input type="date">`.
   - User filter selector: `All Users` or individual restricted child accounts.
   - 24-Hour Timeline Bar Chart: 24 horizontal/vertical hourly segments showing computer activity distribution from 00:00 to 23:00.
   - Proportional Top Applications: Horizontal progress bars showing relative share of daily screen time per application with percentage badges.
   - Collapsible Window Titles: Drill-down into specific documents, web pages, or game titles launched under each process.
   - Footer attribution referencing ActivityWatch architecture.
4. **Data Retention**: Include `app_activity_hourly` in the existing 30-day automatic retention purge routine (`DatabaseManager.RetentionDays`).

## Consequences
- Administrators can audit live application usage in real time from mobile or desktop Web Admin.
- Provides ActivityWatch-grade temporal insights with negligible disk usage (a few hundred bytes per active day).
- No heavy external client JavaScript libraries or node modules required; rendered natively with clean CSS/HTML.
- Database migration automatically handles existing installs by creating `app_activity_hourly` table on startup.
