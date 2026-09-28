namespace ParentalControl.Service.Web.Pages;

public static class RequestPage
{
    public static string Render(string username, int remainingMinutes, string curfew, bool isOffline, int requestsSubmittedToday, int maxDailyRequests, bool hasPendingRequest, string? latestStatus)
    {
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
            requestLimitAlert = """<div class="banner banner-info">ℹ️ Anda masih memiliki permintaan yang sedang menunggu keputusan administrator (Status: <strong>Menunggu Persetujuan</strong>). Harap tunggu sebelum mengirim lagi.</div>""";
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

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Parental Control — Minta Tambahan Waktu Layar</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background: #0f172a; color: #f8fafc; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 1rem; }
                .card { background: #1e293b; border-radius: 1rem; padding: 2rem; max-width: 480px; width: 100%; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); }
                h1 { font-size: 1.5rem; margin-bottom: 0.5rem; color: #38bdf8; display: flex; align-items: center; gap: 0.5rem; }
                .user-badge { background: #334155; padding: 0.25rem 0.75rem; border-radius: 9999px; font-size: 0.875rem; display: inline-block; margin-bottom: 1.5rem; }
                .stats-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1.5rem; }
                .stat-box { background: #0f172a; padding: 1rem; border-radius: 0.5rem; text-align: center; }
                .stat-box .val { font-size: 1.25rem; font-weight: bold; color: #f8fafc; }
                .stat-box .lbl { font-size: 0.75rem; color: #94a3b8; text-transform: uppercase; margin-top: 0.25rem; }
                .banner { padding: 0.75rem 1rem; border-radius: 0.5rem; margin-bottom: 1.5rem; font-size: 0.875rem; line-height: 1.4; }
                .banner-error { background: #7f1d1d; color: #fecaca; border: 1px solid #b91c1c; }
                .banner-info { background: #1e3a8a; color: #bfdbfe; border: 1px solid #2563eb; }
                .banner-success { background: #064e3b; color: #a7f3d0; border: 1px solid #059669; }
                label { display: block; font-size: 0.875rem; color: #cbd5e1; margin-bottom: 0.5rem; font-weight: 500; }
                .radio-group { display: flex; gap: 0.5rem; margin-bottom: 1.25rem; }
                .radio-btn { flex: 1; text-align: center; }
                .radio-btn input { display: none; }
                .radio-btn label { display: block; background: #0f172a; padding: 0.75rem 0.5rem; border-radius: 0.5rem; border: 2px solid #334155; cursor: pointer; transition: all 0.2s; color: #e2e8f0; }
                .radio-btn input:checked + label { border-color: #38bdf8; background: #0369a1; color: #fff; font-weight: bold; }
                textarea { width: 100%; background: #0f172a; border: 2px solid #334155; border-radius: 0.5rem; padding: 0.75rem; color: #f8fafc; font-size: 0.875rem; resize: vertical; min-height: 80px; margin-bottom: 1.25rem; }
                textarea:focus { outline: none; border-color: #38bdf8; }
                button { width: 100%; background: #0284c7; color: white; border: none; padding: 0.875rem; border-radius: 0.5rem; font-size: 1rem; font-weight: 600; cursor: pointer; transition: background 0.2s; }
                button:hover:not(:disabled) { background: #0369a1; }
                button:disabled { opacity: 0.5; cursor: not-allowed; }
                #feedback { margin-top: 1rem; display: none; }
            </style>
        </head>
        <body>
            <div class="card">
                <h1>🎮 Minta Tambahan Waktu Layar</h1>
                <div class="user-badge">Pengguna: <strong>{{username}}</strong></div>

                <div class="stats-grid">
                    <div class="stat-box">
                        <div class="val">{{remainingMinutes}} menit</div>
                        <div class="lbl">Sisa Waktu Hari Ini</div>
                    </div>
                    <div class="stat-box">
                        <div class="val">{{curfew}}</div>
                        <div class="lbl">Batas Jam Malam</div>
                    </div>
                </div>

                {{offlineAlert}}
                {{requestLimitAlert}}

                <div id="feedback"></div>

                <form id="requestForm" onsubmit="submitForm(event)">
                    <label>Pilihan Tambahan Waktu:</label>
                    <div class="radio-group">
                        <div class="radio-btn">
                            <input type="radio" id="m15" name="minutes" value="15" checked {{disabledAttr}}>
                            <label for="m15">+15 Menit</label>
                        <div class="radio-btn">
                            <input type="radio" id="m30" name="minutes" value="30" {{disabledAttr}}>
                            <label for="m30">+30 Menit</label>
                        <div class="radio-btn">
                            <input type="radio" id="m60" name="minutes" value="60" {{disabledAttr}}>
                            <label for="m60">+1 Jam</label>
                    </div>

                    <label for="reason">Alasan meminta tambahan waktu:</label>
                    <textarea id="reason" name="reason" placeholder="Contoh: Sedang bermain game dengan teman, menyelesaikan tugas sekolah..." required {{disabledAttr}}></textarea>

                    <button type="submit" id="submitBtn" {{disabledAttr}}>Kirim Permintaan ke Admin</button>
                </form>
            </div>

            <script>
                async function submitForm(e) {
                    e.preventDefault();
                    const btn = document.getElementById('submitBtn');
                    const feedback = document.getElementById('feedback');
                    btn.disabled = true;
                    btn.innerText = 'Mengirim...';

                    const minutes = document.querySelector('input[name="minutes"]:checked').value;
                    const reason = document.getElementById('reason').value;

                    try {
                        const res = await fetch('/api/request', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ minutes: parseInt(minutes), reason: reason })
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
                            btn.innerText = 'Kirim Permintaan ke Admin';
                        }
                    } catch (err) {
                        feedback.style.display = 'block';
                        feedback.className = 'banner banner-error';
                        feedback.innerHTML = '❌ Gagal terhubung ke layanan.';
                        btn.disabled = false;
                        btn.innerText = 'Kirim Permintaan ke Admin';
                    }
                }
            </script>
        </body>
        </html>
        """;
    }
}
