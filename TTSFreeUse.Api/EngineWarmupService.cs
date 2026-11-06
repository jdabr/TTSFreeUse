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
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        private readonly ITtsEngine _engine;
        private readonly ILogger<EngineWarmupService> _logger;

        public EngineWarmupService(ITtsEngine engine, ILogger<EngineWarmupService> logger)
        {
            _engine = engine;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
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
                    _logger.LogError(ex, "Voice engine failed to start; retrying in {Delay}s.",
                        RetryDelay.TotalSeconds);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }
    }
}
