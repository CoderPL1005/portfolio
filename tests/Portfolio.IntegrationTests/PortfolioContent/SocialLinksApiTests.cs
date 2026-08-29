using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Application.Features.SocialLinks;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class SocialLinksApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Duplicate_platforms_can_be_created_listed_published_and_updated()
    {
        using var socialLinksFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRequestHandler<CreateSocialLinkCommand, SocialLinkResult>>();
                services.RemoveAll<IRequestHandler<UpdateSocialLinkCommand, SocialLinkResult>>();
                services.RemoveAll<IRequestHandler<GetSocialLinksQuery, IReadOnlyCollection<SocialLinkResult>>>();
                services.RemoveAll<IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>>();
                services.AddSingleton<SocialLinkApiState>();
                services.AddScoped<IRequestHandler<CreateSocialLinkCommand, SocialLinkResult>, CreateSocialLinkHandler>();
                services.AddScoped<IRequestHandler<UpdateSocialLinkCommand, SocialLinkResult>, UpdateSocialLinkHandler>();
                services.AddScoped<IRequestHandler<GetSocialLinksQuery, IReadOnlyCollection<SocialLinkResult>>, GetSocialLinksHandler>();
                services.AddScoped<IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>, GetPublicPortfolioHandler>();
            }));
        using var adminClient = socialLinksFactory.CreateClient();
        using (var scope = socialLinksFactory.Services.CreateScope())
        {
            var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
                .CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com").Value;
            adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var firstResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/social-links", new
        {
            platform = "GitHub", label = "CoderPL1005", url = "https://github.com/CoderPL1005",
            iconKey = "github", displayOrder = 1, isVisible = true
        });
        var secondResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/social-links", new
        {
            platform = "GitHub", label = "PhucND3009", url = "https://github.com/PhucND3009",
            iconKey = "github", displayOrder = 2, isVisible = true
        });

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var firstId = await ReadCreatedId(firstResponse);
        var secondId = await ReadCreatedId(secondResponse);
        Assert.NotEqual(firstId, secondId);

        var adminList = await ReadSocialLinks(await adminClient.GetAsync("/api/v1/admin/social-links"));
        Assert.Equal(new[] { firstId, secondId }, adminList.Select(item => item.GetProperty("id").GetGuid()));
        Assert.All(adminList, item => Assert.Equal("GitHub", item.GetProperty("platform").GetString()));

        var updateResponse = await adminClient.PutAsJsonAsync($"/api/v1/admin/social-links/{firstId}", new
        {
            platform = "GitHub", label = "Primary GitHub", url = "https://github.com/CoderPL1005",
            iconKey = "github", displayOrder = 1, isVisible = true
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var anonymousClient = socialLinksFactory.CreateClient();
        var publicLinks = await ReadPortfolioSocialLinks(await anonymousClient.GetAsync("/api/v1/public/portfolio"));
        Assert.Equal(new[] { firstId, secondId }, publicLinks.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(
            new[] { "https://github.com/CoderPL1005", "https://github.com/PhucND3009" },
            publicLinks.Select(item => item.GetProperty("url").GetString()));
    }

    private static async Task<Guid> ReadCreatedId(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement[]> ReadSocialLinks(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static async Task<JsonElement[]> ReadPortfolioSocialLinks(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("socialLinks")
            .EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    public sealed class SocialLinkApiState
    {
        public List<SocialLinkResult> Items { get; } = [];
    }

    public sealed class CreateSocialLinkHandler(SocialLinkApiState state)
        : IRequestHandler<CreateSocialLinkCommand, SocialLinkResult>
    {
        public Task<SocialLinkResult> HandleAsync(CreateSocialLinkCommand request, CancellationToken cancellationToken = default)
        {
            var result = new SocialLinkResult(Guid.NewGuid(), request.Platform.Trim(), request.Label?.Trim(),
                request.Url.Trim(), request.IconKey?.Trim(), request.DisplayOrder, request.IsVisible,
                DateTimeOffset.UtcNow);
            state.Items.Add(result);
            return Task.FromResult(result);
        }
    }

    public sealed class UpdateSocialLinkHandler(SocialLinkApiState state)
        : IRequestHandler<UpdateSocialLinkCommand, SocialLinkResult>
    {
        public Task<SocialLinkResult> HandleAsync(UpdateSocialLinkCommand request, CancellationToken cancellationToken = default)
        {
            var index = state.Items.FindIndex(item => item.Id == request.Id);
            var result = new SocialLinkResult(request.Id, request.Platform.Trim(), request.Label?.Trim(),
                request.Url.Trim(), request.IconKey?.Trim(), request.DisplayOrder, request.IsVisible,
                DateTimeOffset.UtcNow);
            state.Items[index] = result;
            return Task.FromResult(result);
        }
    }

    public sealed class GetSocialLinksHandler(SocialLinkApiState state)
        : IRequestHandler<GetSocialLinksQuery, IReadOnlyCollection<SocialLinkResult>>
    {
        public Task<IReadOnlyCollection<SocialLinkResult>> HandleAsync(GetSocialLinksQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<SocialLinkResult>>(
                state.Items.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id).ToArray());
    }

    public sealed class GetPublicPortfolioHandler(SocialLinkApiState state)
        : IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>
    {
        public Task<PortfolioHomeResult> HandleAsync(GetPublicPortfolioQuery request, CancellationToken cancellationToken = default)
        {
            var links = state.Items.Where(item => item.IsVisible)
                .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
                .Select(item => new PublicSocialLinkResult(item.Id, item.Platform, item.Label, item.Url, item.IconKey))
                .ToArray();
            return Task.FromResult(new PortfolioHomeResult(
                new PublicProfileResult("Owner", null, null, null, null, null, null, null,
                    null, null, null, null, null),
                [], [], [], [], [], [], [], links));
        }
    }
}
