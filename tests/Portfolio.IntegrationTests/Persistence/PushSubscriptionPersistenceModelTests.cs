using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector.EntityFrameworkCore;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class PushSubscriptionPersistenceModelTests
{
    [Fact]
    public void Model_maps_sensitive_subscription_fields_and_unique_endpoint()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(PushSubscription))!;

        Assert.Equal("push_subscriptions", entity.GetTableName());
        Assert.Equal("character varying(2048)", entity.FindProperty(nameof(PushSubscription.Endpoint))!.GetColumnType());
        Assert.Equal(2048, entity.FindProperty(nameof(PushSubscription.Endpoint))!.GetMaxLength());
        Assert.Equal(512, entity.FindProperty(nameof(PushSubscription.P256dh))!.GetMaxLength());
        Assert.Equal(512, entity.FindProperty(nameof(PushSubscription.Auth))!.GetMaxLength());
        var unique = entity.GetIndexes().Single(index => index.GetDatabaseName() == "uq_push_subscriptions_endpoint");
        Assert.True(unique.IsUnique);
        Assert.Equal([nameof(PushSubscription.Endpoint)], unique.Properties.Select(property => property.Name));
        Assert.Equal("is_active = TRUE", entity.GetIndexes()
            .Single(index => index.GetDatabaseName() == "ix_push_subscriptions_active").GetFilter());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_push_model_test", npgsql => npgsql.UseVector())
            .Options;
        return new ApplicationDbContext(options);
    }
}
