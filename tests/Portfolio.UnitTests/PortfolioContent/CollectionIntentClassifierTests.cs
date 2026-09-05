using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Features.Chat;
using Portfolio.Infrastructure.AI;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class CollectionIntentClassifierTests
{
    [Theory]
    [InlineData("Ph\u00fac \u0111\u00e3 l\u00e0m nh\u1eefng d\u1ef1 \u00e1n n\u00e0o?", "PROJECT")]
    [InlineData("Phuc da lam nhung du an nao?", "PROJECT")]
    [InlineData("C\u00e1c d\u1ef1 \u00e1n c\u1ee7a Ph\u00fac l\u00e0 g\u00ec?", "PROJECT")]
    [InlineData("Li\u1ec7t k\u00ea c\u00e1c d\u1ef1 \u00e1n c\u1ee7a Ph\u00fac.", "PROJECT")]
    [InlineData("What projects has Ph\u00fac built?", "PROJECT")]
    [InlineData("List Ph\u00fac's projects.", "PROJECT")]
    [InlineData("What work experience does Ph\u00fac have?", "EXPERIENCE")]
    [InlineData("Ph\u00fac c\u00f3 nh\u1eefng kinh nghi\u1ec7m l\u00e0m vi\u1ec7c n\u00e0o?", "EXPERIENCE")]
    [InlineData("List all education history.", "EDUCATION")]
    [InlineData("Li\u1ec7t k\u00ea c\u00e1c kh\u00f3a \u0111\u00e0o t\u1ea1o.", "TRAINING")]
    [InlineData("Which certificates has Ph\u00fac earned?", "CERTIFICATE")]
    [InlineData("Show me the career timeline.", "JOURNEY")]
    public void Explicit_bilingual_collection_phrases_are_classified(string message, string expected)
    {
        Assert.Equal(expected, CollectionIntentClassifier.Classify(message));
    }

    [Theory]
    [InlineData("Tell me about SchoolSaaS.")]
    [InlineData("Ph\u00fac l\u00e0m g\u00ec \u1edf RTC?")]
    [InlineData("What did Ph\u00fac work on?")]
    [InlineData("Tell me more.")]
    [InlineData("What is the weather today?")]
    [InlineData("Th\u1eddi ti\u1ebft h\u00f4m nay th\u1ebf n\u00e0o?")]
    [InlineData("Give me a recipe for pho.")]
    [InlineData("H\u00e3y h\u01b0\u1edbng d\u1eabn t\u00f4i n\u1ea5u ph\u1edf.")]
    [InlineData("projects")]
    [InlineData("What projectiles has Ph\u00fac built?")]
    [InlineData("What technologies does Ph\u00fac use?")]
    public void Uncertain_singular_negative_and_substring_inputs_fail_closed(string message)
    {
        Assert.Null(CollectionIntentClassifier.Classify(message));
    }

    [Theory]
    [InlineData("List projects and certificates.")]
    [InlineData("Do not list Ph\u00fac's projects.")]
    [InlineData("\u0110\u1eebng li\u1ec7t k\u00ea c\u00e1c d\u1ef1 \u00e1n.")]
    [InlineData("Translate the phrase list projects.")]
    [InlineData("What does \"list projects\" mean?")]
    [InlineData("List projects from https://example.com")]
    [InlineData("List projects\nthen list certificates")]
    public void Multiple_negated_meta_quoted_url_and_multiline_inputs_fail_closed(string message)
    {
        Assert.Null(CollectionIntentClassifier.Classify(message));
    }

    [Theory]
    [InlineData("PROFILE")]
    [InlineData("SKILLS")]
    [InlineData("UNKNOWN")]
    public async Task Collection_retriever_rejects_non_entity_collection_types_before_database_access(
        string sourceType)
    {
        await using var db = CreateContext();
        var retriever = new PgvectorKnowledgeRetriever(db);
        var members = new[]
        {
            new KnowledgeCollectionMember(sourceType, Guid.NewGuid(), "source:test", "hash", 1)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            retriever.RetrieveCollectionAsync(new float[1536], members));
    }

    [Fact]
    public async Task Collection_retriever_requires_the_existing_1536_dimensions()
    {
        await using var db = CreateContext();
        var retriever = new PgvectorKnowledgeRetriever(db);
        var members = new[]
        {
            new KnowledgeCollectionMember("PROJECT", Guid.NewGuid(), "project:test", "hash", 1)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            retriever.RetrieveCollectionAsync(new float[3], members));
    }

    [Fact]
    public async Task Collection_retriever_rejects_missing_current_identity_and_noncanonical_ordinals()
    {
        await using var db = CreateContext();
        var retriever = new PgvectorKnowledgeRetriever(db);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retriever.RetrieveCollectionAsync(
            new float[1536],
            [new("PROJECT", id, "", "hash", 1)]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retriever.RetrieveCollectionAsync(
            new float[1536],
            [new("PROJECT", id, "project:test", "", 1)]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retriever.RetrieveCollectionAsync(
            new float[1536],
            [new("PROJECT", id, "project:test", "hash", 2)]));
    }

    [Fact]
    public async Task Collection_retriever_rejects_duplicate_canonical_identities_before_database_access()
    {
        await using var db = CreateContext();
        var retriever = new PgvectorKnowledgeRetriever(db);
        var id = Guid.NewGuid();
        var members = new[]
        {
            new KnowledgeCollectionMember("PROJECT", id, "project:test", "hash", 1),
            new KnowledgeCollectionMember("PROJECT", id, "project:other", "other-hash", 2)
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            retriever.RetrieveCollectionAsync(new float[1536], members));
    }

    [Fact]
    public async Task Empty_canonical_collection_returns_without_database_access()
    {
        await using var db = CreateContext();
        var retriever = new PgvectorKnowledgeRetriever(db);

        var result = await retriever.RetrieveCollectionAsync(
            new float[1536],
            Array.Empty<KnowledgeCollectionMember>());

        Assert.Empty(result);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
