import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ActivityBudget, SearchPlanner, JobPostExtractor, DiscoveryReporter, runDiscovery, requireMode, Stop } from './core.mjs';

// No live URL/session/auth arguments. Check mode before importing/launching a browser.
const args = process.argv.slice(2);
if (args.length && !(args.length === 2 && args[0] === '--output')) {
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
