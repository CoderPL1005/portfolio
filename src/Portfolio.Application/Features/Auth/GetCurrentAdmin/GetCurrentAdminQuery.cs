using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Features.Auth.GetCurrentAdmin;

public sealed record GetCurrentAdminQuery : IRequest<CurrentAdminResult>;
