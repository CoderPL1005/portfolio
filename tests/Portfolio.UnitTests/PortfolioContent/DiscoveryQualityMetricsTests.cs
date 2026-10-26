using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryQualityMetricsTests
{
    [Fact] public async Task Oversized_cohort_fails_closed_instead_of_silently_truncating_metrics()
    {
        await using var db=new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.DiscoveryCandidates.AddRange(Enumerable.Range(0,GetDiscoveryQualityMetricsHandler.MaximumObservations+1).Select(i=>new Portfolio.Domain.Entities.DiscoveryCandidate{
            Id=Guid.NewGuid(),RunId="large",Query="fixture",PageNumber=1,ItemIndex=i,ContentStatus="NEEDS_REVIEW",TargetStatus="INSUFFICIENT_DATA",
            Payload="{\"classifierVersion\":\"content-first-v1\"}",PayloadHash=new string('a',64)}));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ServiceUnavailableException>(()=>new GetDiscoveryQualityMetricsHandler(db,TimeProvider.System).HandleAsync(new()));
    }
    private static QualitySample Sample(string prediction="VALID_RECRUITMENT",string? human="VALID_RECRUITMENT", string status="CONFIRMED",string? hash=null,string? target="TARGET_MATCH", string targetPrediction="TARGET_MATCH") =>
        new(Guid.NewGuid(),hash,prediction,targetPrediction,"content-first-v1",null,null,new(status,status=="UNREVIEWED"?0:1,human,target,"reason",DateTimeOffset.UtcNow,Guid.NewGuid()));
    [Fact] public void Golden_matrix_has_exact_half_precision_and_recall_and_stable_order()
    {
        var rows=new[]{Sample(),Sample(human:"INVALID_RECRUITMENT"),Sample("INVALID_RECRUITMENT"),Sample("INVALID_RECRUITMENT","INVALID_RECRUITMENT")};
        var d=DiscoveryQualityMetrics.Level(rows,false).ContentValidity;
        Assert.Equal(4,d.EligibleSamples); Assert.Equal(9,d.Matrix.Count);
        var valid=d.Classes.Single(x=>x.Label=="VALID_RECRUITMENT");
        Assert.Equal(1,valid.Tp);Assert.Equal(1,valid.Fp);Assert.Equal(1,valid.Fn);Assert.Equal(2,valid.Support);
        Assert.Equal(2,valid.PrecisionDenominator);Assert.Equal(2,valid.RecallDenominator);
        Assert.Equal(.5m,valid.Precision);Assert.Equal(.5m,valid.Recall);
        Assert.Equal(DiscoveryReviewRules.ContentStatuses,d.Classes.Select(x=>x.Label));
    }
    [Fact] public void Abstention_is_dimension_specific_and_prediction_abstention_is_still_a_false_negative()
    {
        var d=DiscoveryQualityMetrics.Level([Sample("NEEDS_REVIEW"),Sample(human:"NEEDS_REVIEW"),Sample(target:"INSUFFICIENT_DATA"),Sample(status:"UNCERTAIN",human:null,target:null),Sample(status:"UNREVIEWED",human:null,target:null)],false);
        Assert.Equal(2,d.ContentValidity.EligibleSamples);Assert.Equal(1,d.ContentValidity.Abstentions);
        Assert.Equal(1,d.ContentValidity.Uncertain);Assert.Equal(1,d.ContentValidity.Unreviewed);
        Assert.Equal(1,d.ContentValidity.Classes.Single(x=>x.Label=="VALID_RECRUITMENT").Fn);
        Assert.Equal(1,d.TargetMatching.Abstentions);Assert.Equal(2,d.TargetMatching.EligibleSamples);
    }
    [Fact] public void Duplicates_count_once_and_conflicts_are_not_majority_votes_or_propagated_labels()
    {
        var a=Sample(hash:"same"); var b=Sample(hash:"same"); var c=Sample(hash:"same");
        Assert.Equal(3,DiscoveryQualityMetrics.Level([a,b,c],false).ContentValidity.EligibleSamples);
        Assert.Equal(1,DiscoveryQualityMetrics.Level([a,b,c],true).ContentValidity.EligibleSamples);
        var conflict=c with{Review=c.Review with{ContentStatus="INVALID_RECRUITMENT",Status="CORRECTED"}};
        var d=DiscoveryQualityMetrics.Level([a,b,conflict],true);
        Assert.Equal(0,d.ContentValidity.EligibleSamples);Assert.Equal(1,d.ContentValidity.HumanConflicts);
        Assert.Equal(1,d.TargetMatching.EligibleSamples);
        var unreviewed=Sample(status:"UNREVIEWED",human:null,target:null,hash:"same");
        Assert.Equal(1,DiscoveryQualityMetrics.Counts([a,unreviewed]).Unreviewed);
        Assert.Equal(1,DiscoveryQualityMetrics.Level([a,unreviewed],true).ContentValidity.EligibleSamples);
        Assert.Equal("UNREVIEWED",unreviewed.Review.Status);
    }
    [Fact] public void Null_hashes_are_singletons_and_different_predictions_exclude_unique_group()
    {
        Assert.Equal(2,DiscoveryQualityMetrics.Level([Sample(),Sample()],true).ContentValidity.EligibleSamples);
        var d=DiscoveryQualityMetrics.Level([Sample(hash:"same"),Sample("INVALID_RECRUITMENT",hash:"same")],true).ContentValidity;
        Assert.Equal(1,d.PredictionConflicts);Assert.Equal(0,d.EligibleSamples);
    }
    [Fact] public void Empty_rates_are_null_and_potential_fallback_are_separate_target_categories()
    {
        Assert.All(DiscoveryQualityMetrics.Level([],false).TargetMatching.Classes,x=>{Assert.Null(x.Precision);Assert.Null(x.Recall);});
        var d=DiscoveryQualityMetrics.Level([Sample(target:"POTENTIAL_MATCH",targetPrediction:"POTENTIAL_MATCH"),Sample(target:"FALLBACK_MATCH",targetPrediction:"FALLBACK_MATCH")],false).TargetMatching;
        Assert.Equal(1,d.Classes.Single(x=>x.Label=="POTENTIAL_MATCH").Tp);
        Assert.Equal(1,d.Classes.Single(x=>x.Label=="FALLBACK_MATCH").Tp);
        Assert.Null(d.Classes.Single(x=>x.Label=="TARGET_MATCH").Precision);
    }
    [Fact] public async Task Mixed_preferences_are_separate_cohorts_and_do_not_produce_combined_target_rates()
    {
        await using var db=new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var b=DiscoveryReviewFeatureTests.Bundle();
        var h=new ImportDiscoveryReviewHandler(db,TimeProvider.System);
        await h.HandleAsync(new(b));
        await h.HandleAsync(new(b with{RunId="second",Items=[b.Items[0] with{PreferencesFingerprint=new string('a',64)}]}));
        var result=await new GetDiscoveryQualityMetricsHandler(db,TimeProvider.System).HandleAsync(new());
        Assert.Equal(2,result.Counts.Total);Assert.Equal(2,result.Cohorts.Count);Assert.False(result.OverallMatricesAvailable);
        Assert.Equal(0,result.ObservationLevel.TargetMatching.EligibleSamples);
    }
}
