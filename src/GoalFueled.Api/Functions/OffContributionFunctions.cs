using GoalFueled.Api.Services.OpenFoodFacts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Timer-driven worker that drains the Open Food Facts contribution queue in rate-limited batches. Because
/// the OFF API is metered and contributions aren't time-sensitive, products are queued on save and pushed
/// here on a schedule rather than inline.
/// </summary>
public sealed class OffContributionFunctions
{
    // Default batch/spacing are conservative for a metered write API; both are overridable via config.
    private const int DefaultBatchSize = 20;
    private const int DefaultDelayMs = 6000; // ~10 products/minute

    private readonly OffContributionService _contribution;
    private readonly IConfiguration _config;
    private readonly ILogger<OffContributionFunctions> _logger;

    public OffContributionFunctions(
        OffContributionService contribution, IConfiguration config, ILogger<OffContributionFunctions> logger)
    {
        _contribution = contribution;
        _config = config;
        _logger = logger;
    }

    // Every 5 minutes. RunOnStartup is deliberately off so a deploy doesn't immediately hammer OFF.
    [Function("OffContributionWorker")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var batchSize = ReadInt("GoalFueled:OpenFoodFacts:ContributionBatchSize", DefaultBatchSize);
        var delayMs = ReadInt("GoalFueled:OpenFoodFacts:ContributionDelayMs", DefaultDelayMs);

        var sent = await _contribution.ProcessQueueAsync(batchSize, TimeSpan.FromMilliseconds(delayMs), ct);
        if (sent > 0)
            _logger.LogInformation("OFF contribution worker contributed {Count} product(s).", sent);
    }

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_config[key], out var value) && value > 0 ? value : fallback;
}
