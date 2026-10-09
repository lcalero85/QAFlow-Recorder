using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationLifecycleService
{
    public static ReusableAutomationComponent? Component(AutomationProject project, Guid? id) =>
        id.HasValue ? project.Components.FirstOrDefault(c => c.Id == id.Value) : null;

    public static AutomationTestCase CreateSuiteFixture(AutomationProject project, Guid? componentId, string key, string name)
    {
        var component = Component(project, componentId);
        return new AutomationTestCase
        {
            Key = key,
            Name = name,
            Description = component?.Description ?? "",
            Mode = AutomationCaseMode.Automated,
            Status = AutomationCaseStatus.Ready,
            Version = 1,
            Steps = component?.Steps ?? []
        };
    }

    public static List<AutomationDataSet?> ResolveDataIterations(AutomationProject project, AutomationTestCase testCase, AutomationDataSet? fallback)
    {
        if (testCase.DataDrivenEnabled && testCase.DataSetIds.Count > 0)
        {
            var selected = testCase.DataSetIds.Select(id => project.DataSets.FirstOrDefault(d => d.Id == id)).Where(d => d is not null).Cast<AutomationDataSet?>().ToList();
            if (selected.Count > 0) return selected;
        }
        return [fallback ?? project.DataSets.FirstOrDefault(d => d.Id == testCase.DefaultDataSetId)];
    }

    public static bool DependencySatisfied(AutomationTestCase testCase, IEnumerable<AutomationSuiteCaseResult> completed)
    {
        if (!testCase.RequiredTestCaseId.HasValue) return true;
        var results = completed.Where(r => r.TestCaseId == testCase.RequiredTestCaseId.Value && string.IsNullOrWhiteSpace(r.LifecycleStage)).ToList();
        return results.Count > 0 && results.All(r => r.Status == AutomationRunStatus.Passed);
    }

    public static bool WouldCreateDependencyCycle(AutomationSuite suite, AutomationTestCase testCase, Guid? dependencyId)
    {
        if (!dependencyId.HasValue) return false;
        var seen = new HashSet<Guid> { testCase.Id };
        var current = dependencyId;
        while (current.HasValue)
        {
            if (!seen.Add(current.Value)) return true;
            current = suite.TestCases.FirstOrDefault(c => c.Id == current.Value)?.RequiredTestCaseId;
        }
        return false;
    }
}
