using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;

public sealed record GetPublicPortfolioQuery : IRequest<PortfolioHomeResult>;
