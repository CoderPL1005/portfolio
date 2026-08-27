namespace Portfolio.Application.Common.Abstractions.Authentication;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? AdminUserId { get; }
    string? Email { get; }
}
