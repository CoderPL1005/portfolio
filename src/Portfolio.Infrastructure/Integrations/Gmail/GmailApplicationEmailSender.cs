using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;

namespace Portfolio.Infrastructure.Integrations.Gmail;

public sealed class GmailApplicationEmailSender(
    HttpClient client,
    IOptions<EmailSubmissionOptions> configured) : IApplicationEmailSender
{
    private readonly EmailSubmissionOptions options = configured.Value;

    public void EnsureCanSend(string recipient)
    {
        if (!options.Enabled)
            throw new ConflictException("EMAIL_SUBMISSION_DISABLED", "Email submission is disabled.");
        if (!EmailSubmissionOptionsValidator.ValidEmail(recipient))
            throw new ConflictException("APPLICATION_EMAIL_INVALID", "The application email is invalid.");
        if (options.TestMode && !options.AllowedRecipients.Any(item =>
                string.Equals(item.Trim(), recipient.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("EMAIL_RECIPIENT_NOT_ALLOWED", "The application email is not allowed in test mode.");
    }

    public async Task<SubmissionResult> SendAsync(ApplicationEmailMessage message, CancellationToken cancellationToken = default)
    {
        EnsureCanSend(message.Recipient);
        if (!string.Equals(message.Sender, options.SenderEmail, StringComparison.OrdinalIgnoreCase))
            return Failure("EMAIL_SENDER_MISMATCH", "The configured sender identity does not match the email message.");

        string accessToken;
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.GoogleClientId,
                    ["client_secret"] = options.GoogleClientSecret,
                    ["refresh_token"] = options.GoogleRefreshToken,
                    ["grant_type"] = "refresh_token",
                }),
            };
            using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
                return Failure("GMAIL_OAUTH_FAILED", "Gmail authorization failed before the message was sent.");
            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
            accessToken = tokenJson.RootElement.TryGetProperty("access_token", out var value)
                ? value.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(accessToken))
                return Failure("GMAIL_OAUTH_FAILED", "Gmail authorization returned no access token.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown("GMAIL_OAUTH_TIMEOUT", "Gmail authorization timed out before sending.");
        }
        catch (HttpRequestException)
        {
            return Unknown("GMAIL_OAUTH_UNAVAILABLE", "Gmail authorization was unavailable before sending.");
        }
        catch (JsonException)
        {
            return Failure("GMAIL_OAUTH_INVALID_RESPONSE", "Gmail authorization returned an invalid response.");
        }

        try
        {
            var raw = Base64Url(Encoding.UTF8.GetBytes(BuildMime(message)));
            using var sendRequest = new HttpRequestMessage(HttpMethod.Post,
                "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
            sendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            sendRequest.Content = JsonContent.Create(new { raw });
            using var response = await client.SendAsync(sendRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout
                    ? Unknown("GMAIL_SEND_AMBIGUOUS", "Gmail did not return a trustworthy delivery result.")
                    : Failure("GMAIL_SEND_REJECTED", "Gmail rejected the message before confirming delivery.");
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var id = json.RootElement.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            return string.IsNullOrWhiteSpace(id)
                ? Unknown("GMAIL_SEND_INVALID_RESPONSE", "Gmail accepted the request but returned no message identifier.")
                : new(SubmissionOutcomes.Success, id, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown("GMAIL_SEND_TIMEOUT", "The Gmail send request timed out and requires reconciliation.");
        }
        catch (HttpRequestException)
        {
            return Unknown("GMAIL_SEND_UNAVAILABLE", "The Gmail send result is unknown and requires reconciliation.");
        }
        catch (JsonException)
        {
            return Unknown("GMAIL_SEND_INVALID_RESPONSE", "Gmail returned an unreadable send result.");
        }
    }

    internal static string BuildMime(ApplicationEmailMessage message)
    {
        static string Header(string value) => value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        static string EncodedWord(string value)
        {
            const int maximumPayloadBytes = 45;
            var words = new List<string>();
            var segment = new StringBuilder();
            var segmentBytes = 0;
            foreach (var rune in Header(value).EnumerateRunes())
            {
                var runeText = rune.ToString();
                var runeBytes = Encoding.UTF8.GetByteCount(runeText);
                if (segmentBytes > 0 && segmentBytes + runeBytes > maximumPayloadBytes)
                {
                    words.Add($"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(segment.ToString()))}?=");
                    segment.Clear();
                    segmentBytes = 0;
                }
                segment.Append(runeText);
                segmentBytes += runeBytes;
            }
            words.Add($"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(segment.ToString()))}?=");
            return string.Join("\r\n ", words);
        }
        static string EncodedParameter(string value) => Uri.EscapeDataString(Header(value));
        static string NormalizeCrlf(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        static string Lines(string value) => string.Join("\r\n", Enumerable.Range(0, (value.Length + 75) / 76)
            .Select(index => value.Substring(index * 76, Math.Min(76, value.Length - index * 76))));
        var boundary = "portfolio-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(message.Recipient + "\n" + message.Subject + "\n" + message.AttachmentFileName)))[..24].ToLowerInvariant();
        var body = Lines(Convert.ToBase64String(Encoding.UTF8.GetBytes(NormalizeCrlf(message.Body))));
        var attachment = Lines(Convert.ToBase64String(message.AttachmentContent));
        var encodedFileName = EncodedParameter(message.AttachmentFileName);
        return $"From: {Header(message.Sender)}\r\nTo: {Header(message.Recipient)}\r\nSubject: {EncodedWord(message.Subject)}\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n--{boundary}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{body}\r\n--{boundary}\r\nContent-Type: {Header(message.AttachmentContentType)}; name*=UTF-8''{encodedFileName}\r\nContent-Disposition: attachment; filename*=UTF-8''{encodedFileName}\r\nContent-Transfer-Encoding: base64\r\n\r\n{attachment}\r\n--{boundary}--\r\n";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static SubmissionResult Failure(string code, string message) => new(SubmissionOutcomes.Failure, null, code, message);
    private static SubmissionResult Unknown(string code, string message) => new(SubmissionOutcomes.Unknown, null, code, message);
}
