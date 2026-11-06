using TTSFreeUse.Core.Jobs;

namespace TTSFreeUse.Api
{
    /// <summary>Runs queued narration jobs in the background.</summary>
    public class JobWorkerService : BackgroundService
    {
        private readonly NarrationJobQueue _queue;

        public JobWorkerService(NarrationJobQueue queue)
        {
            _queue = queue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _queue.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down.
            }
        }
    }
}
