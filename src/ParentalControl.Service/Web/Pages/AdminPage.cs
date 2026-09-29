namespace ParentalControl.Service.Web.Pages;

public static class AdminPage
{
    public static string RenderLogin(string? error = null)
    {
        var errorHtml = !string.IsNullOrEmpty(error)
            ? $"""<div class="banner banner-error">❌ {error}</div>"""
            : "";

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control Admin — Login</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background: #090d16; color: #f8fafc; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 1rem; }
                .card { background: #131c2e; border: 1px solid #1e293b; border-radius: 1rem; padding: 2rem; max-width: 400px; width: 100%; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.6); }
                h1 { font-size: 1.25rem; margin-bottom: 0.5rem; color: #38bdf8; display: flex; align-items: center; gap: 0.5rem; }
                p { font-size: 0.875rem; color: #94a3b8; margin-bottom: 1.5rem; }
                .banner { padding: 0.75rem 1rem; border-radius: 0.5rem; margin-bottom: 1.5rem; font-size: 0.875rem; }
                .banner-error { background: #7f1d1d; color: #fecaca; border: 1px solid #b91c1c; }
                label { display: block; font-size: 0.875rem; color: #cbd5e1; margin-bottom: 0.5rem; font-weight: 500; }
                input[type="text"] { width: 100%; background: #090d16; border: 2px solid #334155; border-radius: 0.5rem; padding: 0.875rem; color: #f8fafc; font-size: 1.5rem; text-align: center; letter-spacing: 0.5rem; margin-bottom: 1.5rem; }
                input[type="text"]:focus { outline: none; border-color: #38bdf8; }
                button { width: 100%; background: #0284c7; color: white; border: none; padding: 0.875rem; border-radius: 0.5rem; font-size: 1rem; font-weight: 600; cursor: pointer; transition: background 0.2s; }
                button:hover { background: #0369a1; }
            </style>
        </head>
        <body>
            <div class="card">
                <h1>🛡️ Admin Authentication</h1>
                <p>Enter the 6-digit TOTP code from your Authenticator app.</p>
                {{errorHtml}}
                <form method="POST" action="/api/admin/login">
                    <label for="code">Authenticator Code:</label>
                    <input type="text" id="code" name="code" maxlength="6" pattern="[0-9]{6}" inputmode="numeric" autocomplete="one-time-code" autofocus required placeholder="000000">
                    <button type="submit">Verify & Login</button>
                </form>
            </div>
        </body>
        </html>
        """;
    }

    public static string RenderDashboard()
    {
        return """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control — Web Admin</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Inter", sans-serif; }
                body { background: #0b1120; color: #f8fafc; padding: 1.5rem; min-height: 100vh; }
                header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; border-bottom: 1px solid #1e293b; padding-bottom: 1rem; flex-wrap: wrap; gap: 1rem; }
                h1 { font-size: 1.4rem; color: #38bdf8; display: flex; align-items: center; gap: 0.5rem; }
                .actions { display: flex; gap: 0.75rem; }
                .btn { padding: 0.45rem 0.9rem; border-radius: 0.375rem; border: none; cursor: pointer; font-size: 0.85rem; font-weight: 600; text-decoration: none; display: inline-flex; align-items: center; gap: 0.375rem; transition: all 0.2s; }
                .btn-primary { background: #0284c7; color: white; }
                .btn-primary:hover { background: #0369a1; }
                .btn-danger { background: #dc2626; color: white; }
                .btn-danger:hover { background: #b91c1c; }
                .btn-success { background: #16a34a; color: white; }
                .btn-success:hover { background: #15803d; }
                .btn-warning { background: #d97706; color: white; }
                .btn-warning:hover { background: #b45309; }
                .btn-secondary { background: #334155; color: #e2e8f0; }
                .btn-secondary:hover { background: #475569; }

                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(360px, 1fr)); gap: 1.5rem; margin-bottom: 2rem; }
                .card { background: #131c2e; border: 1px solid #1e293b; border-radius: 0.85rem; padding: 1.5rem; box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.5); }
                .card h2 { font-size: 1.15rem; color: #94a3b8; margin-bottom: 1rem; display: flex; align-items: center; justify-content: space-between; }

                table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
                th { text-align: left; padding: 0.65rem 0.75rem; background: #0f172a; color: #94a3b8; font-weight: 600; }
                td { padding: 0.75rem; border-bottom: 1px solid #1e293b; }
                tr:last-child td { border-bottom: none; }

                .badge { padding: 0.2rem 0.5rem; border-radius: 0.25rem; font-size: 0.75rem; font-weight: 600; display: inline-flex; align-items: center; gap: 0.25rem; }
                .badge-pending { background: #854d0e; color: #fef08a; }
                .badge-approved { background: #14532d; color: #bbf7d0; }
                .badge-declined { background: #7f1d1d; color: #fecaca; }
                .empty { color: #64748b; font-style: italic; padding: 1.25rem 0; text-align: center; }

                /* Live Status Pulse Animation */
                @keyframes pulse { 0% { opacity: 1; transform: scale(1); } 50% { opacity: 0.35; transform: scale(0.9); } 100% { opacity: 1; transform: scale(1); } }
                .live-dot { width: 8px; height: 8px; border-radius: 50%; background: #22c55e; display: inline-block; box-shadow: 0 0 8px #22c55e; animation: pulse 2s infinite ease-in-out; flex-shrink: 0; }
                .idle-dot { width: 8px; height: 8px; border-radius: 50%; background: #64748b; display: inline-block; flex-shrink: 0; }

                /* Filter Pills (ActivityWatch inspired) */
                .pill-btn { background: #0f172a; border: 1px solid #334155; color: #cbd5e1; padding: 0.35rem 0.85rem; border-radius: 9999px; cursor: pointer; font-size: 0.8rem; font-weight: 600; transition: all 0.2s; user-select: none; }
                .pill-btn:hover { border-color: #38bdf8; color: white; }
                .pill-btn.active { background: #0284c7; border-color: #38bdf8; color: white; }

                /* 24-Hour Timeline Bar Chart */
                .timeline-card { background: #0f172a; border: 1px solid #1e293b; border-radius: 0.65rem; padding: 1rem; margin-bottom: 1.5rem; }
                .timeline-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem; font-size: 0.8rem; color: #94a3b8; }
                .timeline-grid { display: grid; grid-template-columns: repeat(24, 1fr); gap: 3px; height: 55px; align-items: flex-end; background: #0b1120; border-radius: 6px; padding: 6px 4px 2px; }
                .timeline-bar { border-radius: 3px 3px 0 0; min-height: 4px; transition: height 0.3s, background-color 0.2s; position: relative; cursor: pointer; }
                .timeline-bar:hover { filter: brightness(1.25); }
                .timeline-labels { display: grid; grid-template-columns: repeat(24, 1fr); gap: 3px; font-size: 0.65rem; color: #64748b; text-align: center; margin-top: 4px; }

                /* Proportional Stacked Duration Bar */
                .stacked-bar-container { height: 14px; border-radius: 7px; overflow: hidden; display: flex; background: #0b1120; margin-bottom: 1.5rem; border: 1px solid #1e293b; }
                .stacked-bar-segment { height: 100%; transition: width 0.3s; }

                /* Progress bar for apps */
                .progress-track { height: 6px; background: #0b1120; border-radius: 3px; overflow: hidden; margin-top: 4px; width: 100%; border: 1px solid #1e293b; }
                .progress-fill { height: 100%; border-radius: 3px; transition: width 0.3s; }

                /* Modal styles */
                .modal-backdrop { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); z-index: 999; align-items: center; justify-content: center; }
                .modal-box { background: #131c2e; border: 1px solid #334155; border-radius: 0.85rem; padding: 1.75rem; max-width: 420px; width: 90%; box-shadow: 0 25px 50px -12px rgba(0,0,0,0.8); }
            </style>
        </head>
        <body>
            <header>
                <h1>🛡️ Parental Control Remote Admin</h1>
                <div class="actions">
                    <button class="btn btn-secondary" onclick="loadDashboard()">🔄 Refresh</button>
                    <a href="/api/admin/logout" class="btn btn-secondary">🚪 Logout</a>
                </div>
            </header>

            <div class="grid">
                <!-- ═══ ACTIVE USER SESSIONS ═══ -->
                <div class="card">
                    <h2>🖥️ Active User Sessions</h2>
                    <div id="sessionsContainer">Loading sessions...</div>
                </div>

                <!-- ═══ PENDING GRACE REQUESTS ═══ -->
                <div class="card">
                    <h2>🎮 Pending Grace Requests</h2>
                    <div id="requestsContainer">Loading requests...</div>
                </div>
            </div>

            <!-- ═══ ACTIVITYWATCH TELEMETRY TIMELINE ═══ -->
            <div class="card">
                <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:1rem;margin-bottom:1.25rem">
                    <div>
                        <h2 style="margin-bottom:0.25rem">📊 Application Activity & Screen Time</h2>
                        <div style="font-size:0.8rem;color:#64748b">Detailed window telemetry and temporal usage distribution</div>
                    </div>
                    <div id="statTotalTime" style="font-family:ui-monospace, Consolas, monospace;font-size:1.05rem;font-weight:700;color:#38bdf8;background:#0b1120;padding:0.4rem 0.85rem;border-radius:0.5rem;border:1px solid #1e293b">
                        Total: 0m
                    </div>
                </div>

                <!-- Filter Navigation Row -->
                <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:1rem;margin-bottom:1.25rem;border-bottom:1px solid #1e293b;padding-bottom:1rem">
                    <!-- User Pills -->
                    <div style="display:flex;align-items:center;gap:0.4rem;flex-wrap:wrap" id="userPills">
                        <button class="pill-btn active" onclick="setUserFilter('all')">All Users</button>
                    </div>

                    <!-- Date Preset Buttons & Input -->
                    <div style="display:flex;align-items:center;gap:0.5rem;flex-wrap:wrap">
                        <button class="pill-btn active" id="btnDateToday" onclick="setDatePreset('today')">📅 Today</button>
                        <button class="pill-btn" id="btnDateYesterday" onclick="setDatePreset('yesterday')">Yesterday</button>
                        <button class="pill-btn" id="btnDateWeek" onclick="setDatePreset('week')">Past 7 Days</button>
                        <input type="date" id="dateInput" style="background:#0b1120;border:1px solid #334155;color:#f8fafc;padding:0.35rem 0.65rem;border-radius:0.4rem;font-size:0.8rem" onchange="onDateInputChange(event)">
                    </div>
                </div>

                <!-- 24-Hour Timeline Bar Chart -->
                <div class="timeline-card">
                    <div class="timeline-header">
                        <span><strong>24-Hour Temporal Timeline</strong> (00:00 – 23:00)</span>
                        <span id="timelineSelectedLabel" style="color:#cbd5e1">Today</span>
                    </div>
                    <div class="timeline-grid" id="timelineGrid">
                        <!-- 24 hourly columns -->
                    </div>
                    <div class="timeline-labels">
                        <span>00</span><span>01</span><span>02</span><span>03</span><span>04</span><span>05</span>
                        <span>06</span><span>07</span><span>08</span><span>09</span><span>10</span><span>11</span>
                        <span>12</span><span>13</span><span>14</span><span>15</span><span>16</span><span>17</span>
                        <span>18</span><span>19</span><span>20</span><span>21</span><span>22</span><span>23</span>
                    </div>
                </div>

                <!-- Proportional Stacked App Usage Bar -->
                <div style="margin-bottom:0.5rem;display:flex;justify-content:space-between;font-size:0.8rem;color:#94a3b8">
                    <span>Top Applications Proportion</span>
                    <span id="stackedBarSummary"></span>
                </div>
                <div class="stacked-bar-container" id="stackedBar">
                    <div style="width:100%;height:100%;background:#1e293b;opacity:0.5"></div>
                </div>

                <!-- Top Applications Ranked Table -->
                <div id="appUsageContainer">Loading activity...</div>

                <footer style="margin-top:2rem;text-align:center;font-size:0.75rem;color:#64748b;border-top:1px solid #1e293b;padding-top:1rem">
                    Parental Control Remote Admin &bull; Activity telemetry adapted from <a href="https://github.com/ActivityWatch/activitywatch" target="_blank" style="color:#38bdf8;text-decoration:none">ActivityWatch</a>
                </footer>
            </div>

            <!-- ═══ LOCK MODAL ═══ -->
            <div id="lockModal" class="modal-backdrop">
                <div class="modal-box">
                    <h3 style="color:#38bdf8;margin-bottom:0.75rem">🔒 Lock Session</h3>
                    <p style="font-size:0.85rem;color:#cbd5e1;margin-bottom:1rem">
                        Lock user session for <strong id="modalLockUser">User</strong>. Screen will disconnect and time usage accumulation will be paused.
                    </p>
                    <label style="display:block;font-size:0.8rem;color:#94a3b8;margin-bottom:0.35rem">Warning delay before screen lock:</label>
                    <select id="lockDelaySelect" style="width:100%;background:#090d16;border:1px solid #334155;color:#f8fafc;padding:0.6rem;border-radius:0.4rem;margin-bottom:1.25rem">
                        <option value="0">Immediate (0 seconds)</option>
                        <option value="15" selected>15 seconds (Popup countdown)</option>
                        <option value="30">30 seconds</option>
                        <option value="60">1 minute</option>
                    </select>
                    <div style="display:flex;justify-content:flex-end;gap:0.5rem">
                        <button class="btn btn-secondary" onclick="closeLockModal()">Cancel</button>
                        <button class="btn btn-warning" onclick="confirmLockSession()">Confirm Lock</button>
                    </div>
                </div>
            </div>

            <script>
                const COLOR_PALETTE = [
                    '#38bdf8', '#818cf8', '#34d399', '#f472b6', '#fbbf24',
                    '#a78bfa', '#f87171', '#fb923c', '#2dd4bf', '#a3e635'
                ];

                let currentFilter = {
                    user: 'all',
                    date: getTodayIsoString(),
                    range: 'day'
                };

                let targetLockSessionId = null;

                function getTodayIsoString() {
                    const d = new Date();
                    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
                }

                function formatMinutes(mins) {
                    if (mins <= 0) return '0m';
                    const h = Math.floor(mins / 60);
                    const m = mins % 60;
                    if (h > 0) return `${h}h ${m}m`;
                    return `${m}m`;
                }

                async function loadDashboard() {
                    try {
                        const url = `/api/admin/dashboard?user=${encodeURIComponent(currentFilter.user)}&date=${encodeURIComponent(currentFilter.date)}&range=${encodeURIComponent(currentFilter.range)}`;
                        const res = await fetch(url);
                        if (res.status === 401) { window.location.href = '/admin'; return; }
                        const data = await res.json();

                        renderSessions(data.sessions);
                        renderRequests(data.requests);
                        renderUserPills(data.availableUsers, data.selectedUser);
                        renderTimeline(data.hourlyTimeline, data.selectedDate, data.selectedRange);
                        renderAppUsage(data.appUsage, data.totalMinutes);
                    } catch (e) {
                        console.error('Error loading dashboard:', e);
                    }
                }

                function renderSessions(sessions) {
                    const c = document.getElementById('sessionsContainer');
                    if (!sessions || sessions.length === 0) {
                        c.innerHTML = '<div class="empty">No active sessions right now.</div>';
                        return;
                    }
                    let html = '<table><thead><tr><th>User</th><th>Current Activity</th><th>Remaining</th><th>Curfew</th><th>Quick Actions</th></tr></thead><tbody>';
                    for (const s of sessions) {
                        const liveIndicator = s.isAppLive
                            ? `<span class="live-dot" title="Active foreground app"></span>`
                            : `<span class="idle-dot" title="Idle / Inactive"></span>`;

                        const currentAppHtml = `
                            <div style="display:flex;align-items:center;gap:0.4rem;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                                ${liveIndicator}
                                <span style="font-size:0.8rem;color:${s.isAppLive ? '#e2e8f0' : '#64748b'}" title="${s.currentApp}">${s.currentApp}</span>
                            </div>`;

                        if (!s.isRestricted) {
                            html += `<tr>
                                <td><strong>${s.username}</strong> <span class="badge" style="background:#334155;color:#38bdf8">Admin</span><br><small style="color:#64748b">Session ${s.sessionId}</small></td>
                                <td>${currentAppHtml}</td>
                                <td><span style="color:#64748b">—</span></td>
                                <td><span style="color:#64748b">—</span></td>
                                <td>
                                    <div style="display:flex;gap:0.35rem;flex-wrap:wrap">
                                        <button class="btn btn-warning" style="padding:0.25rem 0.5rem" onclick="openLockModal(${s.sessionId}, '${s.username}')">🔒 Lock</button>
                                        <button class="btn btn-danger" style="padding:0.25rem 0.5rem" onclick="forceLogoff(${s.sessionId})">Logoff</button>
                                    </div>
                                </td>
                            </tr>`;
                        } else {
                            const lockBadge = s.isLocked
                                ? ' <span class="badge" style="background:#854d0e;color:#fef08a">Locked (Paused)</span>'
                                : '';
                            html += `<tr>
                                <td><strong>${s.username}</strong>${lockBadge}<br><small style="color:#64748b">Session ${s.sessionId}</small></td>
                                <td>${currentAppHtml}</td>
                                <td><strong style="color:#38bdf8">${s.remainingMinutes}m</strong> / ${s.totalAllowed}m</td>
                                <td>${s.curfew}</td>
                                <td>
                                    <div style="display:flex;gap:0.35rem;flex-wrap:wrap">
                                        <button class="btn btn-warning" style="padding:0.25rem 0.5rem" onclick="openLockModal(${s.sessionId}, '${s.username}')">🔒 Lock</button>
                                        <button class="btn btn-success" style="padding:0.25rem 0.5rem" onclick="grantTime(${s.userId}, 15)">+15m</button>
                                        <button class="btn btn-success" style="padding:0.25rem 0.5rem" onclick="grantTime(${s.userId}, 30)">+30m</button>
                                        <button class="btn btn-danger" style="padding:0.25rem 0.5rem" onclick="forceLogoff(${s.sessionId})">Logoff</button>
                                    </div>
                                </td>
                            </tr>`;
                        }
                    }
                    html += '</tbody></table>';
                    c.innerHTML = html;
                }

                function renderRequests(requests) {
                    const c = document.getElementById('requestsContainer');
                    if (!requests || requests.length === 0) {
                        c.innerHTML = '<div class="empty">No requests submitted today.</div>';
                        return;
                    }
                    let html = '<table><thead><tr><th>User</th><th>Req</th><th>Reason</th><th>Action</th></tr></thead><tbody>';
                    for (const r of requests) {
                        const isPending = r.status === 'PENDING';
                        let actionHtml = `<span class="badge badge-${r.status.toLowerCase()}">${r.status}</span>`;
                        if (isPending) {
                            actionHtml = `
                                <div style="display:flex;gap:0.35rem">
                                    <button class="btn btn-success" style="padding:0.25rem 0.5rem" onclick="resolveRequest(${r.id}, 'approve', ${r.requestedMinutes})">Approve</button>
                                    <button class="btn btn-danger" style="padding:0.25rem 0.5rem" onclick="resolveRequest(${r.id}, 'decline', 0)">Decline</button>
                                </div>`;
                        }
                        html += `<tr>
                            <td><strong>${r.username}</strong><br><small style="color:#64748b">${r.createdAt}</small></td>
                            <td>+${r.requestedMinutes}m</td>
                            <td style="max-width:180px;word-break:break-word">${r.reason}</td>
                            <td>${actionHtml}</td>
                        </tr>`;
                    }
                    html += '</tbody></table>';
                    c.innerHTML = html;
                }

                function renderUserPills(users, selectedUser) {
                    const container = document.getElementById('userPills');
                    let html = `<button class="pill-btn ${selectedUser === 'all' ? 'active' : ''}" onclick="setUserFilter('all')">All Users</button>`;
                    if (users) {
                        for (const u of users) {
                            const isActive = selectedUser.toLowerCase() === u.toLowerCase();
                            html += `<button class="pill-btn ${isActive ? 'active' : ''}" onclick="setUserFilter('${u}')">${u}</button>`;
                        }
                    }
                    container.innerHTML = html;
                }

                function setUserFilter(user) {
                    currentFilter.user = user;
                    loadDashboard();
                }

                function setDatePreset(preset) {
                    document.getElementById('btnDateToday').classList.remove('active');
                    document.getElementById('btnDateYesterday').classList.remove('active');
                    document.getElementById('btnDateWeek').classList.remove('active');

                    if (preset === 'today') {
                        document.getElementById('btnDateToday').classList.add('active');
                        currentFilter.date = getTodayIsoString();
                        currentFilter.range = 'day';
                    } else if (preset === 'yesterday') {
                        document.getElementById('btnDateYesterday').classList.add('active');
                        const y = new Date();
                        y.setDate(y.getDate() - 1);
                        currentFilter.date = `${y.getFullYear()}-${String(y.getMonth() + 1).padStart(2, '0')}-${String(y.getDate()).padStart(2, '0')}`;
                        currentFilter.range = 'day';
                    } else if (preset === 'week') {
                        document.getElementById('btnDateWeek').classList.add('active');
                        currentFilter.date = getTodayIsoString();
                        currentFilter.range = 'week';
                    }
                    document.getElementById('dateInput').value = currentFilter.date;
                    loadDashboard();
                }

                function onDateInputChange(e) {
                    const val = e.target.value;
                    if (!val) return;
                    document.getElementById('btnDateToday').classList.remove('active');
                    document.getElementById('btnDateYesterday').classList.remove('active');
                    document.getElementById('btnDateWeek').classList.remove('active');
                    currentFilter.date = val;
                    currentFilter.range = 'day';
                    loadDashboard();
                }

                function renderTimeline(timeline, dateStr, range) {
                    const grid = document.getElementById('timelineGrid');
                    const label = document.getElementById('timelineSelectedLabel');

                    label.textContent = range === 'week' ? `Past 7 Days (Ending ${dateStr})` : `Date: ${dateStr}`;

                    if (!timeline || timeline.length !== 24) {
                        grid.innerHTML = '';
                        return;
                    }

                    const maxMinutes = Math.max(1, ...timeline);
                    let html = '';
                    for (let h = 0; h < 24; h++) {
                        const m = timeline[h] || 0;
                        const heightPct = m === 0 ? 8 : Math.max(12, Math.round((m / maxMinutes) * 100));

                        let barColor = '#1e293b';
                        if (m > 0 && m <= 15) barColor = '#0284c7';
                        else if (m > 15 && m <= 30) barColor = '#38bdf8';
                        else if (m > 30 && m <= 45) barColor = '#818cf8';
                        else if (m > 45) barColor = '#f59e0b';

                        const tooltip = `Hour ${String(h).padStart(2, '0')}:00 – ${String(h + 1).padStart(2, '0')}:00: ${m} mins active`;
                        html += `<div class="timeline-bar" style="height:${heightPct}%;background:${barColor}" title="${tooltip}"></div>`;
                    }
                    grid.innerHTML = html;
                }

                function renderAppUsage(apps, totalMinutes) {
                    const c = document.getElementById('appUsageContainer');
                    const statTotal = document.getElementById('statTotalTime');
                    const stackedBar = document.getElementById('stackedBar');
                    const stackedSummary = document.getElementById('stackedBarSummary');

                    statTotal.textContent = `Total Active Time: ${formatMinutes(totalMinutes || 0)}`;

                    if (!apps || apps.length === 0) {
                        c.innerHTML = '<div class="empty">No app activity recorded for the selected period.</div>';
                        stackedBar.innerHTML = '<div style="width:100%;height:100%;background:#1e293b;opacity:0.5"></div>';
                        stackedSummary.textContent = '0 apps';
                        return;
                    }

                    // Render Stacked Bar
                    let stackedHtml = '';
                    let topAppsCount = Math.min(apps.length, COLOR_PALETTE.length);
                    for (let i = 0; i < topAppsCount; i++) {
                        const app = apps[i];
                        const color = COLOR_PALETTE[i % COLOR_PALETTE.length];
                        stackedHtml += `<div class="stacked-bar-segment" style="width:${app.percentage}%;background:${color}" title="${app.processName}: ${app.minutes}m (${app.percentage}%)"></div>`;
                    }
                    stackedBar.innerHTML = stackedHtml;
                    stackedSummary.textContent = `${apps.length} applications logged`;

                    // Render Ranked Apps Table
                    let html = '<table><thead><tr><th>User</th><th>Process / Game</th><th>Window Title Summary</th><th>Share</th><th>Duration</th></tr></thead><tbody>';
                    for (let i = 0; i < apps.length; i++) {
                        const a = apps[i];
                        const color = COLOR_PALETTE[i % COLOR_PALETTE.length];
                        html += `<tr>
                            <td><strong>${a.username}</strong></td>
                            <td>
                                <div style="display:flex;align-items:center;gap:0.4rem">
                                    <span style="width:10px;height:10px;border-radius:2px;background:${color};display:inline-block"></span>
                                    <code style="background:#090d16;padding:0.2rem 0.45rem;border-radius:0.25rem;font-size:0.8rem">${a.processName}</code>
                                </div>
                            </td>
                            <td style="color:#cbd5e1;max-width:280px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${a.windowTitle || '-'}">${a.windowTitle || '-'}</td>
                            <td style="width:150px">
                                <div style="font-size:0.75rem;color:#94a3b8;margin-bottom:2px">${a.percentage}%</div>
                                <div class="progress-track">
                                    <div class="progress-fill" style="width:${a.percentage}%;background:${color}"></div>
                                </div>
                            </td>
                            <td><strong>${formatMinutes(a.minutes)}</strong></td>
                        </tr>`;
                    }
                    html += '</tbody></table>';
                    c.innerHTML = html;
                }

                async function grantTime(userId, minutes) {
                    if (!confirm(`Grant +${minutes} bonus minutes to this user?`)) return;
                    await fetch('/api/admin/grant', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ userId, minutes })
                    });
                    loadDashboard();
                }

                async function forceLogoff(sessionId) {
                    if (!confirm(`Force logoff session ${sessionId}? Any unsaved work will be lost.`)) return;
                    await fetch('/api/admin/force-logoff', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ sessionId })
                    });
                    loadDashboard();
                }

                function openLockModal(sessionId, username) {
                    targetLockSessionId = sessionId;
                    document.getElementById('modalLockUser').textContent = username;
                    document.getElementById('lockModal').style.display = 'flex';
                }

                function closeLockModal() {
                    document.getElementById('lockModal').style.display = 'none';
                    targetLockSessionId = null;
                }

                async function confirmLockSession() {
                    if (!targetLockSessionId) return;
                    const delaySeconds = parseInt(document.getElementById('lockDelaySelect').value, 10);
                    const sessionId = targetLockSessionId;
                    closeLockModal();

                    await fetch('/api/admin/lock-session', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ sessionId, delaySeconds })
                    });
                    loadDashboard();
                }

                async function resolveRequest(requestId, action, minutes) {
                    await fetch('/api/admin/resolve-request', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ requestId, action, minutes })
                    });
                    loadDashboard();
                }

                // Initial load and periodic polling every 10s
                document.getElementById('dateInput').value = currentFilter.date;
                loadDashboard();
                setInterval(loadDashboard, 10000);
            </script>
        </body>
        </html>
        """;
    }
}
