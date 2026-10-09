using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationHeadlessSuiteRunner
{
    public async Task<int> RunAsync(Guid projectId, Guid suiteId, Guid? environmentId, bool sendReport = true, CancellationToken ct = default, string? browserOverride = null, bool browserHeadless = false)
    {
        var workspaceStore = new AutomationWorkspaceStore();
        var projects = await workspaceStore.LoadProjectsAsync();
        var source = projects.FirstOrDefault(p => p.Id == projectId);
        if (source is null) return 11;
        var suite = source.Suites.FirstOrDefault(s => s.Id == suiteId);
        if (suite is null) return 12;
        var environment = environmentId.HasValue ? source.Environments.FirstOrDefault(e => e.Id == environmentId.Value) : source.Environments.FirstOrDefault(e => e.IsDefault);
        var project = AutomationEnvironmentService.Apply(source, environment);
        if (!string.IsNullOrWhiteSpace(browserOverride)) project.DefaultBrowser = browserOverride.Trim();
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return 13;
        if (!await new AutomationGovernanceService().IsExecutionAllowedAsync(source.Id, suite.TestCases)) return 14;

        var runStore = new AutomationRunStore();
        var suiteStore = new AutomationSuiteRunStore();
        var suiteRun = new AutomationSuiteRun
        {
            ProjectId = project.Id, SuiteId = suite.Id, ProjectName = project.Name, SuiteName = suite.Name,
            BaseUrl = project.BaseUrl, Browser = project.DefaultBrowser, EnvironmentName = environment?.Name ?? "Default",
            IsScheduled = true, Status = AutomationSuiteRunStatus.Running, StartedAt = DateTimeOffset.Now
        };
        suiteStore.CreateRunFolder(suiteRun); await suiteStore.SaveAsync(suiteRun);
        try
        {
            var defaultData = project.DataSets.FirstOrDefault(d => d.Id == environment?.DefaultDataSetId);
            var before = AutomationLifecycleService.Component(project, suite.BeforeSuiteComponentId);
            if (before is not null)
            {
                var fixture = AutomationLifecycleService.CreateSuiteFixture(project, before.Id, "SETUP", before.Name);
                var service = new WebAutomationExecutionService(runStore);
                var run = await service.ExecuteAsync(project, suite, fixture, defaultData, _ => Task.FromResult(false), (_, _) => { }, ct, browserHeadless);
                suiteRun.Cases.Add(new AutomationSuiteCaseResult { TestCaseId = fixture.Id, TestRunId = run.Id, TestCaseKey = fixture.Key, TestCaseName = fixture.Name, DataSetName = defaultData?.Name ?? "", LifecycleStage = "BeforeSuite", Status = run.Status, PassRate = run.PassRate, Duration = run.DurationDisplay, HtmlReportPath = run.HtmlReportPath, FailureMessage = run.FailureMessage });
                if (run.Status != AutomationRunStatus.Passed) throw new InvalidOperationException("Before Suite fixture failed; test cases were skipped.");
            }

            foreach (var tc in suite.TestCases.Where(c => c.Status != AutomationCaseStatus.Archived).OrderBy(c => c.Key).ThenBy(c => c.Name))
            {
                ct.ThrowIfCancellationRequested();
                if (!AutomationLifecycleService.DependencySatisfied(tc, suiteRun.Cases))
                {
                    suiteRun.Cases.Add(new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, Status = AutomationRunStatus.Blocked, FailureMessage = "Required dependency did not pass." });
                    continue;
                }
                if (tc.Steps.Count == 0)
                {
                    suiteRun.Cases.Add(new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, Status = AutomationRunStatus.Blocked, FailureMessage = "Test case has no steps." });
                    continue;
                }
                var fallback = defaultData ?? project.DataSets.FirstOrDefault(d => d.Id == tc.DefaultDataSetId);
                foreach (var dataSet in AutomationLifecycleService.ResolveDataIterations(project, tc, fallback))
                {
                    var service = new WebAutomationExecutionService(runStore);
                    var testRun = await service.ExecuteAsync(project, suite, tc, dataSet, _ => Task.FromResult(false), (_, _) => { }, ct, browserHeadless);
                    suiteRun.Cases.Add(new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestRunId = testRun.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, DataSetName = dataSet?.Name ?? "", Status = testRun.Status, PassRate = testRun.PassRate, Duration = testRun.DurationDisplay, HtmlReportPath = testRun.HtmlReportPath, FailureMessage = testRun.FailureMessage });
                    await suiteStore.SaveAsync(suiteRun);
                }
            }
            var normal = suiteRun.Cases.Where(c => string.IsNullOrWhiteSpace(c.LifecycleStage)).ToList();
            suiteRun.Status = normal.Any(c => c.Status == AutomationRunStatus.Failed) ? AutomationSuiteRunStatus.Failed : normal.Any(c => c.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked) ? AutomationSuiteRunStatus.NeedsReview : AutomationSuiteRunStatus.Passed;
        }
        catch (OperationCanceledException) { suiteRun.Status = AutomationSuiteRunStatus.Aborted; suiteRun.FailureMessage = "Execution cancelled."; }
        catch (Exception ex) { suiteRun.Status = AutomationSuiteRunStatus.Failed; suiteRun.FailureMessage = ex.Message; }
        finally
        {
            try
            {
                var after = AutomationLifecycleService.Component(project, suite.AfterSuiteComponentId);
                if (after is not null)
                {
                    var fixture = AutomationLifecycleService.CreateSuiteFixture(project, after.Id, "TEARDOWN", after.Name);
                    var service = new WebAutomationExecutionService(runStore);
                    var data = project.DataSets.FirstOrDefault(d => d.Id == environment?.DefaultDataSetId);
                    var run = await service.ExecuteAsync(project, suite, fixture, data, _ => Task.FromResult(false), (_, _) => { }, CancellationToken.None, browserHeadless);
                    suiteRun.Cases.Add(new AutomationSuiteCaseResult { TestCaseId = fixture.Id, TestRunId = run.Id, TestCaseKey = fixture.Key, TestCaseName = fixture.Name, DataSetName = data?.Name ?? "", LifecycleStage = "AfterSuite", Status = run.Status, PassRate = run.PassRate, Duration = run.DurationDisplay, HtmlReportPath = run.HtmlReportPath, FailureMessage = run.FailureMessage });
                    if (run.Status != AutomationRunStatus.Passed && suiteRun.Status == AutomationSuiteRunStatus.Passed) suiteRun.Status = AutomationSuiteRunStatus.NeedsReview;
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(suiteRun.FailureMessage)) suiteRun.FailureMessage = "After Suite: " + ex.Message;
                if (suiteRun.Status == AutomationSuiteRunStatus.Passed) suiteRun.Status = AutomationSuiteRunStatus.NeedsReview;
            }
            suiteRun.FinishedAt = DateTimeOffset.Now;
            suiteRun.HtmlReportPath = await AutomationSuiteHtmlReportService.GenerateAsync(suiteRun);
            await suiteStore.SaveAsync(suiteRun);
            if (sendReport && project.EmailDelivery?.Enabled == true)
            {
                try { await new AutomationReportDeliveryService().SendSuiteRunAsync(project, suiteRun); } catch { }
            }
        }
        return suiteRun.Status == AutomationSuiteRunStatus.Passed ? 0 : suiteRun.Status == AutomationSuiteRunStatus.NeedsReview ? 2 : 1;
    }
}
