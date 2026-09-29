using System.Text.Json;

namespace ParentalControl.Service.Web.Pages;

public static class RequestPage
{
    public sealed record ScheduleDayView(
        DayOfWeek Day,
        string DayCode,
        string DayName,
        int Minutes,
        string TimeWindow,
        bool IsCustom,
        bool IsToday,
        bool HasException = false,
        string? ExceptionNote = null
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
        string? latestDeclineReason = null,
        DateTime? latestResolvedAt = null,
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

        var declineBanner = "";
        if (string.Equals(latestStatus, "DECLINED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(latestDeclineReason))
        {
            var timeFormatted = latestResolvedAt.HasValue ? $" ({latestResolvedAt.Value:HH:mm})" : "";
            declineBanner = $"""
                <div class="banner-decline">
                    <div class="decline-header">
                        <span class="decline-icon">🚫</span>
                        <span class="decline-title">Catatan Penolakan dari Orang Tua{timeFormatted}</span>
                    </div>
                    <div class="decline-text">"{latestDeclineReason}"</div>
                </div>
            """;
        }

        var disabledAttr = disableForm ? "disabled" : "";

        // Approach A: Segmented Day Strip + Active Day Detail Card
        var scheduleSection = "";
        var daysJson = "[]";
        var todayIndex = 0;

        if (weeklySchedule != null && weeklySchedule.Count > 0)
        {
            var pillsHtml = new System.Text.StringBuilder();
            for (int i = 0; i < weeklySchedule.Count; i++)
            {
                var d = weeklySchedule[i];
                if (d.IsToday) todayIndex = i;

                var dot = d.HasException
                    ? """<span class="pill-dot dot-exception" title="Pengecualian Khusus">★</span>"""
                    : (d.IsCustom ? """<span class="pill-dot dot-custom" title="Jadwal Khusus">•</span>""" : "");

                var activeCls = d.IsToday ? "active today" : "";

                pillsHtml.Append($$"""
                    <button type="button" class="day-pill {{activeCls}}" id="pill-{{i}}" onclick="selectDay({{i}})">
                        <span class="pill-code">{{d.DayCode}}</span>
                        {{dot}}
                    </button>
                """);
            }

            var defText = !string.IsNullOrWhiteSpace(defaultScheduleSummary)
                ? $"Standar: {defaultScheduleSummary}"
                : "Standar: 120m (08:00–22:00)";

            daysJson = JsonSerializer.Serialize(weeklySchedule.Select(d => new
            {
                dayName = d.DayName,
                dayCode = d.DayCode,
                minutes = d.Minutes,
                timeWindow = d.TimeWindow,
                isCustom = d.IsCustom,
                isToday = d.IsToday,
                hasException = d.HasException,
                exceptionNote = d.ExceptionNote
            }));

            scheduleSection = $$"""
                <div class="schedule-panel">
                    <div class="schedule-header">
                        <span class="schedule-title">📅 Jadwal Main Mingguan</span>
                        <span class="schedule-baseline">{{defText}}</span>
                    </div>

                    <!-- 7-Day Segmented Strip -->
                    <div class="day-strip">
                        {{pillsHtml}}
                    </div>

                    <!-- Active Day Detail Card -->
                    <div class="active-day-card" id="activeDayCard">
                        <div class="card-day-header">
                            <div>
                                <span class="card-day-title" id="activeDayTitle">--</span>
                                <span class="card-day-subtitle" id="activeDaySubtitle"></span>
                            </div>
                            <span class="badge-tag" id="activeDayBadge">Standar</span>
                        </div>
                        <div class="card-day-metrics">
                            <div class="metric-box">
                                <span class="metric-label">Kuota Waktu Layar</span>
                                <span class="metric-val" id="activeDayMinutes">--</span>
                            </div>
                            <div class="metric-box">
                                <span class="metric-label">Rentang Jam Main</span>
                                <span class="metric-val" id="activeDayCurfew">--:--</span>
                            </div>
                        </div>
                    </div>
                </div>
            """;
        }

        var tomorrowDate = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
        var minDate = DateTime.Now.ToString("yyyy-MM-dd");
        var maxDate = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");

        return $$"""
        <!DOCTYPE html>
        <html lang="id">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control — Minta Waktu & Jadwal</title>
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
                    max-width: 580px;
                    width: 100%;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.7), 0 0 30px rgba(56, 189, 248, 0.1);
                }
                .header-row {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    margin-bottom: 1.25rem;
                    flex-wrap: wrap;
                    gap: 0.5rem;
                }
                h1 {
                    font-size: 1.35rem;
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

                /* Countdown Display */
                .timer-panel {
                    background: #0b1120;
                    border: 1px solid #1e293b;
                    border-radius: 0.85rem;
                    padding: 1.25rem;
                    text-align: center;
                    margin-bottom: 1.25rem;
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

                /* Decline Notice Banner */
                .banner-decline {
                    background: rgba(127, 29, 29, 0.4);
                    border: 1px solid #ef4444;
                    border-radius: 0.75rem;
                    padding: 0.85rem 1rem;
                    margin-bottom: 1.25rem;
                }
                .decline-header {
                    display: flex;
                    align-items: center;
                    gap: 0.4rem;
                    margin-bottom: 0.35rem;
                }
                .decline-icon { font-size: 1rem; }
                .decline-title {
                    font-size: 0.85rem;
                    font-weight: 700;
                    color: #fca5a5;
                }
                .decline-text {
                    font-size: 0.9rem;
                    color: #fee2e2;
                    font-style: italic;
                    padding-left: 1.4rem;
                }

                /* Banners */
                .banner {
                    padding: 0.85rem 1.1rem;
                    border-radius: 0.65rem;
                    margin-bottom: 1.25rem;
                    font-size: 0.875rem;
                    line-height: 1.5;
                }
                .banner-error { background: #450a0a; color: #fecaca; border: 1px solid #991b1b; }
                .banner-info { background: #172554; color: #bfdbfe; border: 1px solid #1d4ed8; }
                .banner-success { background: #064e3b; color: #a7f3d0; border: 1px solid #059669; }

                /* Approach A: Segmented Schedule Strip */
                .schedule-panel {
                    background: #0f172a;
                    border: 1px solid #1e293b;
                    border-radius: 0.85rem;
                    padding: 1rem;
                    margin-bottom: 1.25rem;
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
                }
                .schedule-baseline {
                    font-size: 0.75rem;
                    color: #94a3b8;
                    background: #1e293b;
                    padding: 0.2rem 0.5rem;
                    border-radius: 0.35rem;
                }

                .day-strip {
                    display: grid;
                    grid-template-columns: repeat(7, 1fr);
                    gap: 0.4rem;
                    margin-bottom: 0.85rem;
                }
                .day-pill {
                    background: #0b1120;
                    border: 1px solid #1e293b;
                    border-radius: 0.5rem;
                    padding: 0.55rem 0.25rem;
                    cursor: pointer;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    position: relative;
                    transition: all 0.15s ease;
                    color: #94a3b8;
                    user-select: none;
                }
                .day-pill:hover {
                    border-color: #38bdf8;
                    color: #e2e8f0;
                    background: #1e293b;
                }
                .day-pill.active {
                    background: #0284c7;
                    border-color: #38bdf8;
                    color: #ffffff;
                    font-weight: 700;
                    box-shadow: 0 0 12px rgba(56, 189, 248, 0.4);
                }
                .day-pill.today::after {
                    content: '';
                    position: absolute;
                    bottom: 2px;
                    left: 50%;
                    transform: translateX(-50%);
                    width: 4px;
                    height: 4px;
                    border-radius: 50%;
                    background: #38bdf8;
                }
                .day-pill.active.today::after {
                    background: #ffffff;
                }
                .pill-code {
                    font-size: 0.85rem;
                    font-weight: 700;
                }
                .pill-dot {
                    font-size: 0.85rem;
                    margin-left: 2px;
                    line-height: 1;
                }
                .dot-custom { color: #f59e0b; }
                .dot-exception { color: #ec4899; }

                /* Active Day Detail Card */
                .active-day-card {
                    background: #0b1120;
                    border: 1px solid #1e293b;
                    border-radius: 0.65rem;
                    padding: 0.85rem 1rem;
                    display: flex;
                    flex-direction: column;
                    gap: 0.65rem;
                }
                .card-day-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                }
                .card-day-title {
                    font-size: 1rem;
                    font-weight: 700;
                    color: #f8fafc;
                }
                .card-day-subtitle {
                    font-size: 0.75rem;
                    color: #64748b;
                    margin-left: 0.4rem;
                }
                .badge-tag {
                    font-size: 0.72rem;
                    font-weight: 600;
                    padding: 0.15rem 0.5rem;
                    border-radius: 0.35rem;
                    background: #1e293b;
                    color: #94a3b8;
                    border: 1px solid #334155;
                }
                .badge-tag.custom {
                    background: #2e1065;
                    color: #c084fc;
                    border-color: #7c3aed;
                }
                .badge-tag.exception {
                    background: #500724;
                    color: #f472b6;
                    border-color: #db2777;
                }

                .card-day-metrics {
                    display: grid;
                    grid-template-columns: 1fr 1fr;
                    gap: 0.75rem;
                }
                .metric-box {
                    background: rgba(15, 23, 42, 0.6);
                    border: 1px solid #1e293b;
                    border-radius: 0.5rem;
                    padding: 0.55rem 0.75rem;
                }
                .metric-label {
                    display: block;
                    font-size: 0.7rem;
                    color: #64748b;
                    text-transform: uppercase;
                    letter-spacing: 0.05em;
                    margin-bottom: 0.2rem;
                }
                .metric-val {
                    font-size: 1.05rem;
                    font-weight: 700;
                    color: #38bdf8;
                }

                /* Tabs Navigation */
                .tabs {
                    display: flex;
                    gap: 0.5rem;
                    margin-bottom: 1.25rem;
                    border-bottom: 1px solid #1e293b;
                    padding-bottom: 0.5rem;
                }
                .tab-btn {
                    flex: 1;
                    background: transparent;
                    border: none;
                    color: #64748b;
                    font-size: 0.875rem;
                    font-weight: 600;
                    padding: 0.6rem 0.5rem;
                    border-radius: 0.5rem;
                    cursor: pointer;
                    transition: all 0.15s ease;
                }
                .tab-btn:hover {
                    color: #cbd5e1;
                    background: #0f172a;
                }
                .tab-btn.active {
                    color: #38bdf8;
                    background: #0f172a;
                    border: 1px solid #1e293b;
                }

                /* Form Controls */
                label.field-label {
                    display: block;
                    font-size: 0.85rem;
                    color: #cbd5e1;
                    margin-bottom: 0.5rem;
                    font-weight: 600;
                }
                .radio-group {
                    display: grid;
                    grid-template-columns: repeat(3, 1fr);
                    gap: 0.75rem;
                    margin-bottom: 1.25rem;
                }
                .radio-btn input { display: none; }
                .radio-btn label {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    background: #0f172a;
                    padding: 0.75rem 0.5rem;
                    border-radius: 0.65rem;
                    border: 2px solid #22314e;
                    cursor: pointer;
                    transition: all 0.2s;
                    color: #e2e8f0;
                    font-size: 0.9rem;
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
                    margin-bottom: 1.25rem;
                }
                .form-row {
                    display: grid;
                    grid-template-columns: 1fr 1fr;
                    gap: 0.75rem;
                    margin-bottom: 1.25rem;
                }
                input[type="date"], input[type="time"], select, textarea {
                    width: 100%;
                    background: #0b1120;
                    border: 2px solid #22314e;
                    border-radius: 0.65rem;
                    padding: 0.75rem;
                    color: #f8fafc;
                    font-size: 0.9rem;
                    font-family: inherit;
                    transition: border-color 0.2s;
                }
                input[type="date"]:focus, input[type="time"]:focus, select:focus, textarea:focus {
                    outline: none;
                    border-color: #38bdf8;
                    box-shadow: 0 0 0 3px rgba(56, 189, 248, 0.2);
                }
                input:disabled, select:disabled, textarea:disabled {
                    opacity: 0.5;
                    cursor: not-allowed;
                }
                textarea {
                    resize: vertical;
                    min-height: 80px;
                }

                button.submit-btn {
                    width: 100%;
                    background: #0284c7;
                    color: white;
                    border: none;
                    padding: 0.9rem;
                    border-radius: 0.65rem;
                    font-size: 0.95rem;
                    font-weight: 700;
                    cursor: pointer;
                    transition: all 0.2s;
                    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.2);
                }
                button.submit-btn:hover:not(:disabled) {
                    background: #0369a1;
                    transform: translateY(-1px);
                }
                button.submit-btn:disabled {
                    opacity: 0.45;
                    cursor: not-allowed;
                }
                #feedback { margin-top: 1rem; display: none; }
            </style>
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

                {{declineBanner}}
                {{scheduleSection}}
                {{adminBanner}}
                {{offlineAlert}}
                {{requestLimitAlert}}

                <div id="feedback"></div>

                <!-- Form Tabs -->
                <div class="tabs">
                    <button type="button" class="tab-btn active" id="tabBtnExt" onclick="switchTab('ext')">⏱️ Tambah Waktu Hari Ini</button>
                    <button type="button" class="tab-btn" id="tabBtnSched" onclick="switchTab('sched')">📅 Rencana Jadwal Mendatang</button>
                </div>

                <!-- Tab 1: Extension Form -->
                <form id="formExt" onsubmit="submitExtension(event)">
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
                        <label for="extReason" class="field-label">Alasan meminta tambahan waktu:</label>
                        <textarea id="extReason" placeholder="Contoh: Sedang menyelesaikan tugas sekolah, bermain game bersama teman..." required {{disabledAttr}}></textarea>
                    </div>

                    <button type="submit" class="submit-btn" id="btnExt" {{disabledAttr}}>Kirim Permintaan ke Orang Tua</button>
                </form>

                <!-- Tab 2: Advance Schedule Change Form -->
                <form id="formSched" style="display:none" onsubmit="submitScheduleChange(event)">
                    <div class="form-row">
                        <div>
                            <label for="schedDate" class="field-label">Tanggal Sasaran:</label>
                            <input type="date" id="schedDate" value="{{tomorrowDate}}" min="{{minDate}}" max="{{maxDate}}" required {{disabledAttr}}>
                        </div>
                        <div>
                            <label for="schedMinutes" class="field-label">Kuota Total Diinginkan:</label>
                            <select id="schedMinutes" {{disabledAttr}}>
                                <option value="60">60 Menit (1 Jam)</option>
                                <option value="90">90 Menit (1.5 Jam)</option>
                                <option value="120" selected>120 Menit (2 Jam)</option>
                                <option value="150">150 Menit (2.5 Jam)</option>
                                <option value="180">180 Menit (3 Jam)</option>
                                <option value="240">240 Menit (4 Jam)</option>
                            </select>
                        </div>
                    </div>

                    <div class="form-row">
                        <div>
                            <label for="schedStart" class="field-label">Jam Boleh Mulai:</label>
                            <input type="time" id="schedStart" value="08:00" required {{disabledAttr}}>
                        </div>
                        <div>
                            <label for="schedEnd" class="field-label">Jam Selesai (Malam):</label>
                            <input type="time" id="schedEnd" value="21:00" required {{disabledAttr}}>
                        </div>
                    </div>

                    <div class="form-group">
                        <label for="schedReason" class="field-label">Alasan perubahan jadwal mendatang:</label>
                        <textarea id="schedReason" placeholder="Contoh: Hari libur nasional, rencana belajar kelompok online, dll..." required {{disabledAttr}}></textarea>
                    </div>

                    <button type="submit" class="submit-btn" id="btnSched" {{disabledAttr}}>Ajukan Perubahan Jadwal ke Orang Tua</button>
                </form>
            </div>

            <script>
                // Countdown logic
                let totalSecondsRemaining = {{remainingSeconds}};
                const isSessionTicking = {{(isSessionActive && !isSessionLocked ? "true" : "false")}};

                function formatTime(totalSec) {
                    if (totalSec <= 0) return "00:00 (Habis)";
                    const hours = Math.floor(totalSec / 3600);
                    const minutes = Math.floor((totalSec % 3600) / 60);
                    const seconds = totalSec % 60;
                    const mm = String(minutes).padStart(2, '0');
                    const ss = String(seconds).padStart(2, '0');
                    if (hours > 0) return `${hours}:${mm}:${ss}`;
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
                    if (isSessionTicking) totalSecondsRemaining--;
                }

                tickCountdown();
                if (isSessionTicking) setInterval(tickCountdown, 1000);

                // Schedule Segmented Strip Logic
                const daysData = {{daysJson}};
                function selectDay(index) {
                    if (!daysData || !daysData[index]) return;
                    const d = daysData[index];

                    // Update Active Card
                    const titleElem = document.getElementById('activeDayTitle');
                    const subtitleElem = document.getElementById('activeDaySubtitle');
                    const badgeElem = document.getElementById('activeDayBadge');
                    const minsElem = document.getElementById('activeDayMinutes');
                    const curfewElem = document.getElementById('activeDayCurfew');

                    if (titleElem) titleElem.textContent = d.dayName + (d.isToday ? " (Hari Ini)" : "");
                    if (subtitleElem) subtitleElem.textContent = d.hasException ? "• " + (d.exceptionNote || "Pengecualian") : "";

                    if (badgeElem) {
                        if (d.hasException) {
                            badgeElem.textContent = "Pengecualian";
                            badgeElem.className = "badge-tag exception";
                        } else if (d.isCustom) {
                            badgeElem.textContent = "Jadwal Khusus";
                            badgeElem.className = "badge-tag custom";
                        } else {
                            badgeElem.textContent = "Standar";
                            badgeElem.className = "badge-tag";
                        }
                    }

                    if (minsElem) {
                        const h = Math.floor(d.minutes / 60);
                        const m = d.minutes % 60;
                        const hText = h > 0 ? (m > 0 ? `${h}j ${m}m` : `${h} Jam`) : `${m}m`;
                        minsElem.textContent = `${d.minutes} Menit (${hText})`;
                    }

                    if (curfewElem) curfewElem.textContent = d.timeWindow;

                    // Update Pill styles
                    for (let i = 0; i < daysData.length; i++) {
                        const p = document.getElementById(`pill-${i}`);
                        if (p) {
                            if (i === index) p.classList.add('active');
                            else p.classList.remove('active');
                        }
                    }
                }

                // Initial selection
                if (daysData.length > 0) {
                    selectDay({{todayIndex}});
                }

                // Tab switching
                function switchTab(tab) {
                    const btnExt = document.getElementById('tabBtnExt');
                    const btnSched = document.getElementById('tabBtnSched');
                    const formExt = document.getElementById('formExt');
                    const formSched = document.getElementById('formSched');

                    if (tab === 'ext') {
                        btnExt.classList.add('active');
                        btnSched.classList.remove('active');
                        formExt.style.display = 'block';
                        formSched.style.display = 'none';
                    } else {
                        btnSched.classList.add('active');
                        btnExt.classList.remove('active');
                        formSched.style.display = 'block';
                        formExt.style.display = 'none';
                    }
                }

                // Submit Extension Request
                async function submitExtension(e) {
                    e.preventDefault();
                    const btn = document.getElementById('btnExt');
                    const feedback = document.getElementById('feedback');
                    btn.disabled = true;
                    btn.innerText = 'Mengirim Permintaan...';

                    const minutes = document.querySelector('input[name="minutes"]:checked').value;
                    const reason = document.getElementById('extReason').value;

                    try {
                        const res = await fetch('/api/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                requestType: 'extension',
                                minutes: parseInt(minutes),
                                reason: reason,
                                username: '{{username}}'
                            })
                        });
                        const data = await res.json();
                        feedback.style.display = 'block';
                        if (res.ok && data.success) {
                            feedback.className = 'banner banner-success';
                            feedback.innerHTML = '✅ ' + data.message;
                            document.getElementById('formExt').style.display = 'none';
                            document.getElementById('formSched').style.display = 'none';
                        } else {
                            feedback.className = 'banner banner-error';
                            feedback.innerHTML = '❌ ' + (data.error || 'Gagal mengirim permintaan.');
                            btn.disabled = false;
                            btn.innerText = 'Kirim Permintaan ke Orang Tua';
                        }
                    } catch (err) {
                        feedback.style.display = 'block';
                        feedback.className = 'banner banner-error';
                        feedback.innerHTML = '❌ Gagal terhubung ke layanan parental control.';
                        btn.disabled = false;
                        btn.innerText = 'Kirim Permintaan ke Orang Tua';
                    }
                }

                // Submit Advance Schedule Change Request
                async function submitScheduleChange(e) {
                    e.preventDefault();
                    const btn = document.getElementById('btnSched');
                    const feedback = document.getElementById('feedback');
                    btn.disabled = true;
                    btn.innerText = 'Mengajukan Jadwal...';

                    const targetDate = document.getElementById('schedDate').value;
                    const minutes = document.getElementById('schedMinutes').value;
                    const start = document.getElementById('schedStart').value;
                    const end = document.getElementById('schedEnd').value;
                    const reason = document.getElementById('schedReason').value;

                    try {
                        const res = await fetch('/api/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                requestType: 'schedule_change',
                                targetDate: targetDate,
                                minutes: parseInt(minutes),
                                requestedStart: start,
                                requestedEnd: end,
                                reason: reason,
                                username: '{{username}}'
                            })
                        });
                        const data = await res.json();
                        feedback.style.display = 'block';
                        if (res.ok && data.success) {
                            feedback.className = 'banner banner-success';
                            feedback.innerHTML = '✅ ' + data.message;
                            document.getElementById('formExt').style.display = 'none';
                            document.getElementById('formSched').style.display = 'none';
                        } else {
                            feedback.className = 'banner banner-error';
                            feedback.innerHTML = '❌ ' + (data.error || 'Gagal mengajukan jadwal.');
                            btn.disabled = false;
                            btn.innerText = 'Ajukan Perubahan Jadwal ke Orang Tua';
                        }
                    } catch (err) {
                        feedback.style.display = 'block';
                        feedback.className = 'banner banner-error';
                        feedback.innerHTML = '❌ Gagal terhubung ke layanan parental control.';
                        btn.disabled = false;
                        btn.innerText = 'Ajukan Perubahan Jadwal ke Orang Tua';
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
