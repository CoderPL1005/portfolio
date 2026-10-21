import { mkdir, readFile, stat, writeFile, rename, unlink } from 'node:fs/promises';
import { resolve, relative, isAbsolute, dirname } from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { ActivityBudget, SearchPlanner, JobPostExtractor, DiscoveryReporter, runDiscovery, requireMode, Stop } from './core.mjs';

// No browser URL/session/auth arguments. Select mode before importing a browser.
const args = process.argv.slice(2);
if (args.includes('--source')) {
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
      if (!['--source', '--replay', '--output', '--max-requests', '--max-credits', '--timeout-ms', '--query'].includes(argument) ||
          !args[i + 1] || options.has(argument)) throw new Stop('INVALID_DISCOVERY_ARGUMENTS');
      options.set(argument, args[++i]);
    }
    const live = flags.has('--live'); const prepare = flags.has('--prepare-state');
    if (options.get('--source') !== 'socialapis' || Number(live) + Number(prepare) + Number(options.has('--replay')) !== 1) throw new Stop('SOCIALAPIS_MODE_REQUIRED');
    const debugDirectory = fileURLToPath(new URL('../.debug/', import.meta.url));
    const capture = options.has('--replay') ? resolve(options.get('--replay')) : null;
    const output = resolve(options.get('--output') ?? resolve(debugDirectory, 'socialapis-discovery.json'));
    const outputRelative = relative(debugDirectory, output);
    if (!outputRelative || outputRelative.startsWith('..') || isAbsolute(outputRelative) || output === capture ||
        output === resolve(debugDirectory, 'socialapis-response.json') || !output.endsWith('.json')) throw new Stop('PRIVATE_REPLAY_OUTPUT_REQUIRED');
    let existing = [];
    try {
      if ((await stat(output)).size > 10_485_760) throw new Stop('REPLAY_STORE_TOO_LARGE');
      const previous = JSON.parse(await readFile(output, 'utf8'));
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
      const { SocialApisCaptureDiscoveryAdapter, SocialApisLiveDiscoveryAdapter } = await import('./socialapis-adapter.mjs');
      if (capture) {
        requireMode('offline');
        if ((await stat(capture)).size > 1_048_576) throw new Stop('RESPONSE_TOO_LARGE');
        adapter = new SocialApisCaptureDiscoveryAdapter(JSON.parse(await readFile(capture, 'utf8')));
      } else {
        // Reserve a local checkpoint before the possible external side effect.
        await save({ evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: existing });
        adapter = new SocialApisLiveDiscoveryAdapter({ enabled: live && process.env.SOCIALAPIS_LIVE_ENABLED === 'true',
          freePlanConfirmed: process.env.SOCIALAPIS_FREE_PLAN_CONFIRMED === 'true', noCashConfirmed: process.env.SOCIALAPIS_NO_CASH_CONFIRMED === 'true',
          maxRequests: Number(options.get('--max-requests') ?? process.env.SOCIALAPIS_MAX_REQUESTS ?? 1),
          maxCredits: Number(options.get('--max-credits') ?? process.env.SOCIALAPIS_MAX_CREDITS ?? 1),
          timeoutMs: Number(options.get('--timeout-ms') ?? 20_000),
          ...(options.has('--query') ? { query: options.get('--query') } : {}) });
      }
      const report = await runDiscovery({ adapter, planner: { plan: () => [adapter.query] },
        extractor: new JobPostExtractor(), reporter: new DiscoveryReporter({ evidenceKind: live ? 'LIVE_PROVIDER_RESPONSE' : 'LOCAL_PROVIDER_CAPTURE_REPLAY' }),
        budget: new ActivityBudget(), target: {}, matcher: job => adapter.classify(job) });
      if (report.stopCode) throw new Stop(report.stopCode);
      const stored = new Map(existing.map(job => [job.sourceIdentity, job]));
      let inserted = 0; let updated = 0; let unchanged = 0;
      for (const job of report.jobs) {
        const previous = stored.get(job.sourceIdentity);
        if (!previous) inserted++;
        else if (JSON.stringify(previous) !== JSON.stringify(job)) updated++;
        else unchanged++;
        stored.set(job.sourceIdentity, job);
      }
      report.jobs = [...stored.values()];
      report.storage = { inserted, updated, unchanged, totalCandidates: report.jobs.length };
      const execution = { processedItems: adapter.itemCount, networkRequests: adapter.requestStats?.requestCount ?? 0,
        pagination: { cursorPresent: adapter.pagination.cursor !== null, followed: false },
        permalinkExistenceVerified: false };
      if (live) { report.live = execution; report.providerUsage = adapter.requestStats; }
      else report.replay = execution;
      await save(report);
      // JD text/contact data remains in the ignored private file, not stdout.
      console.log(JSON.stringify({ evidenceKind: report.evidenceKind, metrics: report.metrics,
        storage: report.storage, ...(live ? { live: report.live, providerUsage: report.providerUsage } : { replay: report.replay }), reportWritten: true }));
    }
  } catch (error) {
    if (adapter?.requestStats) console.log(JSON.stringify({ providerUsage: adapter.requestStats }));
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
