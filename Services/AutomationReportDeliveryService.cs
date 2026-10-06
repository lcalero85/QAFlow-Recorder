using System.Net;
using System.Net.Mail;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationReportDeliveryService
{
    public async Task SendSuiteRunAsync(AutomationProject project, AutomationSuiteRun run, IEnumerable<string>? extraPdfPaths = null, CancellationToken cancellationToken = default)
    {
        var cfg = project.EmailDelivery ?? new();
        Validate(cfg);
        var password = CredentialManagerService.ReadSecret(cfg.PasswordSecretKey);
        if (!string.IsNullOrWhiteSpace(cfg.Username) && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("SMTP password is not available in Windows Credential Manager.");

        using var message = new MailMessage { From = new MailAddress(cfg.FromAddress), Subject = $"QAFlow · {run.ProjectName} · {run.SuiteName} · {run.Status}" };
        foreach (var recipient in SplitAddresses(cfg.Recipients)) message.To.Add(recipient);
        message.Body = $"QAFlow suite execution completed.\r\n\r\nProject: {run.ProjectName}\r\nSuite: {run.SuiteName}\r\nStatus: {run.Status}\r\nPass rate: {run.PassRate}%\r\nPassed: {run.PassedCases}\r\nFailed: {run.FailedCases}\r\nNeeds review: {run.ReviewCases}\r\nDuration: {run.DurationDisplay}\r\n";

        if (cfg.AttachSuiteHtml && File.Exists(run.HtmlReportPath)) message.Attachments.Add(new Attachment(run.HtmlReportPath));
        if (cfg.AttachCaseHtml)
            foreach (var path in run.Cases.Select(c => c.HtmlReportPath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) message.Attachments.Add(new Attachment(path));

        var pdfs = (extraPdfPaths ?? []).Concat(cfg.PdfAttachments ?? []).Where(File.Exists).Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in pdfs) message.Attachments.Add(new Attachment(path));

        using var smtp = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort) { EnableSsl = cfg.UseSsl, DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false };
        if (!string.IsNullOrWhiteSpace(cfg.Username)) smtp.Credentials = new NetworkCredential(cfg.Username, password);
        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message);
    }

    public async Task SendTestAsync(AutomationProject project, CancellationToken cancellationToken = default)
    {
        var cfg = project.EmailDelivery ?? new(); Validate(cfg);
        var password = CredentialManagerService.ReadSecret(cfg.PasswordSecretKey);
        using var message = new MailMessage { From = new MailAddress(cfg.FromAddress), Subject = "QAFlow · Report delivery test", Body = "QAFlow SMTP configuration is working correctly." };
        foreach (var recipient in SplitAddresses(cfg.Recipients)) message.To.Add(recipient);
        using var smtp = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort) { EnableSsl = cfg.UseSsl, DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false };
        if (!string.IsNullOrWhiteSpace(cfg.Username)) smtp.Credentials = new NetworkCredential(cfg.Username, password);
        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message);
    }

    private static void Validate(AutomationEmailDeliverySettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.SmtpHost)) throw new InvalidOperationException("SMTP host is required.");
        if (cfg.SmtpPort is < 1 or > 65535) throw new InvalidOperationException("SMTP port is invalid.");
        if (string.IsNullOrWhiteSpace(cfg.FromAddress)) throw new InvalidOperationException("From address is required.");
        if (!SplitAddresses(cfg.Recipients).Any()) throw new InvalidOperationException("At least one recipient is required.");
    }

    private static IEnumerable<string> SplitAddresses(string value) => (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
