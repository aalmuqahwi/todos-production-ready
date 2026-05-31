using Microsoft.Extensions.Options;

using Todos.Notifications.Options;

namespace Todos.Notifications.Services;

// docs/let-it-crash.md
public class NotificationSummaryService : BackgroundService
{
    private readonly NotificationCounter _counter;
    private readonly IOptions<NotificationSummaryOptions> _options;
    private readonly ILogger<NotificationSummaryService> _logger;

    public NotificationSummaryService(
        NotificationCounter counter,
        IOptions<NotificationSummaryOptions> options,
        ILogger<NotificationSummaryService> logger)
    {
        _counter = counter;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var _ in _counter.Reader.ReadAllAsync(stoppingToken))
        {
            if (_options.Value.SimulateFailure)
            {
                var exception = new InvalidOperationException("Summary state is corrupted.");

                if (_options.Value.BroadCatch)
                {
                    _logger.LogError(exception, "Something went wrong. Continuing.");
                    continue;
                }

                throw exception;
            }

            _logger.LogInformation("Notifications received: {Count}", _counter.Get());
        }
    }
}
