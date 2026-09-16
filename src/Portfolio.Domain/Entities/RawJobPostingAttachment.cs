using Portfolio.Domain.Constants;

namespace Portfolio.Domain.Entities;

public sealed class RawJobPostingAttachment
{
    public Guid Id { get; set; }
    public Guid RawJobPostingId { get; set; }
    public string AttachmentType { get; set; } = RawJobPostingAttachmentTypes.Image;
    public string StorageKey { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public long SortOrder { get; set; }
    public long TelegramMessageId { get; set; }
    public string TelegramFileId { get; set; } = null!;
    public string TelegramFileUniqueId { get; set; } = null!;
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public RawJobPosting RawJobPosting { get; set; } = null!;
}
