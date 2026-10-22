import { mkdir, readFile, stat, writeFile, rename, unlink, appendFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { resolve, relative, isAbsolute } from 'node:path';
import { Stop } from './core.mjs';
import { beginExecution, claimPage, performPage, acknowledgePersistence, workflowMetrics } from './pagination-orchestrator.mjs';

// Existing CLI entrypoint; action commands make the artifact/HTTP boundary explicit.
export async function paginationCli(args) {
  const action = args[args.indexOf('--orchestrate') + 1];
  if (args.length !== 2 || !['init', 'claim', 'page', 'ack', 'summary'].includes(action)) throw new Stop('INVALID_ORCHESTRATION_COMMAND');
  const directory = fileURLToPath(new URL('../.debug/', import.meta.url));
  const output = resolve(process.env.DISCOVERY_STATE_FILE ?? fileURLToPath(new URL('../.debug/socialapis-discovery.json', import.meta.url)));
  const local = relative(directory, output);
  if (!local || local.startsWith('..') || isAbsolute(local) || !output.endsWith('.json') ||
      output === resolve(directory, 'socialapis-response.json')) throw new Stop('PRIVATE_REPLAY_OUTPUT_REQUIRED');
  let state;
  try {
    if ((await stat(output)).size > 10_485_760) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
    state = JSON.parse(await readFile(output, 'utf8'));
    if (!['SOCIALAPIS_DISCOVERY_STATE', 'LIVE_PROVIDER_RESPONSE', 'LOCAL_PROVIDER_CAPTURE_REPLAY'].includes(state.evidenceKind) || !Array.isArray(state.jobs)) throw new Stop('INVALID_REPLAY_STORE');
  } catch (error) {
    if (error.code !== 'ENOENT' || action !== 'init' || process.env.INITIALIZE_STATE !== 'true') throw error;
    state = { evidenceKind: 'SOCIALAPIS_DISCOVERY_STATE', jobs: [] };
  }
  const receipt = process.env.ARTIFACT_RECEIPT;
  if (action === 'init') {
    // Downloading this exact result artifact is proof of persistence. A claim
    // artifact is NEVER acknowledged, even if the prior workflow reported PASS.
    if (state.orchestration?.phase === 'COMPLETED_UNCONFIRMED') acknowledgePersistence(state, receipt);
    const policy = JSON.parse(process.env.DISCOVERY_POLICY ?? '{}');
    // The manual runner deliberately cannot enable unattended production mode.
    if (policy.mode && policy.mode !== 'development') throw new Stop('MONTHLY_FREE_BALANCE_UNVERIFIED_SCHEDULE_DISABLED');
    beginExecution(state, { ...policy, query: process.env.DISCOVERY_QUERY,
      startTime: process.env.DISCOVERY_START_TIME || undefined, endTime: process.env.DISCOVERY_END_TIME || undefined,
      maxRequests: process.env.DISCOVERY_MAX_REQUESTS ?? '1', maxCredits: process.env.DISCOVERY_MAX_CREDITS ?? '1' }, process.env.GITHUB_RUN_ID ?? 'local');
    console.log(JSON.stringify({ maximumHttpRequests: state.orchestration.config.maxRequests,
      maximumPermittedDeductedCredits: state.orchestration.config.maxCredits,
      maximumContractCreditExposure: Math.min(state.orchestration.config.maxRequests, state.orchestration.config.maxCredits),
      warning: 'Free credits only; provider overcharge cannot be undone; operator must verify no auto-top-up. No schedule.' }));
  } else if (action === 'claim') {
    const active = claimPage(state);
    if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT, `active=${active}\n`);
  } else if (action === 'page') {
    if (process.env.SOCIALAPIS_PUBLIC_ARTIFACT_CONSENT !== 'true') throw new Stop('PUBLIC_ARTIFACT_CONSENT_REQUIRED');
    await performPage(state, { receipt, enabled: process.env.SOCIALAPIS_LIVE_ENABLED === 'true',
      freePlanConfirmed: process.env.SOCIALAPIS_FREE_PLAN_CONFIRMED === 'true', noCashConfirmed: process.env.SOCIALAPIS_NO_CASH_CONFIRMED === 'true' });
  } else if (action === 'ack') {
    // Result has already been uploaded even when HTTP/parsing failed.
    acknowledgePersistence(state, receipt);
  }
  state.workflowMetrics = workflowMetrics(state);
  await mkdir(directory, { recursive: true });
  const json = JSON.stringify(state, null, 2) + '\n';
  if (Buffer.byteLength(json) > 1_048_576) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
  const temporary = `${output}.${randomUUID()}.tmp`;
  try { await writeFile(temporary, json, { mode: 0o600, flag: 'wx' }); await rename(temporary, output); }
  finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
  console.log(JSON.stringify({ action, metrics: state.workflowMetrics }));
}
