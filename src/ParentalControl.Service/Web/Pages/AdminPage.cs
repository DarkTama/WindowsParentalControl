using ParentalControl.Core;

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
                body { background: #0b1120; color: #f8fafc; padding: 1.5rem; min-height: 100vh; overflow-x: hidden; }
                header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; border-bottom: 1px solid #1e293b; padding-bottom: 1rem; flex-wrap: wrap; gap: 1rem; }
                h1 { font-size: 1.35rem; color: #38bdf8; display: flex; align-items: center; gap: 0.5rem; }
                .actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
                .btn { padding: 0.45rem 0.85rem; border-radius: 0.375rem; border: none; cursor: pointer; font-size: 0.825rem; font-weight: 600; text-decoration: none; display: inline-flex; align-items: center; justify-content: center; gap: 0.35rem; transition: all 0.2s; white-space: nowrap; }
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

                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 360px), 1fr)); gap: 1.5rem; margin-bottom: 2rem; }
                .card { background: #131c2e; border: 1px solid #1e293b; border-radius: 0.85rem; padding: 1.5rem; box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.5); overflow: hidden; max-width: 100%; }
                .card h2 { font-size: 1.15rem; color: #94a3b8; margin-bottom: 1rem; display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 0.5rem; }

                .table-responsive { width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch; margin-bottom: 0.5rem; }
                .table-responsive::-webkit-scrollbar { height: 6px; }
                .table-responsive::-webkit-scrollbar-thumb { background: #334155; border-radius: 3px; }
                table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
                th { text-align: left; padding: 0.65rem 0.75rem; background: #0f172a; color: #94a3b8; font-weight: 600; white-space: nowrap; }
                td { padding: 0.65rem 0.75rem; border-bottom: 1px solid #1e293b; vertical-align: middle; }
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


                /* Screen Capture styles */
                .cap-thumb { width: 96px; height: 54px; object-fit: cover; border-radius: 4px; border: 2px solid #334155; cursor: pointer; transition: all 0.2s; flex-shrink: 0; opacity: 0.75; }
                .cap-thumb:hover, .cap-thumb.active { border-color: #38bdf8; opacity: 1; transform: scale(1.03); }
                /* Modal styles */
                .modal-backdrop { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.75); backdrop-filter: blur(4px); z-index: 999; align-items: center; justify-content: center; padding: 0.75rem; }
                .modal-box { background: #131c2e; border: 1px solid #334155; border-radius: 0.85rem; padding: 1.5rem; max-width: 580px; width: 100%; max-height: 90vh; overflow-y: auto; box-shadow: 0 25px 50px -12px rgba(0,0,0,0.8); }

                /* Mobile Responsiveness */
                @media (max-width: 768px) {
                    body { padding: 0.75rem 0.5rem; }
                    header { margin-bottom: 1.25rem; flex-direction: column; align-items: stretch; gap: 0.75rem; padding-bottom: 0.75rem; }
                    .header-brand { display: flex; align-items: center; justify-content: space-between; width: 100%; flex-wrap: wrap; gap: 0.5rem; }
                    h1 { font-size: 1.15rem; }
                    .actions { width: 100%; display: flex; gap: 0.35rem; }
                    .actions .btn { flex: 1; text-align: center; justify-content: center; font-size: 0.75rem; padding: 0.45rem 0.25rem; }
                    .grid { grid-template-columns: 1fr; gap: 1rem; margin-bottom: 1rem; }
                    .card { padding: 1rem 0.75rem; border-radius: 0.75rem; margin-bottom: 1rem !important; }
                    .card h2 { font-size: 1rem; }
                    .timeline-card { padding: 0.65rem 0.4rem; }
                    .timeline-grid { gap: 1.5px; height: 48px; padding: 4px 2px 2px; }
                    .timeline-labels { gap: 1.5px; font-size: 0.55rem; }
                    .modal-box { padding: 1.25rem 0.85rem; width: 100%; max-height: 88vh; }
                    .btn { font-size: 0.78rem; padding: 0.4rem 0.65rem; }
                    .card-header-flex { flex-direction: column; align-items: stretch !important; gap: 0.6rem !important; }
                    .btn-group-responsive { width: 100%; display: flex; gap: 0.35rem; }
                    .btn-group-responsive .btn { flex: 1; text-align: center; justify-content: center; font-size: 0.72rem; padding: 0.4rem 0.2rem; }
                }
            </style>
        </head>
        <body>
            <header>
                <div class="header-brand">
                    <h1 style="margin:0">🛡️ Parental Control Remote Admin</h1>
                    <div style="display:flex;align-items:center;gap:0.5rem">
                        <span class="badge" style="background:#1e293b;color:#94a3b8" id="versionBadge">__APP_VERSION__</span>
                        <button id="btnUpdateNotice" class="badge" style="display:none;background:#15803d;color:#dcfce7;border:none;cursor:pointer;padding:0.25rem 0.6rem;font-weight:600" onclick="checkAppUpdates()">🚀 Update Available!</button>
                    </div>
                </div>
                <div class="actions">
                    <button class="btn btn-secondary" onclick="checkAppUpdates()">🚀 Check Update</button>
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

            <!-- ═══ MANAGED ACCOUNTS & SCHEDULES ═══ -->
            <div class="card" style="margin-bottom: 2rem;">
                <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:1rem;margin-bottom:1rem">
                    <div>
                        <h2>👥 Managed User Accounts &amp; Weekly Schedules</h2>
                        <div style="font-size:0.8rem;color:#64748b">Configure default quotas, curfew windows, and per-day customized schedules</div>
                    </div>
                </div>
                <div id="usersScheduleContainer">Loading managed accounts...</div>
            </div>

            <!-- ═══ LIVE SCREEN SUPERVISION CARD ═══ -->
            <div class="card" style="margin-bottom: 2rem;">
                <div class="card-header-flex" style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:1rem;margin-bottom:1rem">
                    <div style="display:flex;align-items:center;gap:0.75rem;flex-wrap:wrap">
                        <h2 style="margin:0">📸 Live Desktop Screen Supervision</h2>
                        <span id="watchBadge" class="badge" style="display:none;background:#15803d;color:#dcfce7;animation:pulse 2s infinite">● Watch Mode Active (10s)</span>
                    </div>
                    <div class="btn-group-responsive" style="display:flex;gap:0.5rem;flex-wrap:wrap">
                        <button class="btn btn-primary" id="btnSnapScreen" onclick="captureCurrentScreen()">📸 Capture Now</button>
                        <button class="btn btn-secondary" id="btnToggleWatch" onclick="toggleWatchMode()">🎥 Watch Mode (10s)</button>
                        <button class="btn btn-secondary" id="btnSendTelegram" onclick="sendCurrentCaptureToTelegram()">📱 Send to Telegram</button>
                    </div>
                </div>

                <div id="captureContainer" style="display:flex;flex-direction:column;gap:1rem">
                    <!-- Main Viewport -->
                    <div style="position:relative;background:#090d16;border:1px solid #1e293b;border-radius:0.65rem;min-height:260px;display:flex;align-items:center;justify-content:center;overflow:hidden">
                        <img id="captureImg" src="" alt="Screen Capture" style="display:none;max-width:100%;max-height:460px;object-fit:contain;cursor:zoom-in;border-radius:0.4rem" onclick="openCaptureLightbox()" />
                        <div id="capturePlaceholder" style="color:#64748b;font-size:0.875rem;padding:2.5rem 1rem;text-align:center">
                            No recent screen capture loaded. Click <strong>📸 Capture Now</strong> or enable <strong>🎥 Watch Mode</strong>.
                        </div>
                        <div id="captureMetaBar" style="display:none;position:absolute;bottom:0;left:0;right:0;background:rgba(9, 13, 22, 0.85);backdrop-filter:blur(4px);padding:0.45rem 0.85rem;font-size:0.75rem;color:#cbd5e1;display:flex;justify-content:space-between;align-items:center">
                            <span id="captureMetaUserTime"></span>
                            <span id="captureMetaDetails" style="color:#94a3b8"></span>
                        </div>
                    </div>

                    <!-- Recent Thumbnails Strip -->
                    <div>
                        <div style="font-size:0.8rem;color:#94a3b8;margin-bottom:0.4rem;display:flex;justify-content:space-between">
                            <span>Recent Captures Today</span>
                            <span id="captureCountBadge" style="color:#64748b">0 captures</span>
                        </div>
                        <div id="captureThumbnails" style="display:flex;gap:0.6rem;overflow-x:auto;padding-bottom:0.5rem;align-items:center">
                            <span style="font-size:0.75rem;color:#64748b;font-style:italic">No captures yet</span>
                        </div>
                    </div>
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

            <!-- ═══ CAPTURE LIGHTBOX MODAL ═══ -->
            <div id="lightboxModal" class="modal-backdrop" onclick="closeCaptureLightbox()">
                <div style="position:relative;max-width:94vw;max-height:94vh;display:flex;align-items:center;justify-content:center" onclick="event.stopPropagation()">
                    <img id="lightboxImg" src="" style="max-width:100%;max-height:92vh;border-radius:0.5rem;box-shadow:0 25px 50px -12px rgba(0,0,0,0.9);border:1px solid #334155" />
                    <button style="position:absolute;top:-12px;right:-12px;background:#dc2626;color:white;border:none;border-radius:50%;width:32px;height:32px;font-weight:bold;cursor:pointer;font-size:1rem;display:flex;align-items:center;justify-content:center" onclick="closeCaptureLightbox()">✕</button>
                </div>
            </div>

            <!-- ═══ SCHEDULE MANAGEMENT MODAL ═══ -->
            <div id="scheduleModal" class="modal-backdrop">
                <div class="modal-box" style="max-width: 650px; max-height: 90vh; overflow-y: auto;">
                    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem">
                        <h3 style="color:#38bdf8;margin:0">📅 Edit Schedule: <span id="schModalUsername"></span></h3>
                        <button type="button" onclick="closeScheduleModal()" style="background:transparent;border:none;color:#94a3b8;font-size:1.2rem;cursor:pointer">✕</button>
                    </div>
                    
                    <input type="hidden" id="schModalUserId" />

                    <!-- Base Limits -->
                    <div style="background:#090d16;border:1px solid #1e293b;border-radius:0.5rem;padding:0.85rem;margin-bottom:1rem">
                        <div style="font-weight:700;font-size:0.85rem;color:#e2e8f0;margin-bottom:0.5rem">⚙️ Baseline Default Limits (Applied to Untoggled Days)</div>
                        <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(140px, 1fr));gap:0.75rem">
                            <div>
                                <label style="font-size:0.75rem;color:#94a3b8">Daily Minutes:</label>
                                <input type="number" id="baseMinutes" min="1" max="1440" style="width:100%;background:#131c2e;border:1px solid #334155;color:#fff;padding:0.4rem;border-radius:0.3rem" />
                            </div>
                            <div>
                                <label style="font-size:0.75rem;color:#94a3b8">Curfew Start:</label>
                                <input type="time" id="baseStart" style="width:100%;background:#131c2e;border:1px solid #334155;color:#fff;padding:0.4rem;border-radius:0.3rem" />
                            </div>
                            <div>
                                <label style="font-size:0.75rem;color:#94a3b8">Curfew End:</label>
                                <input type="time" id="baseEnd" style="width:100%;background:#131c2e;border:1px solid #334155;color:#fff;padding:0.4rem;border-radius:0.3rem" />
                            </div>
                        </div>
                    </div>

                    <!-- Preset Toolbar -->
                    <div style="display:flex;gap:0.4rem;flex-wrap:wrap;margin-bottom:1rem">
                        <button type="button" class="btn btn-secondary" style="font-size:0.75rem;padding:0.3rem 0.6rem" onclick="applySchedulePreset('school')">🎒 School Days (Mon–Fri 90m, 15:00–20:00)</button>
                        <button type="button" class="btn btn-secondary" style="font-size:0.75rem;padding:0.3rem 0.6rem" onclick="applySchedulePreset('weekend')">🎉 Weekend (Sat–Sun 240m, 08:00–22:00)</button>
                        <button type="button" class="btn btn-secondary" style="font-size:0.75rem;padding:0.3rem 0.6rem" onclick="applySchedulePreset('reset')">🔄 Reset All to Baseline</button>
                    </div>

                    <!-- 7-Day Matrix Table -->
                    <!-- 7-Day Matrix Table -->
                    <div class="table-responsive" style="border:1px solid #1e293b;border-radius:0.5rem;margin-bottom:1.25rem">
                        <table style="width:100%;font-size:0.8rem;min-width:440px">
                            <thead>
                                <tr style="background:#090d16">
                                    <th style="padding:0.5rem;text-align:left">Day</th>
                                    <th style="padding:0.5rem;text-align:center">Custom?</th>
                                    <th style="padding:0.5rem;text-align:left">Minutes</th>
                                    <th style="padding:0.5rem;text-align:left">Curfew Window</th>
                                </tr>
                            </thead>
                            <tbody id="schDaysTableBody">
                            </tbody>
                        </table>
                    </div>
                    <!-- Single-Day Schedule Exceptions Section -->
                    <div style="border-top:1px solid #1e293b;padding-top:1rem;margin-top:1rem;margin-bottom:1.25rem">
                        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.6rem">
                            <span style="font-size:0.9rem;font-weight:700;color:#f472b6">📅 Upcoming Schedule Exceptions (Pengecualian Khusus)</span>
                        </div>
                        <div class="table-responsive" style="border:1px solid #1e293b;border-radius:0.5rem;margin-bottom:0.75rem">
                            <table style="width:100%;font-size:0.8rem;min-width:380px">
                                <thead>
                                    <tr style="background:#090d16">
                                        <th style="padding:0.4rem;text-align:left">Date</th>
                                        <th style="padding:0.4rem;text-align:left">Minutes</th>
                                        <th style="padding:0.4rem;text-align:left">Curfew Window</th>
                                        <th style="padding:0.4rem;text-align:center">Action</th>
                                    </tr>
                                </thead>
                                <tbody id="schExceptionsTableBody">
                                    <tr><td colspan="4" style="text-align:center;padding:0.5rem;color:#64748b">No upcoming exceptions</td></tr>
                                </tbody>
                            </table>
                        </div>

                        <!-- Add Exception Form -->
                        <div style="display:flex;gap:0.4rem;flex-wrap:wrap;align-items:center;background:#090d16;padding:0.5rem;border-radius:0.4rem;border:1px solid #1e293b">
                            <input type="date" id="newExcDate" style="background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem;font-size:0.75rem" />
                            <input type="number" id="newExcMinutes" placeholder="Minutes" value="120" style="width:70px;background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem;font-size:0.75rem" />
                            <input type="time" id="newExcStart" value="08:00" style="background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem;font-size:0.75rem" />
                            <input type="time" id="newExcEnd" value="22:00" style="background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem;font-size:0.75rem" />
                            <button type="button" class="btn btn-secondary" style="font-size:0.75rem;padding:0.25rem 0.6rem" onclick="addScheduleException()">➕ Add Exception</button>
                        </div>
                    </div>

                    <div id="schFeedback" style="display:none;padding:0.5rem;border-radius:0.4rem;font-size:0.8rem;margin-bottom:0.75rem"></div>

                    <div style="display:flex;justify-content:flex-end;gap:0.5rem">
                        <button type="button" class="btn btn-secondary" onclick="closeScheduleModal()">Cancel</button>
                        <button type="button" class="btn btn-primary" id="btnSaveSchedule" onclick="saveScheduleModal()">💾 Save Schedule</button>
                    </div>
                </div>
            </div>

            <!-- ═══ UPDATE MODAL ═══ -->
            <div id="updateModal" class="modal-backdrop">
                <div class="modal-box" style="max-width: 520px">
                    <h3 style="color:#38bdf8;margin-bottom:0.75rem">🚀 Software Update</h3>
                    <div id="updateModalBody">
                        <p style="font-size:0.85rem;color:#cbd5e1;margin-bottom:0.75rem">
                            Checking for updates on GitHub Releases...
                        </p>
                    </div>
                    <div style="display:flex;justify-content:flex-end;gap:0.5rem;margin-top:1rem">
                        <button class="btn btn-secondary" onclick="closeUpdateModal()">Close</button>
                        <button class="btn btn-success" id="btnApplyUpdate" style="display:none" onclick="applyAppUpdate()">⚡ Download &amp; Install Now</button>
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
                let currentCaptureId = null;
                let currentCaptureUrl = null;
                let watchModeActive = false;
                let watchIntervalId = null;
                let watchHeartbeatId = null;
                let activeSessionUsernames = [];

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
                        loadLatestCapture();
                        if (data.managedUsers) renderManagedUsers(data.managedUsers);
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
                    activeSessionUsernames = sessions.filter(s => s.isRestricted && !s.isLocked).map(s => s.username);
                    let html = '<div class="table-responsive"><table><thead><tr><th style="min-width:110px">User</th><th style="min-width:160px">Current Activity</th><th style="min-width:90px">Remaining</th><th style="min-width:90px">Curfew</th><th style="min-width:180px">Quick Actions</th></tr></thead><tbody>';
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
                                        <button class="btn btn-primary" style="padding:0.25rem 0.5rem" onclick="captureSessionScreen('${s.username}')">📸 Screen</button>
                                        <button class="btn btn-secondary" style="padding:0.25rem 0.5rem" onclick="openScheduleModal(${s.userId}, '${s.username}')">📅 Schedule</button>
                                        <button class="btn btn-warning" style="padding:0.25rem 0.5rem" onclick="openLockModal(${s.sessionId}, '${s.username}')">🔒 Lock</button>
                                        <button class="btn btn-success" style="padding:0.25rem 0.5rem" onclick="grantTime(${s.userId}, 15)">+15m</button>
                                        <button class="btn btn-success" style="padding:0.25rem 0.5rem" onclick="grantTime(${s.userId}, 30)">+30m</button>
                                        <button class="btn btn-danger" style="padding:0.25rem 0.5rem" onclick="forceLogoff(${s.sessionId})">Logoff</button>
                                </td>
                            </tr>`;
                        }
                    }
                    html += '</tbody></table></div>';
                    c.innerHTML = html;
                }

                function renderRequests(requests) {
                    const c = document.getElementById('requestsContainer');
                    if (!requests || requests.length === 0) {
                        c.innerHTML = '<div class="empty">No requests submitted today.</div>';
                        return;
                    }
                    let html = '<div class="table-responsive"><table><thead><tr><th style="min-width:100px">User</th><th style="min-width:60px">Req</th><th>Reason</th><th style="min-width:140px">Action</th></tr></thead><tbody>';
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
                    html += '</tbody></table></div>';
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
                    let html = '<div class="table-responsive"><table><thead><tr><th style="min-width:90px">User</th><th style="min-width:140px">Process / Game</th><th style="min-width:180px">Window Title Summary</th><th style="min-width:110px">Share</th><th style="min-width:80px">Duration</th></tr></thead><tbody>';
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
                    html += '</tbody></table></div>';
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

                async function captureSessionScreen(username) {
                    const btn = document.getElementById('btnSnapScreen');
                    if (btn) btn.textContent = '⏳ Snapping...';
                    try {
                        const res = await fetch('/api/admin/capture/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username, mode: 'single' })
                        });
                        const data = await res.json();
                        if (!res.ok) {
                            alert(data.error || 'Failed to request screen capture.');
                            return;
                        }
                        setTimeout(loadLatestCapture, 2000);
                        setTimeout(loadLatestCapture, 4500);
                        setTimeout(loadLatestCapture, 7000);
                    } catch (e) {
                        console.error('Error requesting capture:', e);
                    } finally {
                        if (btn) btn.textContent = '📸 Capture Now';
                    }
                }

                function captureCurrentScreen() {
                    let target = currentFilter.user;
                    if (!target || target === 'all') {
                        target = activeSessionUsernames.length > 0 ? activeSessionUsernames[0] : '';
                    }
                    if (!target) {
                        alert('No active restricted user found to capture.');
                        return;
                    }
                    captureSessionScreen(target);
                }

                async function toggleWatchMode() {
                    let target = currentFilter.user;
                    if (!target || target === 'all') {
                        target = activeSessionUsernames.length > 0 ? activeSessionUsernames[0] : '';
                    }
                    if (!target) {
                        alert('No active restricted user found for watch mode.');
                        return;
                    }

                    watchModeActive = !watchModeActive;
                    const badge = document.getElementById('watchBadge');
                    const btn = document.getElementById('btnToggleWatch');

                    if (watchModeActive) {
                        btn.classList.remove('btn-secondary');
                        btn.classList.add('btn-danger');
                        btn.textContent = '⏹ Stop Watch';
                        if (badge) badge.style.display = 'inline-flex';

                        await fetch('/api/admin/capture/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: target, mode: 'watch', enableWatch: true })
                        });

                        loadLatestCapture();
                        watchIntervalId = setInterval(loadLatestCapture, 10000);
                        watchHeartbeatId = setInterval(() => {
                            fetch(`/api/admin/capture/heartbeat?user=${encodeURIComponent(target)}`, { method: 'POST' });
                        }, 60000);
                    } else {
                        btn.classList.remove('btn-danger');
                        btn.classList.add('btn-secondary');
                        btn.textContent = '🎥 Watch Mode (10s)';
                        if (badge) badge.style.display = 'none';

                        if (watchIntervalId) clearInterval(watchIntervalId);
                        if (watchHeartbeatId) clearInterval(watchHeartbeatId);

                        await fetch('/api/admin/capture/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: target, mode: 'watch', enableWatch: false })
                        });
                    }
                }

                async function loadLatestCapture() {
                    let target = currentFilter.user;
                    if (!target || target === 'all') {
                        target = activeSessionUsernames.length > 0 ? activeSessionUsernames[0] : 'all';
                    }

                    try {
                        const res = await fetch(`/api/admin/captures/latest?user=${encodeURIComponent(target)}`);
                        if (res.status === 401) return;
                        const data = await res.json();

                        const img = document.getElementById('captureImg');
                        const ph = document.getElementById('capturePlaceholder');
                        const bar = document.getElementById('captureMetaBar');
                        const metaUserTime = document.getElementById('captureMetaUserTime');
                        const metaDetails = document.getElementById('captureMetaDetails');

                        if (data && data.capture) {
                            const c = data.capture;
                            currentCaptureId = c.id;
                            currentCaptureUrl = c.url;

                            img.src = `${c.url}?t=${Date.now()}`;
                            img.style.display = 'block';
                            ph.style.display = 'none';
                            bar.style.display = 'flex';

                            metaUserTime.innerHTML = `<strong>👤 ${c.username}</strong> &bull; ⏰ ${c.timestamp}`;
                            const kb = Math.round(c.fileSizeBytes / 1024);
                            metaDetails.textContent = `${c.width}×${c.height} • ${kb} KB • ${c.triggerType}`;
                        }
                        loadRecentCaptures(target);
                    } catch (e) {
                        console.error('Error loading latest capture:', e);
                    }
                }

                async function loadRecentCaptures(targetUser) {
                    try {
                        const res = await fetch(`/api/admin/captures/recent?user=${encodeURIComponent(targetUser || currentFilter.user)}&limit=10`);
                        if (res.status === 401) return;
                        const data = await res.json();

                        const strip = document.getElementById('captureThumbnails');
                        const countBadge = document.getElementById('captureCountBadge');
                        if (!strip) return;

                        if (!data.captures || data.captures.length === 0) {
                            strip.innerHTML = '<span style="font-size:0.75rem;color:#64748b;font-style:italic">No captures yet</span>';
                            if (countBadge) countBadge.textContent = '0 captures';
                            return;
                        }

                        if (countBadge) countBadge.textContent = `${data.captures.length} recent`;

                        let html = '';
                        for (const item of data.captures) {
                            const isActive = item.id === currentCaptureId ? 'active' : '';
                            html += `
                                <div style="display:flex;flex-direction:column;align-items:center;gap:2px">
                                    <img src="${item.url}" class="cap-thumb ${isActive}" title="${item.username} @ ${item.timestamp} (${item.width}x${item.height})" onclick="selectCapture(${item.id}, '${item.url}', '${item.username} &bull; ⏰ ${item.timestamp}', '${item.width}×${item.height} • ${Math.round(item.fileSizeBytes/1024)} KB')" />
                                    <span style="font-size:0.65rem;color:#64748b">${item.timestamp}</span>
                                </div>`;
                        }
                        strip.innerHTML = html;
                    } catch (e) {
                        console.error('Error loading recent captures:', e);
                    }
                }

                function selectCapture(id, url, userTime, details) {
                    currentCaptureId = id;
                    currentCaptureUrl = url;
                    const img = document.getElementById('captureImg');
                    img.src = `${url}?t=${Date.now()}`;
                    document.getElementById('captureMetaUserTime').innerHTML = userTime;
                    document.getElementById('captureMetaDetails').textContent = details;

                    // Update active thumbnail border
                    document.querySelectorAll('.cap-thumb').forEach(el => el.classList.remove('active'));
                }

                function openCaptureLightbox() {
                    if (!currentCaptureUrl) return;
                    document.getElementById('lightboxImg').src = currentCaptureUrl;
                    document.getElementById('lightboxModal').style.display = 'flex';
                }

                function closeCaptureLightbox() {
                    document.getElementById('lightboxModal').style.display = 'none';
                }

                async function sendCurrentCaptureToTelegram() {
                    if (!currentCaptureId) {
                        alert('No screen capture loaded to send.');
                        return;
                    }
                    const btn = document.getElementById('btnSendTelegram');
                    btn.textContent = '⏳ Sending...';
                    try {
                        const res = await fetch(`/api/admin/capture/telegram?id=${currentCaptureId}`, { method: 'POST' });
                        const data = await res.json();
                        if (res.ok && data.success) {
                            alert('📸 Screen capture successfully sent to Telegram!');
                        } else {
                            alert(data.error || 'Failed to send capture to Telegram. Ensure Telegram Bot token and Chat ID are configured.');
                        }
                    } catch (e) {
                        console.error('Error sending capture to Telegram:', e);
                    } finally {
                        btn.textContent = '📱 Send to Telegram';
                    }
                }

                function renderManagedUsers(users) {
                    const c = document.getElementById('usersScheduleContainer');
                    if (!users || users.length === 0) {
                        c.innerHTML = '<div class="empty">No restricted child accounts registered yet.</div>';
                        return;
                    }
                    let html = '<div class="table-responsive"><table><thead><tr><th style="min-width:100px">User</th><th style="min-width:110px">Standard Daily Quota</th><th style="min-width:110px">Standard Curfew</th><th style="min-width:130px">Custom Schedule Overrides</th><th style="min-width:130px">Action</th></tr></thead><tbody>';
                    for (const u of users) {
                        const customBadge = u.customCount > 0
                            ? `<span class="badge" style="background:#4c1d95;color:#ddd6fe">${u.customCount} custom days</span>`
                            : `<span class="badge" style="background:#1e293b;color:#94a3b8">All standard</span>`;
                        html += `<tr>
                            <td><strong>${u.username}</strong></td>
                            <td><strong style="color:#38bdf8">${u.baseMinutes} mins</strong></td>
                            <td>${u.baseStart} – ${u.baseEnd}</td>
                            <td>${customBadge}</td>
                            <td>
                                <button class="btn btn-primary" style="padding:0.3rem 0.75rem" onclick="openScheduleModal(${u.id}, '${u.username}')">⚙️ Manage Schedule</button>
                            </td>
                        </tr>`;
                    }
                    html += '</tbody></table></div>';
                    c.innerHTML = html;
                }

                let currentSchDays = [];
                let currentSchExceptions = [];

                async function openScheduleModal(userId, username) {
                    document.getElementById('schModalUserId').value = userId;
                    document.getElementById('schModalUsername').textContent = username;
                    document.getElementById('schFeedback').style.display = 'none';

                    try {
                        const res = await fetch(`/api/admin/schedule?userId=${userId}`);
                        if (!res.ok) { alert('Failed to load schedule'); return; }
                        const data = await res.json();

                        document.getElementById('baseMinutes').value = data.baseDailyMinutes;
                        document.getElementById('baseStart').value = data.baseScheduleStart;
                        document.getElementById('baseEnd').value = data.baseScheduleEnd;

                        currentSchDays = data.days;
                        currentSchExceptions = data.upcomingExceptions || [];
                        renderScheduleDaysTable();
                        renderScheduleExceptionsTable();
                        const tomorrow = new Date(Date.now() + 86400000).toISOString().split('T')[0];
                        const dateInput = document.getElementById('newExcDate');
                        if (dateInput && !dateInput.value) dateInput.value = tomorrow;
                        document.getElementById('scheduleModal').style.display = 'flex';
                    } catch (e) {
                        console.error('Error opening schedule modal:', e);
                    }
                }

                function renderScheduleDaysTable() {
                    const tbody = document.getElementById('schDaysTableBody');
                    let html = '';
                    for (let i = 0; i < currentSchDays.length; i++) {
                        const d = currentSchDays[i];
                        const isChecked = d.isCustom ? 'checked' : '';
                        const disabledAttr = d.isCustom ? '' : 'disabled';
                        const opacityStyle = d.isCustom ? 'opacity:1' : 'opacity:0.45';

                        html += `
                            <tr style="border-bottom:1px solid #1e293b">
                                <td style="padding:0.5rem;font-weight:600">${d.dayName}</td>
                                <td style="padding:0.5rem;text-align:center">
                                    <input type="checkbox" id="chk_${i}" ${isChecked} onchange="toggleDayCustom(${i}, this.checked)" />
                                </td>
                                <td style="padding:0.5rem">
                                    <input type="number" id="mins_${i}" value="${d.dailyMinutes}" min="1" max="1440" style="width:75px;background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem;${opacityStyle}" ${disabledAttr} onchange="currentSchDays[${i}].dailyMinutes = parseInt(this.value) || 120" />
                                </td>
                                <td style="padding:0.5rem">
                                    <div style="display:flex;gap:0.3rem;align-items:center;${opacityStyle}">
                                        <input type="time" id="start_${i}" value="${d.scheduleStart}" style="background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem" ${disabledAttr} onchange="currentSchDays[${i}].scheduleStart = this.value" />
                                        <span style="color:#64748b">–</span>
                                        <input type="time" id="end_${i}" value="${d.scheduleEnd}" style="background:#131c2e;border:1px solid #334155;color:#fff;padding:0.25rem 0.4rem;border-radius:0.3rem" ${disabledAttr} onchange="currentSchDays[${i}].scheduleEnd = this.value" />
                                    </div>
                                </td>
                            </tr>`;
                    }
                    tbody.innerHTML = html;
                }

                function toggleDayCustom(index, isCustom) {
                    currentSchDays[index].isCustom = isCustom;
                    if (!isCustom) {
                        currentSchDays[index].dailyMinutes = parseInt(document.getElementById('baseMinutes').value) || 120;
                        currentSchDays[index].scheduleStart = document.getElementById('baseStart').value || '08:00';
                        currentSchDays[index].scheduleEnd = document.getElementById('baseEnd').value || '22:00';
                    }
                    renderScheduleDaysTable();
                }
                function renderScheduleExceptionsTable() {
                    const tbody = document.getElementById('schExceptionsTableBody');
                    if (!currentSchExceptions || currentSchExceptions.length === 0) {
                        tbody.innerHTML = '<tr><td colspan="4" style="text-align:center;padding:0.5rem;color:#64748b">No upcoming exceptions</td></tr>';
                        return;
                    }
                    let html = '';
                    for (const e of currentSchExceptions) {
                        html += `
                            <tr style="border-bottom:1px solid #1e293b">
                                <td style="padding:0.4rem;font-weight:600;color:#f472b6">${e.date}</td>
                                <td style="padding:0.4rem">${e.dailyMinutes}m</td>
                                <td style="padding:0.4rem">${e.scheduleStart} – ${e.scheduleEnd}</td>
                                <td style="padding:0.4rem;text-align:center">
                                    <button type="button" class="btn btn-secondary" style="font-size:0.7rem;padding:0.15rem 0.4rem;color:#ef4444" onclick="deleteScheduleException(${e.id})">🗑️ Delete</button>
                                </td>
                            </tr>`;
                    }
                    tbody.innerHTML = html;
                }

                async function addScheduleException() {
                    const userId = parseInt(document.getElementById('schModalUserId').value);
                    const date = document.getElementById('newExcDate').value;
                    const mins = parseInt(document.getElementById('newExcMinutes').value) || 120;
                    const start = document.getElementById('newExcStart').value || '08:00';
                    const end = document.getElementById('newExcEnd').value || '22:00';
                    if (!date) { alert('Please select a date.'); return; }

                    try {
                        const res = await fetch('/api/admin/schedule-exception', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ userId, date, dailyMinutes: mins, scheduleStart: start, scheduleEnd: end })
                        });
                        if (res.ok) {
                            openScheduleModal(userId, document.getElementById('schModalUsername').textContent);
                        } else {
                            const err = await res.json();
                            alert(err.error || 'Failed to add exception');
                        }
                    } catch (e) {
                        alert('Network error');
                    }
                }

                async function deleteScheduleException(id) {
                    if (!confirm('Are you sure you want to delete this schedule exception?')) return;
                    const userId = parseInt(document.getElementById('schModalUserId').value);
                    try {
                        const res = await fetch(`/api/admin/schedule-exception?id=${id}`, { method: 'DELETE' });
                        if (res.ok) {
                            openScheduleModal(userId, document.getElementById('schModalUsername').textContent);
                        } else {
                            alert('Failed to delete exception');
                        }
                    } catch (e) {
                        alert('Network error');
                    }
                }

                function applySchedulePreset(type) {
                    const baseM = parseInt(document.getElementById('baseMinutes').value) || 120;
                    const baseS = document.getElementById('baseStart').value || '08:00';
                    const baseE = document.getElementById('baseEnd').value || '22:00';

                    for (let i = 0; i < currentSchDays.length; i++) {
                        const d = currentSchDays[i];
                        const dayOfWeek = d.dayOfWeek;
                        if (type === 'school') {
                            if (dayOfWeek >= 1 && dayOfWeek <= 5) {
                                d.isCustom = true;
                                d.dailyMinutes = 90;
                                d.scheduleStart = '15:00';
                                d.scheduleEnd = '20:00';
                            }
                        } else if (type === 'weekend') {
                            if (dayOfWeek === 0 || dayOfWeek === 6) {
                                d.isCustom = true;
                                d.dailyMinutes = 240;
                                d.scheduleStart = '08:00';
                                d.scheduleEnd = '22:00';
                            }
                        } else if (type === 'reset') {
                            d.isCustom = false;
                            d.dailyMinutes = baseM;
                            d.scheduleStart = baseS;
                            d.scheduleEnd = baseE;
                        }
                    }
                    renderScheduleDaysTable();
                }

                function closeScheduleModal() {
                    document.getElementById('scheduleModal').style.display = 'none';
                }

                async function saveScheduleModal() {
                    const userId = parseInt(document.getElementById('schModalUserId').value);
                    const baseM = parseInt(document.getElementById('baseMinutes').value);
                    const baseS = document.getElementById('baseStart').value;
                    const baseE = document.getElementById('baseEnd').value;

                    const fb = document.getElementById('schFeedback');
                    const btn = document.getElementById('btnSaveSchedule');

                    if (!baseM || baseM < 1 || baseM > 1440) {
                        fb.style.display = 'block';
                        fb.style.background = '#7f1d1d';
                        fb.style.color = '#fecaca';
                        fb.textContent = 'Menit harian standar harus antara 1 dan 1440.';
                        return;
                    }
                    if (!baseS || !baseE || baseS >= baseE) {
                        fb.style.display = 'block';
                        fb.style.background = '#7f1d1d';
                        fb.style.color = '#fecaca';
                        fb.textContent = 'Jam batas standar tidak valid (Jam mulai harus sebelum jam selesai).';
                        return;
                    }

                    btn.textContent = '⏳ Saving...';
                    btn.disabled = true;

                    try {
                        const payload = {
                            userId: userId,
                            baseDailyMinutes: baseM,
                            baseScheduleStart: baseS,
                            baseScheduleEnd: baseE,
                            days: currentSchDays
                        };

                        const res = await fetch('/api/admin/schedule', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(payload)
                        });

                        const data = await res.json();
                        if (res.ok && data.success) {
                            closeScheduleModal();
                            loadDashboard();
                        } else {
                            fb.style.display = 'block';
                            fb.style.background = '#7f1d1d';
                            fb.style.color = '#fecaca';
                            fb.textContent = data.error || 'Failed to save schedule.';
                        }
                    } catch (e) {
                        fb.style.display = 'block';
                        fb.style.background = '#7f1d1d';
                        fb.style.color = '#fecaca';
                        fb.textContent = 'Error: ' + e.message;
                    } finally {
                        btn.textContent = '💾 Save Schedule';
                        btn.disabled = false;
                    }
                }

                let pendingUpdateDownloadUrl = null;

                async function checkAppUpdates() {
                    try {
                        const res = await fetch('/api/admin/update/check');
                        if (!res.ok) return;
                        const data = await res.json();
                        document.getElementById('versionBadge').textContent = data.currentVersion || 'v1.1.0';

                        const noticeBtn = document.getElementById('btnUpdateNotice');
                        if (data.hasUpdate) {
                            noticeBtn.style.display = 'inline-block';
                            noticeBtn.textContent = `🚀 Update v${data.latestVersion} Available!`;
                        } else {
                            noticeBtn.style.display = 'none';
                        }
                        openUpdateModal(data);
                    } catch (e) {
                        console.error('Error checking updates:', e);
                    }
                }

                function openUpdateModal(data) {
                    const modal = document.getElementById('updateModal');
                    const body = document.getElementById('updateModalBody');
                    const applyBtn = document.getElementById('btnApplyUpdate');

                    if (!data) {
                        body.innerHTML = '<p style="color:#94a3b8">Memeriksa pembaruan...</p>';
                        applyBtn.style.display = 'none';
                        modal.style.display = 'flex';
                        checkAppUpdates();
                        return;
                    }

                    if (data.error) {
                        body.innerHTML = `<div style="padding:0.75rem;background:#7f1d1d;color:#fecaca;border-radius:0.4rem">Gagal memeriksa pembaruan: ${data.error}</div>`;
                        applyBtn.style.display = 'none';
                    } else if (data.hasUpdate) {
                        pendingUpdateDownloadUrl = data.downloadUrl;
                        const lines = (data.releaseNotes || '').split('\n').filter(l => l.trim().length > 0 && !l.startsWith('##'));
                        let formattedNotes = '';
                        for (const line of lines) {
                            const t = line.trim();
                            if (t.startsWith('* ') || t.startsWith('- ')) {
                                formattedNotes += `<div style="margin-bottom:0.25rem">&bull; ${t.substring(2)}</div>`;
                            } else if (t.startsWith('**Full Changelog**')) {
                                continue;
                            } else {
                                formattedNotes += `<div style="margin-bottom:0.25rem">${t}</div>`;
                            }
                        }
                        if (!formattedNotes) formattedNotes = '<div>Perbaikan performa dan fitur baru.</div>';

                        pendingUpdateDownloadUrl = data.downloadUrl;
                        body.innerHTML = `
                            <div style="margin-bottom:0.75rem">
                                <span class="badge" style="background:#15803d;color:#dcfce7">Versi Baru Tersedia!</span>
                                <div style="font-size:1.1rem;font-weight:700;color:#38bdf8;margin-top:0.4rem">${data.releaseTitle || 'v' + data.latestVersion}</div>
                                <div style="font-size:0.8rem;color:#94a3b8">Versi saat ini: ${data.currentVersion} &bull; Rilis baru: v${data.latestVersion}</div>
                            </div>
                            <div style="background:#090d16;border:1px solid #1e293b;border-radius:0.4rem;padding:0.75rem;font-size:0.8rem;color:#cbd5e1;max-height:180px;overflow-y:auto;margin-bottom:0.75rem">${formattedNotes}</div>
                            <p style="font-size:0.8rem;color:#fef08a">⚠️ Pembaruan akan mengunduh file installer dan memuat ulang layanan serta agent secara otomatis.</p>
                        `;
                        applyBtn.style.display = 'inline-block';
                    } else {
                        body.innerHTML = `
                            <div style="padding:0.75rem;background:#064e3b;color:#a7f3d0;border-radius:0.4rem;margin-bottom:0.5rem">
                                ✅ Aplikasi sudah menggunakan versi terbaru (<strong>${data.currentVersion}</strong>).
                            </div>
                        `;
                        applyBtn.style.display = 'none';
                    }
                    modal.style.display = 'flex';
                }

                function closeUpdateModal() {
                    document.getElementById('updateModal').style.display = 'none';
                }

                async function applyAppUpdate() {
                    if (!pendingUpdateDownloadUrl) {
                        alert('URL rilis tidak ditemukan.');
                        return;
                    }
                    const applyBtn = document.getElementById('btnApplyUpdate');
                    applyBtn.disabled = true;
                    applyBtn.textContent = '⏳ Mengunduh & Memasang...';

                    try {
                        const res = await fetch('/api/admin/update/apply', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ downloadUrl: pendingUpdateDownloadUrl })
                        });
                        const data = await res.json();
                        alert(data.message || 'Pembaruan sedang berjalan.');
                        closeUpdateModal();
                    } catch (e) {
                        alert('Gagal menerapkan update: ' + e.message);
                    } finally {
                        applyBtn.disabled = false;
                        applyBtn.textContent = '⚡ Download & Install Now';
                    }
                }

                // Initial load and periodic polling every 10s
                document.getElementById('dateInput').value = currentFilter.date;
                loadDashboard();
                setInterval(loadDashboard, 10000);
            </script>
        </body>
        </html>
        """.Replace("__APP_VERSION__", AppVersion.DisplayName);
    }
}
