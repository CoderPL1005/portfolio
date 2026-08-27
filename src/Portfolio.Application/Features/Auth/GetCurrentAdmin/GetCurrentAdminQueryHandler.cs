using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.Auth.GetCurrentAdmin;

public sealed class GetCurrentAdminQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser) : IRequestHandler<GetCurrentAdminQuery, CurrentAdminResult>
{
    public async Task<CurrentAdminResult> HandleAsync(
        GetCurrentAdminQuery request,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.AdminUserId is null)
        {
            throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        }

        var admin = await dbContext.AdminUsers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == currentUser.AdminUserId.Value, cancellationToken);
        if (admin is null)
        {
            throw new UnauthorizedException("UNAUTHORIZED", "Authentication is invalid.");
        }

        if (!admin.IsActive)
        {
            throw new ForbiddenException("ADMIN_DISABLED", "The admin account is disabled.");
        }

        return new CurrentAdminResult(admin.Id, admin.Email, admin.FullName, admin.LastLoginAt);
    }
}
