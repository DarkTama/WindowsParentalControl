namespace ParentalControl.Service.Web.Pages;

public static class RequestPage
{
    public sealed record ScheduleDayView(
        DayOfWeek Day,
        string DayName,
        int Minutes,
        string TimeWindow,
        bool IsCustom,
        bool IsToday
    );

    public static string Render(
        string username,
        int remainingMinutes,
        int remainingSeconds,
        string curfew,
        bool isOffline,
        int requestsSubmittedToday,
        int maxDailyRequests,
        bool hasPendingRequest,
        string? latestStatus,
        bool isAdminTesting = false,
        List<string>? availableUsers = null,
        bool isSessionActive = true,
        bool isSessionLocked = false,
        List<ScheduleDayView>? weeklySchedule = null,
        string? defaultScheduleSummary = null)
    {
        var sessionStatusBadge = "";
        if (!isSessionActive)
        {
            sessionStatusBadge = """<span class="session-badge badge-offline">⏹ Sesi Tidak Aktif (Waktu Dijeda)</span>""";
        }
        else if (isSessionLocked)
        {
            sessionStatusBadge = """<span class="session-badge badge-locked">⏸ Sesi Terkunci (Waktu Dijeda)</span>""";
        }
        else
        {
            sessionStatusBadge = """<span class="session-badge badge-active">▶ Sesi Aktif</span>""";
        }
        var adminBanner = "";
        if (isAdminTesting)
        {
            var switchLinks = "";
            if (availableUsers != null && availableUsers.Count > 1)
            {
                var links = string.Join(" | ", availableUsers.Select(u => $"<a href=\"/request?user={u}\" style=\"color:#38bdf8;text-decoration:underline\">{u}</a>"));
                switchLinks = $"<div style=\"margin-top:0.4rem;font-size:0.8rem;opacity:0.9\">Ganti Akun Pengujian: {links}</div>";
            }
            adminBanner = $"""<div class="banner banner-info" style="border-left:4px solid #38bdf8">🔧 <strong>Mode Uji Coba Administrator</strong>: Menampilkan status akun terbatas <strong>{username}</strong>.{switchLinks}</div>""";
        }

        var offlineAlert = isOffline
            ? """<div class="banner banner-error">⚠️ Komputer sedang offline. Permintaan hanya dapat dikirim saat terhubung ke internet.</div>"""
            : "";

        var statusLabel = latestStatus switch
        {
            "PENDING" => "Menunggu Persetujuan",
            "APPROVED" => "Disetujui",
            "DECLINED" => "Ditolak",
            _ => latestStatus ?? ""
        };

        var requestLimitAlert = "";
        var disableForm = isOffline;

        if (maxDailyRequests <= 0)
        {
            requestLimitAlert = """<div class="banner banner-error">⚠️ Permintaan waktu tambahan saat ini dinonaktifkan oleh administrator.</div>""";
            disableForm = true;
        }
        else if (hasPendingRequest)
        {
            requestLimitAlert = """<div class="banner banner-info">ℹ️ Anda masih memiliki permintaan yang sedang menunggu keputusan administrator (Status: <strong>Menunggu Persetujuan</strong>). Harap tunggu keputusan sebelum mengirim lagi.</div>""";
            disableForm = true;
        }
        else if (requestsSubmittedToday >= maxDailyRequests)
        {
            requestLimitAlert = $"""<div class="banner banner-info">ℹ️ Anda telah mencapai batas maksimal ({maxDailyRequests}) permintaan untuk hari ini (Status terakhir: <strong>{statusLabel}</strong>).</div>""";
            disableForm = true;
        }
        else if (requestsSubmittedToday > 0)
        {
            requestLimitAlert = $"""<div class="banner banner-info">ℹ️ Anda telah menggunakan {requestsSubmittedToday} dari {maxDailyRequests} permintaan hari ini (Status terakhir: <strong>{statusLabel}</strong>).</div>""";
        }

        var disabledAttr = disableForm ? "disabled" : "";
        var scheduleSection = "";
        if (weeklySchedule != null && weeklySchedule.Count > 0)
        {
            var dayCards = new System.Text.StringBuilder();
            foreach (var d in weeklySchedule)
            {
                var todayClass = d.IsToday ? "today-card" : "";
                var todayBadge = d.IsToday ? """<span class="badge-today">Hari Ini</span>""" : "";
                var customBadge = d.IsCustom ? """<span class="badge-custom">Khusus</span>""" : """<span class="badge-std">Standar</span>""";
                dayCards.Append($$"""
                    <div class="schedule-day-card {{todayClass}}">
                        <div class="day-name">{{d.DayName}}</div>
                        <div style="display:flex;gap:0.25rem;justify-content:center;flex-wrap:wrap">
                            {{todayBadge}}
                            {{customBadge}}
                        </div>
                        <div class="day-minutes">{{d.Minutes}}m</div>
                        <div class="day-hours">{{d.TimeWindow}}</div>
                    </div>
                """);
            }

            var defText = !string.IsNullOrWhiteSpace(defaultScheduleSummary)
                ? $"Standar: {defaultScheduleSummary}"
                : "Standar: 120m (08:00–22:00)";

            scheduleSection = $$"""
                <div class="schedule-panel">
                    <div class="schedule-header">
                        <span class="schedule-title">📅 Jadwal Main Mingguan</span>
                        <span class="schedule-baseline">{{defText}}</span>
                    </div>
                    <div class="schedule-grid">
                        {{dayCards}}
                    </div>
                </div>
            """;
        }

        return $$"""
        <!DOCTYPE html>
        <html lang="id">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control — Minta Tambahan Waktu</title>
            <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                body {
                    background: radial-gradient(circle at 50% 0%, #172554 0%, #090d16 100%);
                    color: #f1f5f9;
                    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Inter", sans-serif;
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.5rem;
                }
                .card {
                    background: rgba(19, 28, 49, 0.95);
                    border: 1px solid rgba(56, 189, 248, 0.2);
                    backdrop-filter: blur(16px);
                    border-radius: 1.25rem;
                    padding: 2.25rem;
                    max-width: 520px;
                    width: 100%;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.7), 0 0 30px rgba(56, 189, 248, 0.1);
                }
                .header-row {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    margin-bottom: 1.5rem;
                    flex-wrap: wrap;
                    gap: 0.5rem;
                }
                h1 {
                    font-size: 1.4rem;
                    font-weight: 700;
                    color: #38bdf8;
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    letter-spacing: -0.02em;
                }
                .user-badge {
                    background: #1e293b;
                    border: 1px solid #334155;
                    color: #cbd5e1;
                    padding: 0.35rem 0.85rem;
                    border-radius: 9999px;
                    font-size: 0.85rem;
                    font-weight: 500;
                }
                .session-badge {
                    padding: 0.35rem 0.85rem;
                    border-radius: 9999px;
                    font-size: 0.8rem;
                    font-weight: 600;
                    display: inline-flex;
                    align-items: center;
                    gap: 0.35rem;
                }
                .badge-active { background: #064e3b; color: #a7f3d0; border: 1px solid #059669; }
                .badge-locked { background: #451a03; color: #fde68a; border: 1px solid #d97706; }
                .badge-offline { background: #1e293b; color: #94a3b8; border: 1px solid #475569; }
                .timer-panel {
                    background: #0b1120;
                    border: 1px solid #1e293b;
                    border-radius: 1rem;
                    padding: 1.25rem;
                    text-align: center;
                    margin-bottom: 1.5rem;
                    position: relative;
                    overflow: hidden;
                }
                .timer-label {
                    font-size: 0.75rem;
                    text-transform: uppercase;
                    letter-spacing: 0.1em;
                    color: #94a3b8;
                    margin-bottom: 0.35rem;
                    font-weight: 600;
                }
                .timer-value {
                    font-size: 2.5rem;
                    font-weight: 800;
                    color: #38bdf8;
                    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
                    font-variant-numeric: tabular-nums;
                    line-height: 1.1;
                }
                .timer-curfew {
                    font-size: 0.8rem;
                    color: #64748b;
                    margin-top: 0.5rem;
                }
                .banner {
                    padding: 0.85rem 1.1rem;
                    border-radius: 0.65rem;
                    margin-bottom: 1.5rem;
                    font-size: 0.875rem;
                    line-height: 1.5;
                }
                .banner-error { background: #450a0a; color: #fecaca; border: 1px solid #991b1b; }
                .banner-info { background: #172554; color: #bfdbfe; border: 1px solid #1d4ed8; }
                .banner-success { background: #064e3b; color: #a7f3d0; border: 1px solid #059669; }

                label.field-label {
                    display: block;
                    font-size: 0.875rem;
                    color: #cbd5e1;
                    margin-bottom: 0.65rem;
                    font-weight: 600;
                }
                .radio-group {
                    display: grid;
                    grid-template-columns: repeat(3, 1fr);
                    gap: 0.75rem;
                    margin-bottom: 1.5rem;
                }
                .radio-btn input { display: none; }
                .radio-btn label {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    background: #0f172a;
                    padding: 0.85rem 0.5rem;
                    border-radius: 0.65rem;
                    border: 2px solid #22314e;
                    cursor: pointer;
                    transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
                    color: #e2e8f0;
                    font-size: 0.95rem;
                    font-weight: 600;
                    user-select: none;
                }
                .radio-btn input:checked + label {
                    border-color: #38bdf8;
                    background: #0369a1;
                    color: #ffffff;
                    box-shadow: 0 0 12px rgba(56, 189, 248, 0.35);
                }
                .radio-btn input:disabled + label {
                    opacity: 0.5;
                    cursor: not-allowed;
                }
                .form-group {
                    margin-bottom: 1.5rem;
                }
                textarea {
                    width: 100%;
                    background: #0b1120;
                    border: 2px solid #22314e;
                    border-radius: 0.65rem;
                    padding: 0.85rem;
                    color: #f8fafc;
                    font-size: 0.9rem;
                    font-family: inherit;
                    resize: vertical;
                    min-height: 85px;
                    transition: border-color 0.2s;
                }
                textarea:focus {
                    outline: none;
                    border-color: #38bdf8;
                    box-shadow: 0 0 0 3px rgba(56, 189, 248, 0.2);
                }
                textarea:disabled {
                    opacity: 0.5;
                    cursor: not-allowed;
                }
                button.submit-btn {
                    width: 100%;
                    background: #0284c7;
                    color: white;
                    border: none;
                    padding: 0.95rem;
                    border-radius: 0.65rem;
                    font-size: 1rem;
                    font-weight: 700;
                    cursor: pointer;
                    transition: all 0.2s;
                    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.2);
                }
                button.submit-btn:hover:not(:disabled) {
                    background: #0369a1;
                    transform: translateY(-1px);
                }
                button.submit-btn:active:not(:disabled) {
                    transform: translateY(0);
                }
                button.submit-btn:disabled {
                    opacity: 0.45;
                    cursor: not-allowed;
                }
                #feedback { margin-top: 1rem; display: none; }
            </style>
                /* Jadwal Main Card & Grid */
                .schedule-panel {
                    background: #0f172a;
                    border: 1px solid #1e293b;
                    border-radius: 0.85rem;
                    padding: 1rem;
                    margin-bottom: 1.5rem;
                }
                .schedule-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    margin-bottom: 0.75rem;
                    flex-wrap: wrap;
                    gap: 0.5rem;
                }
                .schedule-title {
                    font-size: 0.95rem;
                    font-weight: 700;
                    color: #38bdf8;
                    display: flex;
                    align-items: center;
                    gap: 0.4rem;
                }
                .schedule-baseline {
                    font-size: 0.75rem;
                    color: #94a3b8;
                    background: #1e293b;
                    padding: 0.2rem 0.5rem;
                    border-radius: 0.35rem;
                }
                .schedule-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(88px, 1fr));
                    gap: 0.5rem;
                }
                .schedule-day-card {
                    background: #0b1120;
                    border: 1px solid #1e293b;
                    border-radius: 0.5rem;
                    padding: 0.55rem 0.45rem;
                    text-align: center;
                    display: flex;
                    flex-direction: column;
                    gap: 0.25rem;
                    transition: all 0.2s;
                }
                .schedule-day-card.today-card {
                    border-color: #38bdf8;
                    background: rgba(56, 189, 248, 0.08);
                    box-shadow: 0 0 10px rgba(56, 189, 248, 0.15);
                }
                .day-name {
                    font-size: 0.8rem;
                    font-weight: 700;
                    color: #e2e8f0;
                }
                .badge-today {
                    display: inline-block;
                    background: #0284c7;
                    color: #ffffff;
                    font-size: 0.65rem;
                    font-weight: 700;
                    padding: 0.1rem 0.35rem;
                    border-radius: 0.25rem;
                    margin-top: 0.15rem;
                }
                .badge-custom {
                    display: inline-block;
                    background: #1e293b;
                    color: #a78bfa;
                    border: 1px solid #7c3aed;
                    font-size: 0.62rem;
                    font-weight: 600;
                    padding: 0.05rem 0.3rem;
                    border-radius: 0.25rem;
                    margin-top: 0.15rem;
                }
                .badge-std {
                    display: inline-block;
                    background: #1e293b;
                    color: #64748b;
                    font-size: 0.62rem;
                    font-weight: 500;
                    padding: 0.05rem 0.3rem;
                    border-radius: 0.25rem;
                    margin-top: 0.15rem;
                }
                .day-minutes {
                    font-size: 0.85rem;
                    font-weight: 700;
                    color: #38bdf8;
                    font-variant-numeric: tabular-nums;
                }
                .day-hours {
                    font-size: 0.68rem;
                    color: #64748b;
                    font-family: ui-monospace, SFMono-Regular, monospace;
                }
        </head>
        <body>
            <div class="card">
                <div class="header-row">
                    <h1>🎮 Waktu Layar</h1>
                    <div style="display:flex;gap:0.5rem;align-items:center;flex-wrap:wrap">
                        {{sessionStatusBadge}}
                        <div class="user-badge">Akun: <strong>{{username}}</strong></div>
                    </div>
                </div>

                <div class="timer-panel">
                    <div class="timer-label">Sisa Waktu Penggunaan Hari Ini</div>
                    <div class="timer-value" id="countdownTimer">--:--</div>
                    <div class="timer-curfew">Jadwal Jam Malam: <strong>{{curfew}}</strong></div>
                </div>
                {{scheduleSection}}

                {{adminBanner}}
                {{offlineAlert}}
                {{requestLimitAlert}}

                <div id="feedback"></div>

                <form id="requestForm" onsubmit="submitForm(event)">
                    <label class="field-label">Pilih Tambahan Waktu:</label>
                    <div class="radio-group">
                        <div class="radio-btn">
                            <input type="radio" id="m15" name="minutes" value="15" checked {{disabledAttr}}>
                            <label for="m15">+15 Menit</label>
                        </div>
                        <div class="radio-btn">
                            <input type="radio" id="m30" name="minutes" value="30" {{disabledAttr}}>
                            <label for="m30">+30 Menit</label>
                        </div>
                        <div class="radio-btn">
                            <input type="radio" id="m60" name="minutes" value="60" {{disabledAttr}}>
                            <label for="m60">+1 Jam</label>
                        </div>
                    </div>

                    <div class="form-group">
                        <label for="reason" class="field-label">Alasan meminta tambahan waktu:</label>
                        <textarea id="reason" name="reason" placeholder="Contoh: Sedang menyelesaikan tugas sekolah, bermain game bersama teman..." required {{disabledAttr}}></textarea>
                    </div>

                    <button type="submit" class="submit-btn" id="submitBtn" {{disabledAttr}}>Kirim Permintaan ke Administrator</button>
                </form>
            </div>

            <script>
                let totalSecondsRemaining = {{remainingSeconds}};
                const isSessionTicking = {{(isSessionActive && !isSessionLocked ? "true" : "false")}};
                function formatTime(totalSec) {
                    if (totalSec <= 0) return "00:00 (Habis)";
                    const hours = Math.floor(totalSec / 3600);
                    const minutes = Math.floor((totalSec % 3600) / 60);
                    const seconds = totalSec % 60;
                    const mm = String(minutes).padStart(2, '0');
                    const ss = String(seconds).padStart(2, '0');
                    if (hours > 0) {
                        return `${hours}:${mm}:${ss}`;
                    }
                    return `${mm}:${ss}`;
                }

                function tickCountdown() {
                    const timerElem = document.getElementById('countdownTimer');
                    if (!timerElem) return;

                    timerElem.textContent = formatTime(totalSecondsRemaining);
                    document.title = `[${formatTime(totalSecondsRemaining)}] Parental Control`;

                    if (totalSecondsRemaining <= 0) {
                        timerElem.style.color = '#ef4444';
                        return;
                    }
                    if (isSessionTicking) {
                        totalSecondsRemaining--;
                    }
                }

                tickCountdown();
                if (isSessionTicking) {
                    setInterval(tickCountdown, 1000);
                }
                async function submitForm(e) {
                    e.preventDefault();
                    const btn = document.getElementById('submitBtn');
                    const feedback = document.getElementById('feedback');
                    btn.disabled = true;
                    btn.innerText = 'Mengirim Permintaan...';

                    const minutes = document.querySelector('input[name="minutes"]:checked').value;
                    const reason = document.getElementById('reason').value;

                    try {
                        const res = await fetch('/api/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ minutes: parseInt(minutes), reason: reason, username: '{{username}}' })
                        });
                        const data = await res.json();
                        feedback.style.display = 'block';
                        if (res.ok && data.success) {
                            feedback.className = 'banner banner-success';
                            feedback.innerHTML = '✅ ' + data.message;
                            document.getElementById('requestForm').style.display = 'none';
                        } else {
                            feedback.className = 'banner banner-error';
                            feedback.innerHTML = '❌ ' + (data.error || 'Gagal mengirim permintaan.');
                            btn.disabled = false;
                            btn.innerText = 'Kirim Permintaan ke Administrator';
                        }
                    } catch (err) {
                        feedback.style.display = 'block';
                        feedback.className = 'banner banner-error';
                        feedback.innerHTML = '❌ Gagal terhubung ke layanan parental control.';
                        btn.disabled = false;
                        btn.innerText = 'Kirim Permintaan ke Administrator';
                    }
                }
            </script>
        </body>
        </html>
        """;
    }

