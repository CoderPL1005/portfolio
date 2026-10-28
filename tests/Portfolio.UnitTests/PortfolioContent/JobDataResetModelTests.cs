using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobDataResetModelTests
{
    [Fact] public void Actual_relational_FK_closure_is_exactly_the_thirteen_reset_tables_not_execution_accounting_or_portfolio()
    {
        // Model inspection only; no connection is opened and no SQL is run.
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=reset_model_only",o=>o.UseVector()).Options);
        var model=db.Model;var scope=new HashSet<Microsoft.EntityFrameworkCore.Metadata.IEntityType>
        {model.FindEntityType(typeof(JobPosting))!,model.FindEntityType(typeof(RawJobPosting))!,model.FindEntityType(typeof(DiscoveryCandidate))!};
        bool changed;
        do{changed=false;foreach(var entity in model.GetEntityTypes())
            if(entity.GetForeignKeys().Any(fk=>scope.Contains(fk.PrincipalEntityType)))changed|=scope.Add(entity);
        }while(changed);
        var expected=new[]{"discovery_candidates","discovery_candidate_reviews","discovery_candidate_review_events","job_postings","raw_job_postings",
            "raw_job_posting_attachments","job_applications","job_application_documents","job_application_events","submission_attempts","submission_attempt_events",
            "discovery_execution_observations","discovery_execution_vacancies"};
        Assert.Equal(expected.Order(),scope.Select(e=>e.GetTableName()).Order());
    }
}
