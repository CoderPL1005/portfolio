using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Integrations.Gmail;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class GmailApplicationEmailSenderTests
{
    [Fact]
    public void Test_mode_empty_allowlist_fails_closed_before_network()
    {
        var http = new RecordingHandler();
        var sender = Sender(http, enabled: true, allowed: []);
        var error = Assert.Throws<ConflictException>(() => sender.EnsureCanSend("owner@example.com"));
        Assert.Equal("EMAIL_RECIPIENT_NOT_ALLOWED", error.Code);
        Assert.Equal(0, http.CallCount);
    }

    [Fact]
    public void Disabled_or_non_allowlisted_recipient_fails_before_network()
    {
        var disabledHttp = new RecordingHandler();
        var disabled = Sender(disabledHttp, enabled: false, allowed: ["owner@example.com"]);
        Assert.Equal("EMAIL_SUBMISSION_DISABLED", Assert.Throws<ConflictException>(() => disabled.EnsureCanSend("owner@example.com")).Code);
        Assert.Equal(0, disabledHttp.CallCount);

        var blockedHttp = new RecordingHandler();
        var blocked = Sender(blockedHttp, enabled: true, allowed: ["owner@example.com"]);
        Assert.Equal("EMAIL_RECIPIENT_NOT_ALLOWED", Assert.Throws<ConflictException>(() => blocked.EnsureCanSend("recruiter@example.com")).Code);
        Assert.Equal(0, blockedHttp.CallCount);
    }

    [Fact]
    public async Task Allowed_recipient_refreshes_token_and_sends_mime_pdf_once()
    {
        var http = new RecordingHandler(
            _ => Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\"}"),
            request =>
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
                return Json(HttpStatusCode.OK, "{\"id\":\"gmail-message-1\",\"threadId\":\"thread-1\"}");
            });
        var sender = Sender(http, enabled: true, allowed: ["OWNER@example.com"]);
        var pdf = "%PDF-nội dung CV"u8.ToArray();
        var message = new DeterministicApplicationEmailComposer().Compose(
            "owner@example.com", "sender@example.com", "Nguyễn Đình Phúc", "Công ty Ánh Dương", "Kỹ sư phần mềm",
            "CV Nguyễn Đình Phúc.pdf", "application/pdf", pdf);

        var result = await sender.SendAsync(message);

        Assert.Equal((SubmissionOutcomes.Success, "gmail-message-1"), (result.Outcome, result.ProviderSubmissionId));
        Assert.Equal(2, http.CallCount);
        var rawJson = http.Bodies[1];
        Assert.DoesNotContain("access-token", rawJson);
        using var envelope = JsonDocument.Parse(rawJson);
        var raw = envelope.RootElement.GetProperty("raw").GetString()!;
        Assert.Matches("^[A-Za-z0-9_-]+$", raw);
        Assert.DoesNotContain("=", raw);
        var mime = Encoding.UTF8.GetString(DecodeBase64Url(raw));
        Assert.Contains("From: sender@example.com\r\nTo: owner@example.com\r\n", mime);
        Assert.DoesNotContain("\nTo: owner@example.com\n", mime);
        var subjectHeader = mime.Split("\r\nMIME-Version:", StringSplitOptions.None)[0]["From: sender@example.com\r\nTo: owner@example.com\r\nSubject: ".Length..];
        Assert.Equal(message.Subject, DecodeEncodedWord(subjectHeader));
        Assert.All(subjectHeader.Split("\r\n "), word => Assert.True(word.Length <= 75));
        Assert.Contains("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n", mime);
        Assert.Contains("Content-Type: application/pdf; name*=UTF-8''" + Uri.EscapeDataString(message.AttachmentFileName), mime);
        Assert.Contains("Content-Disposition: attachment; filename*=UTF-8''" + Uri.EscapeDataString(message.AttachmentFileName), mime);
        Assert.Equal(message.AttachmentFileName, Uri.UnescapeDataString(mime.Split("filename*=UTF-8''", StringSplitOptions.None)[1].Split("\r\n")[0]));
        var encodedParts = mime.Split("Content-Transfer-Encoding: base64\r\n\r\n", StringSplitOptions.None);
        Assert.Equal(3, encodedParts.Length);
        var decodedBody = Encoding.UTF8.GetString(Convert.FromBase64String(encodedParts[1].Split("\r\n--", StringSplitOptions.None)[0].Replace("\r\n", string.Empty)));
        Assert.Equal(message.Body.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"), decodedBody);
        var decodedAttachment = Convert.FromBase64String(encodedParts[2].Split("\r\n--", StringSplitOptions.None)[0].Replace("\r\n", string.Empty));
        Assert.Equal(pdf, decodedAttachment);
    }

    [Fact]
    public async Task Definite_gmail_rejection_is_failure_and_timeout_is_unknown_without_retry()
    {
        var rejectedHttp = new RecordingHandler(
            _ => Json(HttpStatusCode.OK, "{\"access_token\":\"token\"}"),
            _ => Json(HttpStatusCode.BadRequest, "{}"));
        var rejected = await Sender(rejectedHttp, true, ["owner@example.com"]).SendAsync(Message());
        Assert.Equal(SubmissionOutcomes.Failure, rejected.Outcome);
        Assert.Equal(2, rejectedHttp.CallCount);

        var timeoutHttp = new RecordingHandler(
            _ => Json(HttpStatusCode.OK, "{\"access_token\":\"token\"}"),
            _ => throw new TaskCanceledException());
        var unknown = await Sender(timeoutHttp, true, ["owner@example.com"]).SendAsync(Message());
        Assert.Equal(SubmissionOutcomes.Unknown, unknown.Outcome);
        Assert.Equal(2, timeoutHttp.CallCount);
    }

    [Fact]
    public async Task Adapter_reads_and_verifies_the_immutable_managed_snapshot()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var bytes = "%PDF-immutable"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var job = new JobPosting { Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Remote",Description="Role",TechnologyStack=System.Text.Json.JsonDocument.Parse("[]"),ApplicationEmail="owner@example.com",SelectionStatus=JobPostingSelectionStatuses.Approved,Version=1 };
        var app = new JobApplication { Id=Guid.NewGuid(),JobPostingId=job.Id,Status=JobApplicationStatuses.Draft,ApplicationEmail="owner@example.com",PackageStatus=JobApplicationPackageStatuses.Finalized,PackageRevision=1,PackageJobPostingVersion=job.Version,PackageManifestHash=new string('a',64),Version=2 };
        var document = new JobApplicationDocument { Id=Guid.NewGuid(),JobApplicationId=app.Id,DocumentType="CV",VersionLabel="Package revision 1",FileName="cv.pdf",StorageKey="private/snapshot.pdf",ContentHash=hash,ContentType="application/pdf",FileSizeBytes=bytes.Length,PackageRevision=1,SourceCanonicalCvVersion=1,Metadata=System.Text.Json.JsonDocument.Parse("{}") };
        db.JobPostings.Add(job);db.JobApplications.Add(app);db.JobApplicationDocuments.Add(document);db.Profiles.Add(new Profile{Id=Guid.NewGuid(),SingletonKey=1,FullName="Nguyen Dinh Phuc"});await db.SaveChangesAsync();
        var transport = new FakeSender();
        var adapter = new GmailSubmissionAdapter(db,new Storage(bytes),transport,new DeterministicApplicationEmailComposer(),
            Options.Create(Settings(true,["owner@example.com"])),NullLogger<GmailSubmissionAdapter>.Instance);
        var request = new SubmissionRequest(Guid.NewGuid(),app.Id,job.Id,"EMAIL",1,app.PackageManifestHash,document.Id,"cv.pdf","application/pdf",bytes.Length,hash,"owner@example.com",null);

        var result = await adapter.SubmitAsync(request);

        Assert.Equal(SubmissionOutcomes.Success,result.Outcome);
        Assert.Equal(bytes,transport.Message!.AttachmentContent);
        Assert.Equal("Application for Developer - Nguyen Dinh Phuc",transport.Message.Subject);
    }

    [Fact]
    public async Task Adapter_rejects_a_job_changed_after_package_finalization_before_storage_or_email()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var bytes = "%PDF-immutable"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var job = new JobPosting { Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Remote",Description="Changed role",TechnologyStack=JsonDocument.Parse("[]"),ApplicationEmail="owner@example.com",SelectionStatus=JobPostingSelectionStatuses.Approved,Version=2 };
        var app = new JobApplication { Id=Guid.NewGuid(),JobPostingId=job.Id,Status=JobApplicationStatuses.Draft,ApplicationEmail="owner@example.com",PackageStatus=JobApplicationPackageStatuses.Finalized,PackageRevision=1,PackageJobPostingVersion=1,PackageManifestHash=new string('a',64),Version=2 };
        var document = new JobApplicationDocument { Id=Guid.NewGuid(),JobApplicationId=app.Id,DocumentType="CV",VersionLabel="Package revision 1",FileName="cv.pdf",StorageKey="private/snapshot.pdf",ContentHash=hash,ContentType="application/pdf",FileSizeBytes=bytes.Length,PackageRevision=1,SourceCanonicalCvVersion=1,Metadata=JsonDocument.Parse("{}") };
        db.JobPostings.Add(job);db.JobApplications.Add(app);db.JobApplicationDocuments.Add(document);await db.SaveChangesAsync();
        var storage = new Storage(bytes);
        var transport = new FakeSender();
        var adapter = new GmailSubmissionAdapter(db,storage,transport,new DeterministicApplicationEmailComposer(),
            Options.Create(Settings(true,["owner@example.com"])),NullLogger<GmailSubmissionAdapter>.Instance);

        var result = await adapter.SubmitAsync(new SubmissionRequest(Guid.NewGuid(),app.Id,job.Id,"EMAIL",1,app.PackageManifestHash,document.Id,"cv.pdf","application/pdf",bytes.Length,hash,"owner@example.com",null));

        Assert.Equal((SubmissionOutcomes.Failure,"EMAIL_AUTHORITATIVE_DATA_CHANGED"),(result.Outcome,result.FailureCode));
        Assert.Equal(0,storage.OpenCalls);
        Assert.Equal(0,transport.SendCalls);
    }

    private static GmailApplicationEmailSender Sender(RecordingHandler handler,bool enabled,string[] allowed) =>
        new(new HttpClient(handler),Options.Create(Settings(enabled,allowed)));
    private static EmailSubmissionOptions Settings(bool enabled,string[] allowed)=>new(){Enabled=enabled,TestMode=true,AllowedRecipients=allowed,SenderEmail="sender@example.com",GoogleClientId="client",GoogleClientSecret="secret",GoogleRefreshToken="refresh"};
    private static ApplicationEmailMessage Message()=>new("owner@example.com","sender@example.com","Subject","Body","cv.pdf","application/pdf","%PDF-test"u8.ToArray());
    private static HttpResponseMessage Json(HttpStatusCode status,string value)=>new(status){Content=new StringContent(value,Encoding.UTF8,"application/json")};
    private static byte[] DecodeBase64Url(string value)
    {var standard=value.Replace('-','+').Replace('_','/');standard+=new string('=',(4-standard.Length%4)%4);return Convert.FromBase64String(standard);}
    private static string DecodeEncodedWord(string value)
    {const string prefix="=?UTF-8?B?";const string suffix="?=";var decoded=new StringBuilder();foreach(var word in value.Split("\r\n ")){Assert.StartsWith(prefix,word,StringComparison.OrdinalIgnoreCase);Assert.EndsWith(suffix,word);decoded.Append(Encoding.UTF8.GetString(Convert.FromBase64String(word[prefix.Length..^suffix.Length])));}return decoded.ToString();}

    private sealed class RecordingHandler(params Func<HttpRequestMessage,HttpResponseMessage>[] responses):HttpMessageHandler
    {
        public int CallCount{get;private set;}public List<string>Bodies{get;}=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Bodies.Add(request.Content is null?string.Empty:await request.Content.ReadAsStringAsync(cancellationToken));var index=CallCount++;return responses[index](request);}
    }
    private sealed class Storage(byte[] bytes):IPrivateFileStorage
    {public int OpenCalls;public Task UploadAsync(string key,Stream content,string type,CancellationToken ct=default)=>throw new NotSupportedException();public Task<Stream> OpenReadAsync(string key,long maximum,CancellationToken ct=default){OpenCalls++;return Task.FromResult<Stream>(new MemoryStream(bytes,false));}public Task DeleteAsync(string key,CancellationToken ct=default)=>throw new NotSupportedException();}
    private sealed class FakeSender:IApplicationEmailSender
    {public int SendCalls;public ApplicationEmailMessage? Message{get;private set;}public void EnsureCanSend(string recipient){}public Task<SubmissionResult> SendAsync(ApplicationEmailMessage message,CancellationToken ct=default){SendCalls++;Message=message;return Task.FromResult(new SubmissionResult("SUCCESS","gmail-id",null,null));}}
}
