using Maki.Api.Services;
using Quartz;

namespace Maki.Api.Jobs;

/// <summary>
/// Recycles chapter files that a volume file in the same series already contains
/// (<see cref="VolumeOverlapService"/>). Runs once a day; each file moved can be restored from the
/// recycle bin.
/// </summary>
[DisallowConcurrentExecution]
public class VolumeOverlapJob(IServiceScopeFactory scopes, ILogger<VolumeOverlapJob> logger) : IJob
{
    public static readonly JobKey Key = new("volume-overlap");

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        using var scope = scopes.CreateScope();
        var candidates = await scope.ServiceProvider.GetRequiredService<VolumeOverlapService>().CandidateSeriesAsync(ct);
        foreach (var seriesId in candidates)
        {
            // A scope per series so one failure or a long change tracker never carries over.
            using var seriesScope = scopes.CreateScope();
            try
            {
                await seriesScope.ServiceProvider.GetRequiredService<VolumeOverlapService>().ReconcileAsync(seriesId, apply: true, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Volume overlap check failed for series {Id}", seriesId);
            }
        }
    }
}
