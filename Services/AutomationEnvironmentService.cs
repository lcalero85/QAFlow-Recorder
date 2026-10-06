using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationEnvironmentService
{
    public static AutomationProject Apply(AutomationProject source, AutomationEnvironmentProfile? environment)
    {
        if (environment is null) return source;
        return new AutomationProject
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            BaseUrl = string.IsNullOrWhiteSpace(environment.BaseUrl) ? source.BaseUrl : environment.BaseUrl,
            AllowedDomains = string.IsNullOrWhiteSpace(environment.AllowedDomains) ? source.AllowedDomains : environment.AllowedDomains,
            DefaultBrowser = string.IsNullOrWhiteSpace(environment.Browser) ? source.DefaultBrowser : environment.Browser,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Suites = source.Suites,
            DataSets = source.DataSets,
            Components = source.Components,
            Environments = source.Environments,
            Schedules = source.Schedules,
            EmailDelivery = source.EmailDelivery
        };
    }
}
