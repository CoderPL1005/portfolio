using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionRecovery
{
    /// <summary>
    /// Explicit Admin-only closure of an old, stranded execution. This is NOT recovery finalize:
    /// it produces no candidate, no new resultHash, no success receipt, and never retries a provider.
    /// Requires independent operator verification that the GitHub Actions run has finished.
    /// </summary>
    public async Task<object> CloseStaleAsync(Guid id, DiscoveryStaleCloseRequest request, CancellationToken ct)
    {
        var actor = Actor();
        if (id == Guid.Empty || request.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(request.ExpectedWorkflowRunId) ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.ExpectedWorkflowRunId, "^[1-9][0-9]{0,19}$") ||
            request.Confirmation != "CLOSE STALE EXECUTION WITHOUT RETRY" ||
            !request.RunConfirmedFinished || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length < 10 || request.Reason.Length > 1000 || request.Reason.Contains('\0'))
            throw new ValidationException([new("staleClose", "Explicit Admin authorization, final run attestation and reason are required.")]);

        // Shares the exact PostgreSQL lock used by recovery finalize, blocking conflicting
        // provider callbacks and new execution writes during the close transaction.
        await using var tx = await transactions.BeginAsync(true, ct);
        var execution = await db.DiscoveryExecutions.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Conflict("DISCOVERY_STALE_EXECUTION_NOT_FOUND");
        if (execution.State != "INGESTING" || execution.ActiveKey is null || execution.ResultHash is not null ||
            execution.WorkflowRunId != request.ExpectedWorkflowRunId || execution.Version != request.ExpectedVersion ||
            clock.GetUtcNow() - execution.UpdatedAt < TimeSpan.FromHours(12))
            throw Conflict("DISCOVERY_STALE_CLOSE_NOT_ELIGIBLE");

        var attempt = await db.DiscoveryExecutionAttempts.SingleOrDefaultAsync(x => x.ExecutionId == id, ct);
        if (attempt is null || attempt.WorkflowRunId != execution.WorkflowRunId || attempt.ActualRequests != 1 ||
            attempt.CreditsDeducted is not (>= 0 and <= 1) || attempt.HttpStatus is null ||
            !await db.DiscoveryExecutionEvents.AnyAsync(x => x.ExecutionId == id && x.State == "PROVIDER_EVIDENCE", ct))
            throw Conflict("DISCOVERY_STALE_CLOSE_ACCOUNTING_UNVERIFIED");
        // Independently compare the persisted provider-cost evidence with the attempt.
        var providerEvidence = await db.DiscoveryExecutionEvents.AsNoTracking()
            .Where(x => x.ExecutionId == id && x.State == "PROVIDER_EVIDENCE")
            .Select(x => x.Reason).Take(2).ToArrayAsync(ct);
        if (providerEvidence.Length != 1 || providerEvidence[0] is null)
            throw Conflict("DISCOVERY_STALE_CLOSE_EVIDENCE_UNVERIFIED");
        try
        {
            using var document = JsonDocument.Parse(providerEvidence[0]!);
            var evidence = document.RootElement;
            if (!evidence.TryGetProperty("actualRequests", out var r) || !r.TryGetInt32(out var requests) || requests != attempt.ActualRequests ||
                !evidence.TryGetProperty("creditsDeducted", out var c) || !c.TryGetDecimal(out var credits) || credits != attempt.CreditsDeducted ||
                !evidence.TryGetProperty("httpStatus", out var h) || !h.TryGetInt32(out var http) || http != attempt.HttpStatus)
                throw Conflict("DISCOVERY_STALE_CLOSE_EVIDENCE_UNVERIFIED");
        }
        catch (JsonException) { throw Conflict("DISCOVERY_STALE_CLOSE_EVIDENCE_UNVERIFIED"); }
        var observations = await db.DiscoveryExecutionObservations.AsNoTracking()
            .Where(x => x.ExecutionId == id).Select(x => x.SemanticState).ToArrayAsync(ct);
        if (observations.Length is 0 or > 200)
            throw Conflict("DISCOVERY_STALE_CLOSE_OBSERVATIONS_UNVERIFIED");

        var before = execution.State;
        var now = clock.GetUtcNow();
        execution.State = "PARTIAL";
        execution.ErrorCode = "DISCOVERY_STALE_CLOSED_FOR_REVIEW";
        execution.ActiveKey = null;
        execution.Version++;
        execution.UpdatedAt = now;
        db.DiscoveryExecutionEvents.Add(new DiscoveryExecutionEvent
        {
            Id = Guid.NewGuid(), ExecutionId = id, At = now, State = "STALE_CLOSE_AUDIT",
            Reason = JsonSerializer.Serialize(new
            {
                actorId = actor, request.ExpectedWorkflowRunId, request.ExpectedVersion,
                operatorAttestedRunFinished = true, reason = request.Reason.Trim(),
                priorState = before, terminalState = execution.State,
                observationCount = observations.Length,
                unknownSemanticCount = observations.Count(s => s == "UNKNOWN"),
                attemptCountRetained = execution.SemanticAttempts,
                actualRequestsRetained = attempt.ActualRequests,
                creditsDeductedRetained = attempt.CreditsDeducted,
                originalAttemptOutcome = attempt.Outcome
            }, Json)
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException) { throw Conflict("DISCOVERY_STALE_CLOSE_CONCURRENT_CHANGE"); }
        return new { state = "PARTIAL", reason = "DISCOVERY_STALE_CLOSED_FOR_REVIEW",
            observationsRetained = observations.Length, semanticUnknownRetained = observations.Count(s => s == "UNKNOWN"),
            attemptsRetained = execution.SemanticAttempts, creditsRetained = attempt.CreditsDeducted, active = false, version = execution.Version };
    }
}
