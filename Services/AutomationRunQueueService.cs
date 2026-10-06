using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationRunQueueService
{
    private readonly AutomationRunQueueStore _queueStore = new();
    private readonly AutomationSuiteRunStore _suiteRunStore = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    public event Action<AutomationQueueItem>? ItemChanged;

    public async Task RunPendingAsync(IList<AutomationQueueItem> queue, AutomationQueueSettings settings, CancellationToken cancellationToken)
    {
        var max = Math.Clamp(settings.MaxConcurrency, 1, 4);
        using var gate = new SemaphoreSlim(max, max);
        var candidates = queue.Where(x => x.Status == AutomationQueueStatus.Queued).OrderBy(x => x.CreatedAt).ToList();
        var batchesFailed = new HashSet<Guid>();
        var sync = new object();

        var tasks = candidates.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                lock (sync)
                {
                    if (settings.StopBatchOnFailure && batchesFailed.Contains(item.BatchId))
                    {
                        item.Status = AutomationQueueStatus.Cancelled;
                        item.Message = "Skipped because another browser in this matrix batch failed.";
                        item.FinishedAt = DateTimeOffset.Now;
                        ItemChanged?.Invoke(item);
                        return;
                    }
                    item.Status = AutomationQueueStatus.Running;
                    item.StartedAt = DateTimeOffset.Now;
                    item.Message = "Starting web suite execution...";
                    ItemChanged?.Invoke(item);
                }

                await PersistQueueAsync(queue);
                var runner = new AutomationHeadlessSuiteRunner();
                var code = await runner.RunAsync(item.ProjectId, item.SuiteId, item.EnvironmentId, false, cancellationToken, item.Browser);
                item.ExitCode = code;
                item.Status = code switch
                {
                    0 => AutomationQueueStatus.Passed,
                    2 => AutomationQueueStatus.NeedsReview,
                    _ => AutomationQueueStatus.Failed
                };
                item.FinishedAt = DateTimeOffset.Now;
                item.Message = item.Status switch
                {
                    AutomationQueueStatus.Passed => "Suite passed.",
                    AutomationQueueStatus.NeedsReview => "Suite completed and requires review.",
                    _ => $"Suite failed with exit code {code}."
                };

                var threshold = item.StartedAt?.AddSeconds(-5) ?? DateTimeOffset.MinValue;
                var latest = (await _suiteRunStore.LoadAllAsync())
                    .Where(r => r.ProjectId == item.ProjectId && r.SuiteId == item.SuiteId && r.Browser.Equals(item.Browser, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.StartedAt).FirstOrDefault(r => r.StartedAt >= threshold);
                if (latest is not null)
                {
                    item.SuiteRunId = latest.Id;
                    item.HtmlReportPath = latest.HtmlReportPath;
                }
                if (settings.StopBatchOnFailure && item.Status == AutomationQueueStatus.Failed)
                    lock (sync) batchesFailed.Add(item.BatchId);
            }
            catch (OperationCanceledException)
            {
                item.Status = AutomationQueueStatus.Aborted;
                item.Message = "Queue execution cancelled.";
                item.FinishedAt = DateTimeOffset.Now;
            }
            catch (Exception ex)
            {
                item.Status = AutomationQueueStatus.Failed;
                item.Message = ex.Message;
                item.FinishedAt = DateTimeOffset.Now;
                if (settings.StopBatchOnFailure) lock (sync) batchesFailed.Add(item.BatchId);
            }
            finally
            {
                ItemChanged?.Invoke(item);
                await PersistQueueAsync(queue);
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        foreach (var batch in candidates.GroupBy(x => x.BatchId))
        {
            var report = await AutomationBrowserMatrixReportService.GenerateAsync(batch);
            foreach (var item in batch) item.MatrixReportPath = report;
        }
        await PersistQueueAsync(queue);
    }

    private async Task PersistQueueAsync(IEnumerable<AutomationQueueItem> queue)
    {
        await _persistGate.WaitAsync();
        try { await _queueStore.SaveAsync(queue); }
        finally { _persistGate.Release(); }
    }
}
