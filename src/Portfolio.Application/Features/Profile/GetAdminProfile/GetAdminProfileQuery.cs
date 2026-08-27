using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Profile.GetAdminProfile;

public sealed record GetAdminProfileQuery : IRequest<AdminProfileResult>;
