using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Portfolio.Application.Common.Configuration;

public sealed class EmailSubmissionOptions
{
    public const string SectionName = "EmailSubmission";
    public bool Enabled { get; set; }
    public bool TestMode { get; set; } = true;
    public string[] AllowedRecipients { get; set; } = [];
    public string SenderEmail { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string GoogleRefreshToken { get; set; } = string.Empty;
}

public sealed class EmailSubmissionOptionsValidator : IValidateOptions<EmailSubmissionOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailSubmissionOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var failures = new List<string>();
        if (!ValidEmail(options.SenderEmail)) failures.Add("EmailSubmission:SenderEmail must be a valid email address.");
        if (string.IsNullOrWhiteSpace(options.GoogleClientId)) failures.Add("EmailSubmission:GoogleClientId is required when enabled.");
        if (string.IsNullOrWhiteSpace(options.GoogleClientSecret)) failures.Add("EmailSubmission:GoogleClientSecret is required when enabled.");
        if (string.IsNullOrWhiteSpace(options.GoogleRefreshToken)) failures.Add("EmailSubmission:GoogleRefreshToken is required when enabled.");
        if (options.AllowedRecipients.Any(value => !ValidEmail(value))) failures.Add("EmailSubmission:AllowedRecipients contains an invalid email address.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value.Trim(), out var parsed)
        && string.Equals(parsed.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
}
