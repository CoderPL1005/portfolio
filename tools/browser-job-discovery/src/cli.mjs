import { mkdir, readFile, stat, writeFile, rename, unlink } from 'node:fs/promises';
import { resolve, relative, isAbsolute, dirname } from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { ActivityBudget, SearchPlanner, JobPostExtractor, DiscoveryReporter, runDiscovery, requireMode, Stop } from './core.mjs';
import { structuredMetrics } from './vacancy-extraction.mjs';
import { validateDateFilters } from './discovery-contract.mjs';
import { buildDiscoveryYield } from './discovery-yield.mjs';
import { reviewExport } from './discovery-review.mjs';
import { resolveDiscoveryPolicy } from './candidate-technology-profile.mjs';

// No browser URL/session/auth arguments. Select mode before importing a browser.
const args = process.argv.slice(2);
if (args.includes('--execution')) {
  try {
    if(args[0]!=='--execution'||!((args.length===2)||(args[2]==='--persisted'&&(args.length===3||args.length===4&&args[3]==='--confirm-gemini-free-tier'))))throw new Stop('DISCOVERY_EXECUTION_ID_INVALID');
    const {runDiscoveryExecution,executionBackendFromEnvironment,replayDiscoveryExecution,persistedBackendFromEnvironment}=await import('./discovery-execution-worker.mjs');
    const result=args[2]==='--persisted'?await replayDiscoveryExecution({executionId:args[1],confirmGeminiFreeTier:args.includes('--confirm-gemini-free-tier')},{backend:persistedBackendFromEnvironment()}):
      await runDiscoveryExecution({executionId:args[1],workflowRunId:process.env.GITHUB_RUN_ID,runAttempt:Number(process.env.GITHUB_RUN_ATTEMPT)},{backend:executionBackendFromEnvironment()});
    console.log(JSON.stringify({state:result?.state??'UNKNOWN',providerRequests:args[2]==='--persisted'?0:undefined}));
    if(!['SUCCEEDED','PARTIAL','PROCESSED_FOR_REVIEW'].includes(result?.state))process.exitCode=1;
  }catch(error){console.error(error instanceof Stop?error.code:'DISCOVERY_EXECUTION_FAILED');process.exitCode=1;}
} else if (args.includes('--orchestrate')) {
  try { await (await import('./pagination-cli.mjs')).paginationCli(args); }
  catch (error) { console.error(error instanceof Stop ? error.code : 'ORCHESTRATION_FAILED'); process.exitCode = 1; }
} else if (args.includes('--source')) {
  // Same entrypoint; live is explicit and state must be restored before IO.
  let adapter;
  try {
    const options = new Map(); const flags = new Set();
    for (let i = 0; i < args.length; i++) {
      const argument = args[i];
      if (['--live', '--initialize-state', '--prepare-state'].includes(argument)) {
        if (flags.has(argument)) throw new Stop('INVALID_DISCOVERY_ARGUMENTS');
        flags.add(argument); continue;
      }
      if (!['--source', '--replay', '--output', '--max-requests', '--max-credits', '--timeout-ms', '--query', '--start-time', '--end-time', '--review-output', '--review-run-id', '--review-policy'].includes(argument) ||
          !args[i + 1] || options.has(argument)) throw new Stop('INVALID_DISCOVERY_ARGUMENTS');
      options.set(argument, args[++i]);
    }
    const live = flags.has('--live'); const prepare = flags.has('--prepare-state');
    if (options.get('--source') !== 'socialapis' || Number(live) + Number(prepare) + Number(options.has('--replay')) !== 1) throw new Stop('SOCIALAPIS_MODE_REQUIRED');
    const dateFilters = validateDateFilters({ startTime: options.get('--start-time'), endTime: options.get('--end-time') });
    const debugDirectory = fileURLToPath(new URL('../.debug/', import.meta.url));
    const capture = options.has('--replay') ? resolve(options.get('--replay')) : null;
    const reviewOutput = options.has('--review-output') ? resolve(options.get('--review-output')) : null;
    let reviewContext;
    if (reviewOutput) {
      const repo = fileURLToPath(new URL('../../../', import.meta.url));
      const fromRepo = relative(repo, reviewOutput), fromDebug = relative(debugDirectory, reviewOutput);
      const inRepo = fromRepo && !fromRepo.startsWith('..') && !isAbsolute(fromRepo);
      const inDebug = fromDebug && !fromDebug.startsWith('..') && !isAbsolute(fromDebug);
      if (prepare || !reviewOutput.endsWith('.json') || reviewOutput === capture || inRepo && !inDebug) throw new Stop('PRIVATE_REVIEW_OUTPUT_REQUIRED');
      try { await stat(reviewOutput); throw new Stop('REVIEW_EXPORT_ALREADY_EXISTS'); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
      reviewExport([], { runId: options.get('--review-run-id'), query: options.get('--query') ?? 'capture' });
      let policy = {};
      if (options.has('--review-policy')) {
        if ((await stat(options.get('--review-policy'))).size > 1_048_576) throw new Stop('REVIEW_POLICY_TOO_LARGE');
        policy = resolveDiscoveryPolicy(JSON.parse(await readFile(options.get('--review-policy'), 'utf8')), options.get('--query') ?? 'capture');
      }
      reviewContext = { runId: options.get('--review-run-id'), pageNumber: 1, evaluatedAt: new Date().toISOString(),
        profile: policy.profile, targetJobPreferences: policy.targetJobPreferences };
    } else if (options.has('--review-run-id') || options.has('--review-policy')) throw new Stop('REVIEW_OUTPUT_REQUIRED');
    const output = resolve(options.get('--output') ?? resolve(debugDirectory, 'socialapis-discovery.json'));
    if (reviewOutput === output) throw new Stop('PRIVATE_REVIEW_OUTPUT_REQUIRED');
    const outputRelative = relative(debugDirectory, output);
    if (!outputRelative || outputRelative.startsWith('..') || isAbsolute(outputRelative) || output === capture ||
        output === resolve(debugDirectory, 'socialapis-response.json') || !output.endsWith('.json')) throw new Stop('PRIVATE_REPLAY_OUTPUT_REQUIRED');
    let existing = [];
    try {
      if ((await stat(output)).size > 10_485_760) throw new Stop('REPLAY_STORE_TOO_LARGE');
      const previous = JSON.parse(await readFile(output, 'utf8'));
      // Never let the legacy one-page writer erase a durable pagination journal.
      // One-request orchestration remains available with maxRequests=1.
      if (previous.orchestration || previous.requestLedger) throw new Stop('ORCHESTRATED_STATE_REQUIRES_ORCHESTRATION');
      if (!['LOCAL_PROVIDER_CAPTURE_REPLAY', 'LIVE_PROVIDER_RESPONSE', 'SOCIALAPIS_DISCOVERY_STATE'].includes(previous.evidenceKind) || !Array.isArray(previous.jobs) ||
          previous.jobs.some(job => job.provider !== 'SOCIALAPIS' || !job.id || job.sourceIdentity !== `SOCIALAPIS:${job.id}`)) throw new Stop('INVALID_REPLAY_STORE');
      existing = previous.jobs;
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
      if (!capture && !flags.has('--initialize-state')) throw new Stop('DISCOVERY_STATE_REQUIRED');
    }
    const save = async report => {
      await mkdir(dirname(output), { recursive: true });
      const temporary = `${output}.${randomUUID()}.tmp`;
      try {
        await writeFile(temporary, JSON.stringify(report, null, 2) + '\n', { encoding: 'utf8', mode: 0o600, flag: 'wx' });
        await rename(temporary, output);
      } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
    };
    if (prepare) {
      await save({ evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: existing });
      console.log(JSON.stringify({ statePrepared: true, totalCandidates: existing.length, networkRequests: 0 }));
    } else {
      const budget = new ActivityBudget();
      const { SocialApisCaptureDiscoveryAdapter, SocialApisLiveDiscoveryAdapter } = await import('./socialapis-adapter.mjs');
      if (capture) {
        requireMode('offline');
        if ((await stat(capture)).size > 1_048_576) throw new Stop('RESPONSE_TOO_LARGE');
        adapter = new SocialApisCaptureDiscoveryAdapter(JSON.parse(await readFile(capture, 'utf8')), { budget, reviewContext });
      } else {
        // Reserve a local checkpoint before the possible external side effect.
        await save({ evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: existing });
        adapter = new SocialApisLiveDiscoveryAdapter({ enabled: live && process.env.SOCIALAPIS_LIVE_ENABLED === 'true',
          freePlanConfirmed: process.env.SOCIALAPIS_FREE_PLAN_CONFIRMED === 'true', noCashConfirmed: process.env.SOCIALAPIS_NO_CASH_CONFIRMED === 'true',
          maxRequests: Number(options.get('--max-requests') ?? process.env.SOCIALAPIS_MAX_REQUESTS ?? 1),
          maxCredits: Number(options.get('--max-credits') ?? process.env.SOCIALAPIS_MAX_CREDITS ?? 1),
          timeoutMs: Number(options.get('--timeout-ms') ?? 20_000),
          ...dateFilters, reviewContext,
          ...(options.has('--query') ? { query: options.get('--query') } : {}) });
      }
      const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] },
        extractor: new JobPostExtractor(), reporter: new DiscoveryReporter({ evidenceKind: live ? 'LIVE_PROVIDER_RESPONSE' : 'LOCAL_PROVIDER_CAPTURE_REPLAY' }),
        budget, target: {}, matcher: job => adapter.classify(job) });
      if (reviewOutput && (adapter.reviewItems || adapter.adapter?.reviewItems)) {
        const bundle = reviewExport(adapter.reviewItems ?? adapter.adapter.reviewItems, { runId: reviewContext.runId, query: adapter.query });
        const json = JSON.stringify(bundle, null, 2) + '\n';
        if (Buffer.byteLength(json) > 8 * 1024 * 1024) throw new Stop('REVIEW_EXPORT_TOO_LARGE');
        await mkdir(dirname(reviewOutput), { recursive: true });
        await writeFile(reviewOutput, json, { encoding: 'utf8', mode: 0o600, flag: 'wx' });
      }
      if (report.stopCode) throw new Stop(report.stopCode);
      report.queryYield = buildDiscoveryYield({ jobs: report.jobs, existingJobs: existing, query: adapter.query,
        itemStats: adapter.itemStats, requestStats: adapter.requestStats, dateFilters,
        mode: live ? 'LIVE_PROVIDER_RESPONSE' : 'REPLAY' });
      const stored = new Map(existing.map(job => [job.sourceIdentity, job]));
      let inserted = 0; let updated = 0; let unchanged = 0; let contentUpdated = 0; let extractionRefreshed = 0; let metadataUpdated = 0;
      for (const job of report.jobs) {
        const previous = stored.get(job.sourceIdentity);
        if (!previous) inserted++;
        else if (JSON.stringify(previous) !== JSON.stringify(job)) {
          updated++;
          if (previous.contentHash !== job.contentHash) contentUpdated++;
          else if (previous.extractionVersion !== job.extractionVersion) extractionRefreshed++;
          else metadataUpdated++;
        }
        else unchanged++;
        stored.set(job.sourceIdentity, job);
      }
      report.metricsScope = 'CURRENT_RUN';
      report.processedSourceIdentities = report.jobs.map(job => job.sourceIdentity);
      report.jobs = [...stored.values()];
      report.accumulatedMetrics = { scope: 'STORED_STATE_NOT_NEW_DISCOVERIES', uniquePosts: report.jobs.length,
        ...structuredMetrics(report.jobs) };
      report.storage = { inserted, updated, unchanged, contentUpdated, extractionRefreshed, metadataUpdated, totalCandidates: report.jobs.length };
      const execution = { processedItems: adapter.itemCount, networkRequests: adapter.requestStats?.requestCount ?? 0,
        pagination: { cursorPresent: adapter.pagination.cursor !== null, followed: false },
        permalinkExistenceVerified: false };
      if (live) { report.live = execution; report.providerUsage = adapter.requestStats; }
      else report.replay = execution;
      await save(report);
      // JD text/contact data remains in the ignored private file, not stdout.
      console.log(JSON.stringify({ evidenceKind: report.evidenceKind, metrics: report.metrics,
        itemStats: report.itemStats, storage: report.storage, queryYield: report.queryYield,
        ...(live ? { live: report.live, providerUsage: report.providerUsage } : { replay: report.replay }), reportWritten: true }));
    }
  } catch (error) {
    if (adapter?.requestStats || adapter?.itemStats) console.log(JSON.stringify({ providerUsage: adapter.requestStats, itemStats: adapter.itemStats }));
    console.error(error instanceof Stop ? error.code : 'DISCOVERY_RUN_FAILED'); process.exitCode = 1;
  }
} else if (args.length && !(args.length === 2 && args[0] === '--output')) {
  console.error('OFFLINE_ONLY: supported argument is --output <report.json>'); process.exitCode = 1;
} else {
  requireMode('offline');
  const { startFixtureServer, FixtureBrowserSessionProvider, FixtureBrowserDiscoveryAdapter } = await import('./browser.mjs');
  let server;
  try {
    server = await startFixtureServer();
    const report = await runDiscovery({
      sessionProvider: new FixtureBrowserSessionProvider(server.origin), adapter: new FixtureBrowserDiscoveryAdapter(server.origin),
      planner: new SearchPlanner(), extractor: new JobPostExtractor(), reporter: new DiscoveryReporter(),
      budget: new ActivityBudget(), target: {
        technologies: ['.NET', 'C#', 'ASP.NET Core', 'Angular', 'TypeScript .NET'],
        locations: ['Hà Nội', 'Hanoi', 'Ha Noi'], seniorities: ['Intern', 'Fresher', 'Junior', '0–2 years']
      }
    });
    const output = resolve(args[1] ?? fileURLToPath(new URL('../output/report.json', import.meta.url)));
    await mkdir(resolve(output, '..'), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + '\n', 'utf8');
    console.log(JSON.stringify({ evidenceKind: report.evidenceKind, stopCode: report.stopCode, metrics: report.metrics,
      browser: report.gates.browserTechnical, reportWritten: true }));
    if (report.stopCode || report.metrics.targetMatches < 1) process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Stop ? error.code : 'OFFLINE_RUN_FAILED'); process.exitCode = 1;
  } finally { await server?.close(); }
}
