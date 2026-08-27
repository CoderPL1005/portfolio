using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.Phase4C;

namespace Portfolio.Application.Features.Dashboard;

public sealed record GetDashboardQuery : IRequest<DashboardResult>;

public sealed class GetDashboardQueryHandler(IApplicationDbContext db) : IRequestHandler<GetDashboardQuery, DashboardResult>
{
    public async Task<DashboardResult> HandleAsync(GetDashboardQuery r, CancellationToken ct = default)
    {
        var projects = await db.Projects.CountAsync(ct); var experiences = await db.Experiences.CountAsync(ct);
        var skills = await db.Skills.CountAsync(ct); var certificates = await db.Certificates.CountAsync(ct);
        var unread = await db.ContactMessages.CountAsync(x => x.Status == "NEW", ct);
        var knowledge = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.IndexingStatus == "INDEXED" || x.IndexingStatus == "PENDING" || x.IndexingStatus == "FAILED").GroupBy(x => x.IndexingStatus).Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        var conversations = await db.ChatSessions.CountAsync(ct);
        var recent = new List<RecentUpdateResult>();
        recent.AddRange(await db.Projects.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("PROJECT", x.Id, x.Title, x.UpdatedAt)).ToListAsync(ct));
        recent.AddRange(await db.Experiences.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("EXPERIENCE", x.Id, x.CompanyName + " — " + x.RoleTitle, x.UpdatedAt)).ToListAsync(ct));
        recent.AddRange(await db.Skills.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("SKILL", x.Id, x.Name, x.UpdatedAt)).ToListAsync(ct));
        recent.AddRange(await db.Certificates.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("CERTIFICATE", x.Id, x.Name, x.UpdatedAt)).ToListAsync(ct));
        recent.AddRange(await db.JourneyItems.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("JOURNEY", x.Id, x.Title, x.UpdatedAt)).ToListAsync(ct));
        recent.AddRange(await db.SocialLinks.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(10).Select(x => new RecentUpdateResult("SOCIAL_LINK", x.Id, x.Platform, x.UpdatedAt)).ToListAsync(ct));
        return new(projects, experiences, skills, certificates, unread,
            new(knowledge.FirstOrDefault(x => x.Status == "INDEXED")?.Count ?? 0,
                knowledge.FirstOrDefault(x => x.Status == "PENDING")?.Count ?? 0,
                knowledge.FirstOrDefault(x => x.Status == "FAILED")?.Count ?? 0),
            conversations, recent.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.ResourceType)
                .ThenBy(x => x.Id).Take(10).ToList());
    }
}
