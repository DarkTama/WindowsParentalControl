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
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background: #0b1120; color: #f8fafc; padding: 1.5rem; min-height: 100vh; }
                header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; border-bottom: 1px solid #1e293b; padding-bottom: 1rem; }
                h1 { font-size: 1.5rem; color: #38bdf8; display: flex; align-items: center; gap: 0.5rem; }
                .actions { display: flex; gap: 0.75rem; }
                .btn { padding: 0.5rem 1rem; border-radius: 0.375rem; border: none; cursor: pointer; font-size: 0.875rem; font-weight: 600; text-decoration: none; display: inline-flex; align-items: center; gap: 0.375rem; transition: background 0.2s; }
                .btn-primary { background: #0284c7; color: white; }
                .btn-primary:hover { background: #0369a1; }
                .btn-danger { background: #dc2626; color: white; }
                .btn-danger:hover { background: #b91c1c; }
                .btn-success { background: #16a34a; color: white; }
                .btn-success:hover { background: #15803d; }
                .btn-secondary { background: #334155; color: #e2e8f0; }
                .btn-secondary:hover { background: #475569; }
                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(350px, 1fr)); gap: 1.5rem; margin-bottom: 2rem; }
                .card { background: #131c2e; border: 1px solid #1e293b; border-radius: 0.75rem; padding: 1.5rem; box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.5); }
                .card h2 { font-size: 1.125rem; color: #94a3b8; margin-bottom: 1rem; display: flex; align-items: center; justify-content: space-between; }
                table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
                th { text-align: left; padding: 0.625rem 0.75rem; background: #0f172a; color: #94a3b8; font-weight: 600; }
                td { padding: 0.75rem; border-bottom: 1px solid #1e293b; }
                tr:last-child td { border-bottom: none; }
                .badge { padding: 0.2rem 0.5rem; border-radius: 0.25rem; font-size: 0.75rem; font-weight: 600; }
                .badge-pending { background: #854d0e; color: #fef08a; }
                .badge-approved { background: #14532d; color: #bbf7d0; }
                .badge-declined { background: #7f1d1d; color: #fecaca; }
                .empty { color: #64748b; font-style: italic; padding: 1rem 0; text-align: center; }
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
                <div class="card">
                    <h2>🖥️ Active User Sessions</h2>
                    <div id="sessionsContainer">Loading...</div>
                </div>

                <div class="card">
                    <h2>🎮 Pending Grace Requests</h2>
                    <div id="requestsContainer">Loading...</div>
                </div>
            </div>

            <div class="card">
                <h2>📊 Today's Foreground App Activity</h2>
                <div id="appUsageContainer">Loading...</div>
            </div>

            <script>
                async function loadDashboard() {
                    try {
                        const res = await fetch('/api/admin/dashboard');
                        if (res.status === 401) { window.location.href = '/admin'; return; }
                        const data = await res.json();
                        renderSessions(data.sessions);
                        renderRequests(data.requests);
                        renderAppUsage(data.appUsage);
                    } catch (e) {
                        console.error(e);
                    }
                }

                function renderSessions(sessions) {
                    const c = document.getElementById('sessionsContainer');
                    if (!sessions || sessions.length === 0) {
                        c.innerHTML = '<div class="empty">No active sessions right now.</div>';
                        return;
                    }
                    let html = '<table><thead><tr><th>User</th><th>Remaining</th><th>Curfew</th><th>Quick Actions</th></tr></thead><tbody>';
                    for (const s of sessions) {
                        if (!s.isRestricted) {
                            html += `<tr>
                                <td><strong>${s.username}</strong> <span class="badge" style="background:#334155;color:#38bdf8;font-size:0.7rem;padding:0.15rem 0.4rem;border-radius:4px;margin-left:4px">Admin</span><br><small style="color:#64748b">Session ${s.sessionId}</small></td>
                                <td><span style="color:#22c55e;font-weight:600">Unlimited</span></td>
                                <td><span style="color:#64748b">—</span></td>
                                <td>
                                    <button class="btn btn-danger" style="padding:0.25rem 0.5rem" onclick="forceLogoff(${s.sessionId})">Logoff</button>
                                </td>
                            </tr>`;
                        } else {
                            html += `<tr>
                                <td><strong>${s.username}</strong><br><small style="color:#64748b">Session ${s.sessionId}</small></td>
                                <td><strong style="color:#38bdf8">${s.remainingMinutes}m</strong> / ${s.totalAllowed}m</td>
                                <td>${s.curfew}</td>
                                <td>
                                    <div style="display:flex;gap:0.35rem;flex-wrap:wrap">
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

                function renderAppUsage(appUsage) {
                    const c = document.getElementById('appUsageContainer');
                    if (!appUsage || appUsage.length === 0) {
                        c.innerHTML = '<div class="empty">No app activity logged today.</div>';
                        return;
                    }
                    let html = '<table><thead><tr><th>User</th><th>Process / Game</th><th>Window Title</th><th>Duration</th></tr></thead><tbody>';
                    for (const a of appUsage) {
                        html += `<tr>
                            <td><strong>${a.username}</strong></td>
                            <td><code style="background:#090d16;padding:0.2rem 0.4rem;border-radius:0.25rem">${a.processName}</code></td>
                            <td style="color:#cbd5e1">${a.windowTitle || '-'}</td>
                            <td><strong>${a.minutes} min</strong></td>
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
                    if (!confirm('Forcefully log off this session now?')) return;
                    await fetch('/api/admin/force-logoff', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ sessionId })
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

                loadDashboard();
                setInterval(loadDashboard, 10000);
            </script>
        </body>
        </html>
        """;
    }
}
