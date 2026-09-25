namespace Portfolio.Application.Common.Abstractions.Submission;

public sealed record ApplicationEmailMessage(
    string Recipient,
    string Sender,
    string Subject,
    string Body,
    string AttachmentFileName,
    string AttachmentContentType,
    byte[] AttachmentContent);

public interface IApplicationEmailSender
{
    void EnsureCanSend(string recipient);
    Task<SubmissionResult> SendAsync(
        ApplicationEmailMessage message,
        CancellationToken cancellationToken = default);
}

public interface IApplicationEmailComposer
{
    ApplicationEmailMessage Compose(
        string recipient,
        string sender,
        string candidateName,
        string companyName,
        string positionTitle,
        string attachmentFileName,
        string attachmentContentType,
        byte[] attachmentContent);
}