    public static string RenderUnrestricted(string username)
    {
        return $$"""
        <!DOCTYPE html>
        <html lang="id">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control — Akun Bebas</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background: #0f172a; color: #f8fafc; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 1.5rem; }
                .card { background: #1e293b; border-radius: 1.25rem; padding: 2.5rem 2rem; max-width: 480px; width: 100%; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); text-align: center; }
                h1 { font-size: 1.5rem; margin-bottom: 1rem; color: #38bdf8; }
                p { font-size: 0.95rem; color: #94a3b8; line-height: 1.6; margin-bottom: 1.5rem; }
                .badge { background: #064e3b; color: #a7f3d0; padding: 0.35rem 0.85rem; border-radius: 9999px; font-weight: 600; font-size: 0.85rem; display: inline-block; margin-bottom: 1.25rem; }
                .btn { display: inline-block; background: #0284c7; color: white; padding: 0.85rem 1.5rem; border-radius: 0.5rem; text-decoration: none; font-weight: 600; }
                .btn:hover { background: #0369a1; }
            </style>
        </head>
        <body>
            <div class="card">
                <h1>🛡️ Parental Control</h1>
                <div class="badge">Akun: {{username}} (Administrator / Tidak Dibatasi)</div>
                <p>Akun ini tidak memiliki batasan waktu layar atau jam malam harian. Anda bebas menggunakan komputer kapan saja tanpa batas waktu.</p>
                <a href="/admin" class="btn">Buka Web Admin Console &rarr;</a>
            </div>
        </body>
        </html>
        """;
    }
}
