using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Platform;

namespace ParentalControl.Core.Services;

public sealed class TelegramBotService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly Serilog.ILogger _logger;
    private long _lastUpdateId = 0;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int RequestId, DateTime ExpiresAt)> _pendingCustomDeclines = new();

    public static Func<string, Task<ScreenCaptureRecord?>>? OnCaptureRequested { get; set; }
    public static Func<Task<string>>? OnStatusRequested { get; set; }

    public TelegramBotService(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public static async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await HttpClient.GetAsync("https://api.telegram.org", cts.Token);
            return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendGraceRequestAlertAsync(GraceRequest request, string username)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        var chatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
        {
            _logger.Warning("Telegram alert skipped: bot token or chat ID is not configured.");
            return false;
        }

        string text;
        object inlineKeyboard;

        if (string.Equals(request.RequestType, "schedule_change", StringComparison.OrdinalIgnoreCase))
        {
            var targetDate = request.TargetDate ?? request.Date;
            var dayName = GetIndonesianDay(targetDate.DayOfWeek);
            var startStr = request.RequestedStart.HasValue ? request.RequestedStart.Value.ToString("HH:mm") : "08:00";
            var endStr = request.RequestedEnd.HasValue ? request.RequestedEnd.Value.ToString("HH:mm") : "21:00";

            text = $"📅 *Pengajuan Perubahan Jadwal Layar*\n"
                 + $"*Pengguna:* `{username}`\n"
                 + $"*Tanggal Sasaran:* `{targetDate:yyyy-MM-dd}` ({dayName})\n"
                 + $"*Kuota & Jam:* {request.RequestedMinutes} menit ({startStr} – {endStr})\n"
                 + $"*Alasan:* _{EscapeMarkdown(request.Reason)}_\n"
                 + $"*Diajukan:* {request.CreatedAt:HH:mm:ss}";

            inlineKeyboard = new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✅ Setujui (1 Hari Saja)", callback_data = $"app_exc:{request.Id}:0" } },
                    new object[] { new { text = "🌟 Setujui (Permanen)", callback_data = $"app_perm:{request.Id}:0" } },
                    new object[] { new { text = "❌ Tolak Permintaan", callback_data = $"dec_menu:{request.Id}:0" } }
                }
            };
        }
        else
        {
            text = $"🎮 *Screen Time Request*\n"
                 + $"*User:* `{username}`\n"
                 + $"*Requested:* {request.RequestedMinutes} minutes\n"
                 + $"*Reason:* _{EscapeMarkdown(request.Reason)}_\n"
                 + $"*Submitted:* {request.CreatedAt:HH:mm:ss}";

            var approveMinutes = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            approveMinutes.Add(request.RequestedMinutes);
            if (request.RequestedMinutes > 60) approveMinutes.Add(60);
            if (request.RequestedMinutes > 30) approveMinutes.Add(30);
            if (request.RequestedMinutes > 15) approveMinutes.Add(15);

            var allButtons = approveMinutes
                .Select(m => (object)new { text = $"✅ Approve {m}m", callback_data = $"approve:{request.Id}:{m}" })
                .ToList();
            allButtons.Add(new { text = "❌ Tolak", callback_data = $"dec_menu:{request.Id}:0" });

            object[][] buttonRows;
            if (allButtons.Count <= 3)
            {
                buttonRows = new[] { allButtons.ToArray() };
            }
            else
            {
                buttonRows = allButtons
                    .Select((btn, idx) => new { btn, idx })
                    .GroupBy(x => x.idx / 2)
                    .Select(g => g.Select(x => x.btn).ToArray())
                    .ToArray();
            }

            inlineKeyboard = new
            {
                inline_keyboard = buttonRows
            };
        }
        var payload = new
        {
            chat_id = chatId,
            text = text,
            parse_mode = "Markdown",
            reply_markup = inlineKeyboard
        };

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to dispatch Telegram grace request notification");
            return false;
        }
    }

    public async Task PollUpdatesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
            var configuredChatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(configuredChatId))
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_lastUpdateId + 1}&timeout=10";
                var response = await HttpClient.GetAsync(url, stoppingToken);

                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var jsonStr = await response.Content.ReadAsStringAsync(stoppingToken);
                var doc = JsonNode.Parse(jsonStr);
                var results = doc?["result"]?.AsArray();

                if (results != null)
                {
                    foreach (var item in results)
                    {
                        var updateId = item?["update_id"]?.GetValue<long>() ?? 0;
                        if (updateId > _lastUpdateId)
                        {
                            _lastUpdateId = updateId;
                        }

                        var callbackQuery = item?["callback_query"];
                        if (callbackQuery != null)
                        {
                            await HandleCallbackQueryAsync(token, configuredChatId, callbackQuery);
                        }

                        var message = item?["message"];
                        if (message != null)
                        {
                            await HandleMessageAsync(token, configuredChatId, message);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error while polling Telegram updates");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(string botToken, string configuredChatId, JsonNode callbackQuery)
    {
        var callbackId = callbackQuery["id"]?.GetValue<string>() ?? "";
        var data = callbackQuery["data"]?.GetValue<string>() ?? "";
        var messageNode = callbackQuery["message"];
        var messageId = messageNode?["message_id"]?.GetValue<long>() ?? 0;
        var chatId = messageNode?["chat"]?["id"]?.ToString() ?? "";

        if (chatId != configuredChatId)
        {
            _logger.Warning("Unauthorized Telegram callback attempt from chat {ChatId}", chatId);
            await AnswerCallbackQueryAsync(botToken, callbackId, "Unauthorized.");
            return;
        }

        var parts = data.Split(':');
        if (parts.Length < 3)
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, "Invalid command.");
            return;
        }
        var action = parts[0];
        if (action == "admin_verify")
        {
            var subAction = parts[1].ToLowerInvariant(); // "self" or "lock"
            int.TryParse(parts[2], out var targetSessionId);

            if (subAction == "self")
            {
                await AnswerCallbackQueryAsync(botToken, callbackId, "Akses administrator diverifikasi.");
                await EditMessageTextAsync(botToken, configuredChatId, messageId,
                    $"✅ *Login Administrator Diverifikasi oleh Orang Tua*\n"
                    + $"Waktu verifikasi: `{DateTime.Now:HH:mm:ss}`\n"
                    + $"Sesi pada workstation `{Environment.MachineName}` telah dikonfirmasi aman.");
            }
            else if (subAction == "lock")
            {
                await AnswerCallbackQueryAsync(botToken, callbackId, "Workstation dikunci!");
                if (targetSessionId > 0)
                {
                    SessionManager.LockSession(targetSessionId);
                }
                var consoleId = SessionManager.GetActiveConsoleSessionId();
                if (consoleId >= 0 && consoleId != targetSessionId)
                {
                    SessionManager.LockSession(consoleId);
                }

                EventRepository.LogEvent("SYSTEM", EventType.SECURITY_ALERT,
                    $"Workstation locked remotely via Telegram: unauthorized admin login alert on session {targetSessionId}");

                await EditMessageTextAsync(botToken, configuredChatId, messageId,
                    $"🔒 *AKSES DITOLAK: Komputer Telah Dikunci*\n"
                    + $"Perintah penguncian darurat dieksekusi pada `{DateTime.Now:HH:mm:ss}`.\n"
                    + $"Workstation `{Environment.MachineName}` berhasil dikunci aman.");
            }
            return;
        }


        if (!int.TryParse(parts[1], out var requestId))
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, "Invalid payload.");
            return;
        }
        _ = int.TryParse(parts[2], out var bonusMins);

        var req = GraceRequestRepository.GetById(requestId);
        if (req == null)
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, "Request not found.");
            return;
        }

        if (req.Status != "PENDING")
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, $"Already resolved as {req.Status}.");
            return;
        }

        if (action == "approve")
        {
            GraceRequestRepository.Resolve(requestId, "APPROVED");
            UsageRepository.AddBonusMinutes(req.UserId, req.Date, bonusMins);

            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Grace granted: +{bonusMins}m by Admin (Request #{requestId})");

                var activeSessions = SessionManager.GetActiveSessions();
                var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                if (userSession.Sid != null)
                {
                    NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Tambahan Waktu Disetujui!",
                        $"Kabar baik! Administrator telah menyetujui tambahan waktu layar sebesar +{bonusMins} menit untuk hari ini.",
                        isWarning: false, timeoutSeconds: 20);
                }
            }

            await AnswerCallbackQueryAsync(botToken, callbackId, $"Approved +{bonusMins}m!");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"✅ *Screen Time Request #{requestId} APPROVED* (+{bonusMins} mins)\nResolved at {DateTime.Now:HH:mm:ss}");
        }
        else if (action == "app_exc")
        {
            var targetDate = req.TargetDate ?? req.Date;
            var start = req.RequestedStart ?? new TimeOnly(8, 0);
            var end = req.RequestedEnd ?? new TimeOnly(21, 0);
            var mins = req.RequestedMinutes > 0 ? req.RequestedMinutes : 120;

            ScheduleExceptionRepository.Upsert(new ScheduleException
            {
                UserId = req.UserId,
                ExceptionDate = targetDate,
                DailyMinutes = mins,
                ScheduleStart = start,
                ScheduleEnd = end,
                CreatedAt = DateTime.Now
            });

            GraceRequestRepository.Resolve(requestId, "APPROVED");

            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Schedule exception approved for {targetDate:yyyy-MM-dd}: {mins}m ({start:HH:mm}-{end:HH:mm}) (Request #{requestId})");
                var activeSessions = SessionManager.GetActiveSessions();
                var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                if (userSession.Sid != null)
                {
                    NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Jadwal Disetujui!",
                        $"Jadwal khusus untuk tanggal {targetDate:dd/MM/yyyy} ({mins}m, {start:HH:mm}–{end:HH:mm}) telah disetujui orang tua.",
                        isWarning: false, timeoutSeconds: 20);
                }
            }

            await AnswerCallbackQueryAsync(botToken, callbackId, "Pengecualian Disetujui!");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"✅ *Schedule Change #{requestId} APPROVED (1 Hari Saja)*\nTanggal: `{targetDate:yyyy-MM-dd}`\nKuota: {mins} menit ({start:HH:mm} – {end:HH:mm})\nResolved at {DateTime.Now:HH:mm:ss}");
        }
        else if (action == "app_perm")
        {
            var targetDate = req.TargetDate ?? req.Date;
            var targetDayOfWeek = targetDate.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            var start = req.RequestedStart ?? new TimeOnly(8, 0);
            var end = req.RequestedEnd ?? new TimeOnly(21, 0);
            var mins = req.RequestedMinutes > 0 ? req.RequestedMinutes : 120;

            ScheduleRepository.SaveDaySchedule(new DaySchedule
            {
                UserId = req.UserId,
                DayOfWeek = targetDayOfWeek,
                DailyMinutes = mins,
                ScheduleStart = start,
                ScheduleEnd = end
            });
            GraceRequestRepository.Resolve(requestId, "APPROVED");

            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Permanent schedule approved for {targetDayOfWeek}: {mins}m ({start:HH:mm}-{end:HH:mm}) (Request #{requestId})");
                var activeSessions = SessionManager.GetActiveSessions();
                var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                if (userSession.Sid != null)
                {
                    NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Jadwal Permanen Disetujui!",
                        $"Jadwal mingguan hari {GetIndonesianDay(targetDayOfWeek)} ({mins}m, {start:HH:mm}–{end:HH:mm}) telah diperbarui permanen oleh orang tua.",
                        isWarning: false, timeoutSeconds: 20);
                }
            }

            await AnswerCallbackQueryAsync(botToken, callbackId, "Jadwal Permanen Disetujui!");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"🌟 *Schedule Change #{requestId} APPROVED (Jadwal Permanen)*\nHari: `{GetIndonesianDay(targetDayOfWeek)}`\nKuota: {mins} menit ({start:HH:mm} – {end:HH:mm})\nResolved at {DateTime.Now:HH:mm:ss}");
        }
        else if (action == "dec_menu")
        {
            var presetsKeyboard = new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "📚 Belum Selesai Tugas", callback_data = $"dec_preset:{requestId}:1" } },
                    new object[] { new { text = "🌙 Waktunya Tidur", callback_data = $"dec_preset:{requestId}:2" } },
                    new object[] { new { text = "🍽️ Waktunya Makan", callback_data = $"dec_preset:{requestId}:3" } },
                    new object[] { new { text = "⚠️ Melanggar Aturan", callback_data = $"dec_preset:{requestId}:4" } },
                    new object[] { new { text = "✍️ Tulis Alasan Sendiri", callback_data = $"dec_custom:{requestId}:0" } },
                    new object[] { new { text = "❌ Tolak Tanpa Alasan", callback_data = $"dec_preset:{requestId}:0" } }
                }
            };

            await AnswerCallbackQueryAsync(botToken, callbackId, "Pilih alasan penolakan:");
            await EditMessageTextAndKeyboardAsync(botToken, configuredChatId, messageId,
                $"❌ *Pilih Alasan Penolakan untuk Permintaan #{requestId}:*", presetsKeyboard);
        }
        else if (action == "dec_preset")
        {
            string declineReason = bonusMins switch
            {
                1 => "Selesaikan tugas sekolah terlebih dahulu.",
                2 => "Sudah waktunya tidur/istirahat.",
                3 => "Waktunya makan bersama keluarga.",
                4 => "Waktu bermain dibatasi karena melanggar aturan.",
                _ => "Permintaan ditolak oleh orang tua."
            };

            GraceRequestRepository.Resolve(requestId, "DECLINED", declineReason);
            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN_DENIED, $"Grace declined by parent: {declineReason} (Request #{requestId})");
                var activeSessions = SessionManager.GetActiveSessions();
                var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                if (userSession.Sid != null)
                {
                    NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Permintaan Ditolak",
                        $"Permintaan Anda ditolak: \"{declineReason}\"",
                        isWarning: true, timeoutSeconds: 15);
                }
            }

            await AnswerCallbackQueryAsync(botToken, callbackId, "Ditolak.");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"❌ *Permintaan #{requestId} DITOLAK*\nAlasan: _{EscapeMarkdown(declineReason)}_\nResolved at {DateTime.Now:HH:mm:ss}");
        }
        else if (action == "dec_custom")
        {
            _pendingCustomDeclines[configuredChatId] = (requestId, DateTime.UtcNow.AddMinutes(3));
            await AnswerCallbackQueryAsync(botToken, callbackId, "Ketik alasan...");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"✍️ *Silakan balas/ketik alasan penolakan untuk Permintaan #{requestId}:*\n(Waktu respons: 3 menit)");
        }
        else if (action == "decline")
        {
            GraceRequestRepository.Resolve(requestId, "DECLINED");
            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN_DENIED, $"Grace declined by Admin (Request #{requestId})");

                var activeSessions = SessionManager.GetActiveSessions();
                var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                if (userSession.Sid != null)
                {
                    NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Permintaan Ditolak",
                        "Permintaan tambahan waktu layar Anda ditolak oleh administrator.",
                        isWarning: true, timeoutSeconds: 15);
                }
            }

            await AnswerCallbackQueryAsync(botToken, callbackId, "Declined.");
            await EditMessageTextAsync(botToken, configuredChatId, messageId,
                $"❌ *Screen Time Request #{requestId} DECLINED*\nResolved at {DateTime.Now:HH:mm:ss}");
        }
    }

    private static async Task AnswerCallbackQueryAsync(string botToken, string callbackId, string text)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/answerCallbackQuery";
            var payload = new { callback_query_id = callbackId, text = text };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await HttpClient.PostAsync(url, content);
        }
        catch { }
    }

    private static async Task EditMessageTextAsync(string botToken, string chatId, long messageId, string text)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/editMessageText";
            var payload = new
            {
                chat_id = chatId,
                message_id = messageId,
                text = text,
                parse_mode = "Markdown"
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await HttpClient.PostAsync(url, content);
        }
        catch { }
    }

    private async Task HandleMessageAsync(string botToken, string configuredChatId, JsonNode message)
    {
        var chatId = message?["chat"]?["id"]?.ToString() ?? "";
        if (chatId != configuredChatId) return;

        var text = message?["text"]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_pendingCustomDeclines.TryGetValue(configuredChatId, out var pending))
        {
            if (DateTime.UtcNow < pending.ExpiresAt)
            {
                _pendingCustomDeclines.TryRemove(configuredChatId, out _);
                var customReason = text.Trim();
                GraceRequestRepository.Resolve(pending.RequestId, "DECLINED", customReason);

                var customReq = GraceRequestRepository.GetById(pending.RequestId);
                if (customReq != null)
                {
                    var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == customReq.UserId);
                    if (user != null)
                    {
                        EventRepository.LogEvent(user.Sid, EventType.LOGIN_DENIED, $"Grace declined with custom reason: {customReason} (Request #{pending.RequestId})");
                        var activeSessions = SessionManager.GetActiveSessions();
                        var userSession = activeSessions.FirstOrDefault(s => s.Sid == user.Sid);
                        if (userSession.Sid != null)
                        {
                            NotificationManager.SendMessage(userSession.SessionId, "Parental Control — Permintaan Ditolak",
                                $"Permintaan Anda ditolak: \"{customReason}\"",
                                isWarning: true, timeoutSeconds: 15);
                        }
                    }
                }

                await SendTextMessageAsync(chatId, $"❌ Permintaan #{pending.RequestId} telah ditolak dengan alasan:\n_{EscapeMarkdown(customReason)}_");
                return;
            }
            else
            {
                _pendingCustomDeclines.TryRemove(configuredChatId, out _);
            }
        }
        if (text.StartsWith("/capture", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var userArg = parts.Length > 1 ? parts[1].Trim() : "";

            if (OnCaptureRequested == null)
            {
                await SendTextMessageAsync(chatId, "⚠️ Layanan tangkapan layar belum siap.");
                return;
            }

            await SendTextMessageAsync(chatId, $"📸 Meminta tangkapan layar desktop untuk {(string.IsNullOrEmpty(userArg) ? "pengguna aktif" : userArg)}...");
            var record = await OnCaptureRequested(userArg);
            if (record != null && File.Exists(record.FilePath))
            {
                try
                {
                    var photoBytes = await File.ReadAllBytesAsync(record.FilePath);
                    var caption = $"📸 *Tangkapan Layar Desktop*\n⏰ Waktu: `{record.Timestamp:yyyy-MM-dd HH:mm:ss}`\n📏 Resolusi: {record.Width}x{record.Height}\n🏷️ Trigger: {record.TriggerType}";
                    await SendPhotoAsync(chatId, photoBytes, caption);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Gagal membaca atau mengirim foto tangkapan layar");
                    await SendTextMessageAsync(chatId, "❌ Gagal mengirim gambar tangkapan layar.");
                }
            }
            else
            {
                await SendTextMessageAsync(chatId, "❌ Gagal mengambil tangkapan layar. Pastikan pengguna sedang aktif dan sesi tidak sedang terkunci.");
            }
        }
        else if (text.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
        {
            if (OnStatusRequested != null)
            {
                var statusText = await OnStatusRequested();
                await SendTextMessageAsync(chatId, statusText);
            }
        }
        else if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase) || text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var helpMsg = "🛡️ *Parental Control Admin Bot*\n\n"
                        + "Perintah yang tersedia:\n"
                        + "• `/capture [user]` — Ambil tangkapan layar desktop diam-diam\n"
                        + "• `/status` — Cek sisa screen time dan aplikasi aktif anak\n"
                        + "• `/help` — Tampilkan bantuan ini";
            await SendTextMessageAsync(chatId, helpMsg);
        }
    }

    public async Task<bool> SendPhotoAsync(string chatId, byte[] photoBytes, string caption)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendPhoto";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId), "chat_id");
            form.Add(new StringContent(caption), "caption");
            form.Add(new StringContent("Markdown"), "parse_mode");

            var fileContent = new ByteArrayContent(photoBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(fileContent, "photo", "screenshot.jpg");

            var response = await HttpClient.PostAsync(url, form);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Telegram photo");
            return false;
        }
    }

    public async Task<bool> SendTextMessageAsync(string chatId, string text)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = text,
                parse_mode = "Markdown"
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Telegram text message");
            return false;
        }
    }
    public async Task<bool> SendTextMessageWithKeyboardAsync(string chatId, string text, object inlineKeyboard)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text,
                parse_mode = "Markdown",
                reply_markup = inlineKeyboard
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Telegram message with keyboard");
            return false;
        }
    }

    private async Task<bool> SendWithRetryAsync(Func<Task<bool>> sendAction, int maxRetries = 3)
    {
        int[] delays = { 0, 15, 30 };
        for (int i = 0; i < maxRetries; i++)
        {
            if (delays[i] > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delays[i]));
            }
            try
            {
                if (await sendAction())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Telegram notification attempt {Attempt} failed", i + 1);
            }
        }
        return false;
    }

    public async Task<bool> SendUserSignInNotificationAsync(string username, string machineName, DateTime time, int remainingMinutes, TimeOnly? start, TimeOnly? end)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        var chatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId)) return false;

        var lang = SettingsRepository.Get(SettingsRepository.KeyLanguagePreset, "id");
        var remainingHours = remainingMinutes / 60;
        var remainingMins = remainingMinutes % 60;
        var remainingStr = remainingHours > 0 ? $"{remainingHours}j {remainingMins}m" : $"{remainingMins}m";
        if (lang == "en")
        {
            remainingStr = remainingHours > 0 ? $"{remainingHours}h {remainingMins}m" : $"{remainingMins}m";
        }

        var curfewStr = (start.HasValue && end.HasValue) ? $"{start.Value:HH:mm} – {end.Value:HH:mm}" : "-";

        string text;
        if (lang == "en")
        {
            text = $"👤 *Notification: User Signed In*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *User:* `{EscapeMarkdown(username)}`\n"
                 + $"• *Computer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Time:* `{time:HH:mm:ss}`\n"
                 + $"• *Remaining Screen Time:* {remainingStr}\n"
                 + $"• *Curfew Window:* {curfewStr}";
        }
        else
        {
            text = $"👤 *Pemberitahuan: Pengguna Masuk*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *Pengguna:* `{EscapeMarkdown(username)}`\n"
                 + $"• *Komputer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Waktu:* `{time:HH:mm:ss}`\n"
                 + $"• *Sisa Waktu Layar:* {remainingStr}\n"
                 + $"• *Jam Operasional:* {curfewStr}";
        }

        return await SendWithRetryAsync(() => SendTextMessageAsync(chatId, text));
    }

    public async Task<bool> SendUserSignOutNotificationAsync(string username, string machineName, DateTime time, int minutesUsedToday)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        var chatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId)) return false;

        var lang = SettingsRepository.Get(SettingsRepository.KeyLanguagePreset, "id");
        var usedHours = minutesUsedToday / 60;
        var usedMins = minutesUsedToday % 60;
        var usedStr = usedHours > 0 ? $"{usedHours}j {usedMins}m" : $"{usedMins}m";
        if (lang == "en")
        {
            usedStr = usedHours > 0 ? $"{usedHours}h {usedMins}m" : $"{usedMins}m";
        }

        string text;
        if (lang == "en")
        {
            text = $"🚪 *Notification: User Signed Out*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *User:* `{EscapeMarkdown(username)}`\n"
                 + $"• *Computer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Time:* `{time:HH:mm:ss}`\n"
                 + $"• *Screen Time Used Today:* {usedStr}";
        }
        else
        {
            text = $"🚪 *Pemberitahuan: Pengguna Keluar*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *Pengguna:* `{EscapeMarkdown(username)}`\n"
                 + $"• *Komputer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Waktu:* `{time:HH:mm:ss}`\n"
                 + $"• *Total Waktu Terpakai Hari Ini:* {usedStr}";
        }

        return await SendWithRetryAsync(() => SendTextMessageAsync(chatId, text));
    }

    public async Task<bool> SendAdminSignInAlertAsync(int sessionId, string username, string machineName, DateTime time)
    {
        var token = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        var chatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId)) return false;

        var lang = SettingsRepository.Get(SettingsRepository.KeyLanguagePreset, "id");
        string text;
        object keyboard;

        if (lang == "en")
        {
            text = $"🚨 *SECURITY ALERT: Administrator Login Detected*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *User:* `{EscapeMarkdown(username)}` (Administrator / Unrestricted)\n"
                 + $"• *Computer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Time:* `{time:HH:mm:ss}`\n\n"
                 + $"⚠️ *Is this you?*\n"
                 + $"If this logon was unauthorized, tap *Not Me — Lock PC!* immediately to secure your workstation.";

            keyboard = new
            {
                inline_keyboard = new object[][]
                {
                    new object[]
                    {
                        new { text = "✅ It's Me", callback_data = $"admin_verify:self:{sessionId}" },
                        new { text = "🔒 Not Me — Lock PC!", callback_data = $"admin_verify:lock:{sessionId}" }
                    }
                }
            };
        }
        else
        {
            text = $"🚨 *PERINGATAN KEAMANAN: Login Admin Terdeteksi*\n"
                 + $"━━━━━━━━━━━━━━━━━━━━\n"
                 + $"• *Pengguna:* `{EscapeMarkdown(username)}` (Administrator / Hak Penuh)\n"
                 + $"• *Komputer:* `{EscapeMarkdown(machineName)}`\n"
                 + $"• *Waktu:* `{time:HH:mm:ss}`\n\n"
                 + $"⚠️ *Apakah ini Anda?*\n"
                 + $"Jika ini bukan Anda atau mencurigakan, segera ketuk tombol *Kunci Komputer* di bawah untuk mengamankan PC.";

            keyboard = new
            {
                inline_keyboard = new object[][]
                {
                    new object[]
                    {
                        new { text = "✅ Saya Sendiri", callback_data = $"admin_verify:self:{sessionId}" },
                        new { text = "🔒 Bukan Saya — Kunci Komputer!", callback_data = $"admin_verify:lock:{sessionId}" }
                    }
                }
            };
        }

        return await SendWithRetryAsync(() => SendTextMessageWithKeyboardAsync(chatId, text, keyboard));
    }


    private static async Task EditMessageTextAndKeyboardAsync(string botToken, string chatId, long messageId, string text, object inlineKeyboard)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/editMessageText";
            var payload = new
            {
                chat_id = chatId,
                message_id = messageId,
                text = text,
                parse_mode = "Markdown",
                reply_markup = inlineKeyboard
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await HttpClient.PostAsync(url, content);
        }
        catch { }
    }

    private static string GetIndonesianDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Senin",
        DayOfWeek.Tuesday => "Selasa",
        DayOfWeek.Wednesday => "Rabu",
        DayOfWeek.Thursday => "Kamis",
        DayOfWeek.Friday => "Jumat",
        DayOfWeek.Saturday => "Sabtu",
        DayOfWeek.Sunday => "Minggu",
        _ => day.ToString()
    };
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`");
    }
}
