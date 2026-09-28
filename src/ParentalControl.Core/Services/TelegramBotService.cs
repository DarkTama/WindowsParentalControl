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

        var text = $"🎮 *Screen Time Request*\n"
                 + $"*User:* `{username}`\n"
                 + $"*Requested:* {request.RequestedMinutes} minutes\n"
                 + $"*Reason:* _{EscapeMarkdown(request.Reason)}_\n"
                 + $"*Submitted:* {request.CreatedAt:HH:mm:ss}";

        var inlineKeyboard = new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = "✅ Approve 15m", callback_data = $"approve:{request.Id}:15" },
                    new { text = "✅ Approve 30m", callback_data = $"approve:{request.Id}:30" },
                    new { text = "❌ Decline", callback_data = $"decline:{request.Id}:0" }
                }
            }
        };

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
        if (!int.TryParse(parts[1], out var requestId) || !int.TryParse(parts[2], out var bonusMins))
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, "Invalid payload.");
            return;
        }

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

                // Find active session for user to pop notification
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

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`");
    }
}
