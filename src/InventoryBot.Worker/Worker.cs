using InventoryBot.Application.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace InventoryBot.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClient _botClient;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider, ITelegramBotClient botClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClient = botClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bot started...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };

        _botClient.StartReceiving(
            async (bot, update, ct) =>
            {
                using var scope = _serviceProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
                await handler.HandleUpdateAsync(update, ct);
            },
            (bot, ex, ct) =>
            {
                _logger.LogError(ex, "Telegram API Error");
                return Task.CompletedTask;
            },
            receiverOptions,
            stoppingToken
        );

        await Task.Delay(-1, stoppingToken);
    }
}
