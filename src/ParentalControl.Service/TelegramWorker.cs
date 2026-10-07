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

        // Background loop for draining pending offline notifications
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _botService.ProcessPendingQueueAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error processing pending Telegram notifications queue");
                }
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }, stoppingToken);

        await _botService.PollUpdatesAsync(stoppingToken);
    }
}
