using ParentalControl.Core.Services;

namespace ParentalControl.Service;

public sealed class TelegramWorker : BackgroundService
{
    private readonly TelegramBotService _botService;
    private readonly Serilog.ILogger _logger;

    public TelegramWorker(TelegramBotService botService, Serilog.ILogger logger)
    {
        _botService = botService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Telegram bot worker started");
        await _botService.PollUpdatesAsync(stoppingToken);
    }
}
