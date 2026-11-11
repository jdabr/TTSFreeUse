using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Api
{
    /// <summary>
    /// Prepares the TTS engine in the background at startup (e.g. the one-time
    /// Kokoro model download), retrying if it fails — for example when the
    /// computer is offline on first run.
    /// </summary>
    public class EngineWarmupService : BackgroundService
    {
        private readonly ITtsEngine _engine;
        private readonly ILogger<EngineWarmupService> _logger;

        public EngineWarmupService(ITtsEngine engine, ILogger<EngineWarmupService> logger)
        {
            _engine = engine;
            _logger = logger;
        }

        /// <summary>
        /// The wait before trying again after <paramref name="failures"/> failed
        /// attempts: 30 seconds, doubling up to 5 minutes. A failure partway
        /// through the 330 MB download costs real data on a metered connection,
        /// so the retries slow down instead of repeating every 30 seconds forever.
        /// </summary>
        internal static TimeSpan RetryDelay(int failures) =>
            TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, Math.Max(failures, 1) - 1), 300));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int failures = 1; !stoppingToken.IsCancellationRequested; failures++)
            {
                try
                {
                    _logger.LogInformation("Preparing {Engine} voice engine…", _engine.Name);
                    await _engine.InitializeAsync(stoppingToken);
                    _logger.LogInformation("{Engine} voice engine ready.", _engine.Name);
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var delay = RetryDelay(failures);
                    _logger.LogError(ex, "Voice engine failed to start; retrying in {Delay}s.", delay.TotalSeconds);
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }
    }
}
