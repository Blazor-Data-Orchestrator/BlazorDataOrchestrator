using Azure.Storage.Blobs;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services
{
    /// <summary>
    /// Probes the blob endpoint once at startup so the designer never silently writes to storage the platform cannot see.
    /// </summary>
    public class StorageHealthService
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Null until the probe finishes.</summary>
        public bool? IsReachable { get; private set; }

        public string Endpoint { get; private set; } = "the configured blob endpoint";

        public Task Completion => _completion.Task;

        public async Task ProbeAsync(IServiceProvider services, ILogger logger)
        {
            try
            {
                var client = services.GetRequiredService<BlobServiceClient>();
                Endpoint = $"{client.Uri.Host}:{client.Uri.Port}";

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.GetPropertiesAsync(timeout.Token);
                IsReachable = true;
            }
            catch (Exception ex)
            {
                IsReachable = false;
                logger.LogWarning("Storage emulator not reachable at {Endpoint}. Start the platform with 'aspire run' first. ({Error})",
                    Endpoint, ex.Message);
            }
            finally
            {
                _completion.TrySetResult();
            }
        }
    }
}
