using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly string _projectsFile;
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    public AutomationWorkspaceStore()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation");
        _projectsFile = Path.Combine(_root, "projects.json");
    }

    public async Task<List<AutomationProject>> LoadProjectsAsync()
    {
        try
        {
            if (!File.Exists(_projectsFile)) return [];
            var json = await File.ReadAllTextAsync(_projectsFile);
            var projects = JsonSerializer.Deserialize<List<AutomationProject>>(json, JsonOptions) ?? [];
            foreach (var project in projects)
            {
                project.DataSets ??= []; project.Components ??= []; project.Environments ??= []; project.Schedules ??= []; project.EmailDelivery ??= new AutomationEmailDeliverySettings();
                if (project.Environments.Count == 0 && Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out _))
                    project.Environments.Add(new AutomationEnvironmentProfile { Name = "Default", BaseUrl = project.BaseUrl, AllowedDomains = project.AllowedDomains, Browser = project.DefaultBrowser, IsDefault = true });
            }
            return projects;
        }
        catch (Exception ex)
        {
            TechnicalLogService.Error(nameof(AutomationWorkspaceStore), "LoadProjects", ex);
            return [];
        }
    }

    public async Task SaveProjectsAsync(IEnumerable<AutomationProject> projects)
    {
        await SaveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            var normalized = projects.ToList();
        foreach (var project in normalized)
        {
            project.UpdatedAt = DateTimeOffset.Now;
            project.Suites = project.Suites.OrderBy(s => s.Order).ThenBy(s => s.Name).ToList();
            project.DataSets ??= [];
            project.Components ??= [];
            project.Environments ??= [];
            project.Schedules ??= [];
            project.EmailDelivery ??= new AutomationEmailDeliverySettings();
            if (project.Environments.Count == 0 && Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out _))
                project.Environments.Add(new AutomationEnvironmentProfile { Name = "Default", BaseUrl = project.BaseUrl, AllowedDomains = project.AllowedDomains, Browser = project.DefaultBrowser, IsDefault = true });
            project.EmailDelivery.PdfAttachments ??= [];
            foreach (var dataSet in project.DataSets) dataSet.Variables ??= [];
            foreach (var tc in project.Suites.SelectMany(x => x.TestCases)) tc.DataSetIds ??= [];
            foreach (var suite in project.Suites)
            {
                suite.TestCases = suite.TestCases.OrderBy(t => t.Key).ThenBy(t => t.Name).ToList();
                foreach (var testCase in suite.TestCases)
                    testCase.Steps = testCase.Steps.OrderBy(s => s.Order).ToList();
            }
        }
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            var tempFile = _projectsFile + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _projectsFile, true);
        }
        catch (Exception ex)
        {
            TechnicalLogService.Error(nameof(AutomationWorkspaceStore), "SaveProjects", ex);
            throw;
        }
        finally
        {
            SaveGate.Release();
        }
    }
}
