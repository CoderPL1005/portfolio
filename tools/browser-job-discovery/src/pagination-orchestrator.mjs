import { createHash, randomBytes, createCipheriv, createDecipheriv } from 'node:crypto';
import { Stop, ActivityBudget, JobPostExtractor } from './core.mjs';
import { validateDateFilters, validateQuery } from './discovery-contract.mjs';
import { SocialApisLiveDiscoveryAdapter } from './socialapis-adapter.mjs';

const digest = text => createHash('sha256').update(text).digest('hex');
const fold = text => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
export function calendarDay(now = new Date()) {
  const parts = new Intl.DateTimeFormat('en', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  return ['year', 'month', 'day'].map(type => parts.find(p => p.type === type).value).join('-');
}
function cursorKey(secret) {
  if (typeof secret !== 'string' || secret.length < 16) throw new Stop('CURSOR_KEY_UNAVAILABLE');
  return createHash('sha256').update('discovery-cursor-v1\0' + secret).digest();
}
function seal(cursor, secret) {
  const iv = randomBytes(12); const cipher = createCipheriv('aes-256-gcm', cursorKey(secret), iv);
  return { iv: iv.toString('base64'), data: Buffer.concat([cipher.update(cursor, 'utf8'), cipher.final()]).toString('base64'), tag: cipher.getAuthTag().toString('base64') };
}
function open(cursor, secret) {
  if (!cursor) return null;
  try {
    const decipher = createDecipheriv('aes-256-gcm', cursorKey(secret), Buffer.from(cursor.iv, 'base64'));
    decipher.setAuthTag(Buffer.from(cursor.tag, 'base64'));
    return Buffer.concat([decipher.update(Buffer.from(cursor.data, 'base64')), decipher.final()]).toString('utf8');
  } catch { throw new Stop('CURSOR_CHECKPOINT_UNREADABLE'); }
}
export function configuration(input) {
  const maxRequests = Number(input.maxRequests ?? 1); const maxCredits = Number(input.maxCredits ?? maxRequests);
  const target = Number(input.target ?? 5); const minMarginal = Number(input.minMarginal ?? 1);
  if (![maxRequests, maxCredits].every(n => Number.isInteger(n) && n >= 1 && n <= 6) ||
      !Number.isInteger(target) || target < 1 || target > 200 || !Number.isInteger(minMarginal) || minMarginal < 1 || minMarginal > 200) throw new Stop('INVALID_ORCHESTRATION_BUDGET');
  const profile = input.profile;
  if (!profile || !['technologies', 'locations', 'seniorities'].every(key => Array.isArray(profile[key]) && profile[key].length > 0 && profile[key].length <= 100 &&
    profile[key].every(value => typeof value === 'string' && value.trim() && value.length <= 100 && !/[\p{C}]/u.test(value)))) throw new Stop('CANDIDATE_PROFILE_REQUIRED');
  if (input.adaptiveQueries && (!Array.isArray(input.adaptiveQueries) || input.adaptiveQueries.length > 16)) throw new Stop('INVALID_ADAPTIVE_POOL');
  return { query: validateQuery(input.query), ...validateDateFilters(input), maxRequests, maxCredits, target, minMarginal,
    rotatingQuery: input.rotatingQuery ? validateQuery(input.rotatingQuery) : null,
    adaptiveQueries: [...new Set((input.adaptiveQueries ?? []).map(validateQuery))].sort(), profile,
    mode: input.mode ?? 'development' };
}
// Future Portfolio integration supplies this profile, not another technology DB.
// Existing extraction evidence is reused; UNKNOWN publication dates stay UNKNOWN.
export function classifyProfile(job, profile) {
  for (const vacancy of job.vacancies) {
    const technology = (vacancy.primaryTechnologies ?? []).some(t => profile.technologies.some(value => fold(t) === fold(value)));
    const level = vacancy.seniority.some(t => profile.seniorities.some(value => fold(t) === fold(value)));
    const place = profile.locations.some(value => fold(vacancy.location ?? '').includes(fold(value))) ||
      vacancy.match.locationVerified && profile.locations.some(value => ['hanoi', 'ha noi'].includes(fold(value).trim()));
    const excluded = ['ROLE_MISMATCH', 'CANDIDATE_SEEKING_WORK', 'RECRUITMENT_UNVERIFIED'].includes(vacancy.match.reason);
    vacancy.match = { ...vacancy.match, classification: !excluded && technology && level && place ? 'TARGET_MATCH' : 'NOT_MATCHED', profileMatched: !excluded && technology && level && place };
  }
  job.structuredMatch = { ...job.structuredMatch, classification: job.vacancies.some(v => v.match.profileMatched) ? 'TARGET_MATCH' : 'NOT_MATCHED' };
  job.match = { ...job.structuredMatch };
  return job;
}
export function beginExecution(state, input, workflowRunId, { productionGuard } = {}) {
  const config = configuration(input);
  if (config.mode !== 'development' && config.mode !== 'production') throw new Stop('INVALID_MODE');
  if (config.mode === 'production' && (typeof productionGuard !== 'function' || productionGuard() !== true)) throw new Stop('MONTHLY_FREE_BALANCE_UNVERIFIED_SCHEDULE_DISABLED');
  if (!Array.isArray(state.jobs) || state.jobs.some(j => j.provider !== 'SOCIALAPIS' || !j.id || j.sourceIdentity !== `SOCIALAPIS:${j.id}`) ||
      new Set(state.jobs.map(j => j.sourceIdentity)).size !== state.jobs.length) throw new Stop('INVALID_REPLAY_STORE');
  const previous = state.orchestration;
  if (['CLAIMED', 'IN_FLIGHT'].includes(previous?.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (previous?.phase === 'COMPLETED_UNCONFIRMED') throw new Stop('PERSISTENCE_CONFIRMATION_REQUIRED');
  if (previous && !previous.stopReason) {
    if (previous.configHash !== digest(JSON.stringify(config))) throw new Stop('PENDING_EXECUTION_CONFIG_MISMATCH');
    previous.workflowRunId = workflowRunId;
    return state;
  }
  state.evidenceKind = 'SOCIALAPIS_DISCOVERY_STATE'; state.requestLedger ??= [];
  state.discoveryYieldHistory ??= [];
  state.orchestration = { config, configHash: digest(JSON.stringify(config)), workflowRunId, phase: 'READY', stopReason: null,
    initialIdentities: state.jobs.map(j => j.sourceIdentity), pages: [], newRelevantKeys: [], relevantKeys: [],
    plan: { allocation: 'CORE', query: config.query, pageNumber: 1, cursor: null, cursorHash: null },
    usedCursorHashes: [], usedQueries: [config.query], storage: { inserted: 0, updated: 0, unchanged: 0 } };
  return state;
}
export function workflowMetrics(state) {
  const run = state.orchestration;
  const attempts = state.requestLedger.filter(p => p.workflowRunId === run.workflowRunId);
  const pages = run.pages.filter(p => p.workflowRunId === run.workflowRunId);
  const known = attempts.reduce((sum, p) => sum + (p.creditsDeducted ?? 0), 0);
  const knownRequests = attempts.reduce((sum, p) => sum + (p.actualRequests ?? 0), 0);
  return { actualRequests: attempts.some(p => p.actualRequests === null) ? null : knownRequests,
    knownActualRequests: knownRequests, reservedAttempts: attempts.length, uncertainAttempts: attempts.filter(p => p.actualRequests === null).length,
    totalDeductedCredits: attempts.some(p => p.creditsDeducted === null) ? null : known, knownDeductedCredits: known,
    stopReason: run.stopReason, uniqueRelevantVacancies: new Set(pages.flatMap(p => p.relevantVacancyKeys)).size,
    newRelevantVacancies: pages.reduce((sum, p) => sum + p.newRelevantVacancies, 0),
    overlapAcrossPages: pages.reduce((sum, p) => sum + p.overlap, 0),
    storage: { inserted: pages.reduce((sum,p) => sum+p.storage.inserted,0), updated: pages.reduce((sum,p) => sum+p.storage.updated,0),
      unchanged: pages.reduce((sum,p) => sum+p.storage.unchanged,0), totalCandidates: state.jobs.length },
    logicalExecution: { completedPages: run.pages.length, uniqueRelevantVacancies: run.relevantKeys.length, newRelevantVacancies: run.newRelevantKeys.length } };
}
export function claimPage(state, now = new Date(), { productionGuard } = {}) {
  const run = state.orchestration; const config = run.config;
  if (['CLAIMED', 'IN_FLIGHT'].includes(run.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (run.phase === 'COMPLETED_UNCONFIRMED') throw new Stop('PERSISTENCE_CONFIRMATION_REQUIRED');
  if (run.stopReason) return false;
  if (config.mode === 'production') {
    if (state.requestLedger.filter(p => p.day === calendarDay(now)).length >= 6) { run.stopReason = 'DAILY_REQUEST_BUDGET'; return false; }
    if (typeof productionGuard !== 'function' || productionGuard() !== true) throw new Stop('MONTHLY_FREE_BALANCE_UNVERIFIED_SCHEDULE_DISABLED');
  }
  if (run.pages.length >= config.maxRequests) { run.stopReason = 'REQUEST_BUDGET'; return false; }
  if (run.pages.reduce((sum, p) => sum + (p.creditsDeducted ?? 0), 0) + 1 > config.maxCredits) { run.stopReason = 'CREDIT_BUDGET'; return false; }
  const attempt = { workflowRunId: run.workflowRunId, day: calendarDay(now), allocation: run.plan.allocation,
    query: run.plan.query, pageNumber: run.plan.pageNumber, inputCursor: run.plan.cursorHash,
    startTime: config.startTime, endTime: config.endTime,
    actualRequests: null, creditsDeducted: null, outcome: 'RESERVED_AMBIGUOUS_UNTIL_COMPLETED' };
  state.requestLedger.push(attempt); run.attemptIndex = state.requestLedger.length - 1; run.phase = 'CLAIMED';
  return true;
}
function chooseNext(state, page, nextCursor, secret) {
  const run = state.orchestration; const config = run.config;
  if (run.newRelevantKeys.length >= config.target) { run.stopReason = 'SUFFICIENT_NEW_VACANCIES'; return; }
  if (run.pages.length >= config.maxRequests) { run.stopReason = 'REQUEST_BUDGET'; return; }
  if (run.pages.reduce((sum, p) => sum + (p.creditsDeducted ?? 0), 0) + 1 > config.maxCredits) { run.stopReason = 'CREDIT_BUDGET'; return; }
  let coreStop = null;
  if (!nextCursor) coreStop = 'NO_CURSOR';
  else if (run.usedCursorHashes.includes(digest(nextCursor))) coreStop = 'REPEATED_CURSOR';
  else if (!page.returnedPostIds.length) coreStop = 'EMPTY_PAGE';
  else if (page.newRelevantVacancies < config.minMarginal) coreStop = 'LOW_MARGINAL_YIELD';
  else if (page.pageNumber >= 4) coreStop = 'CORE_BUDGET';
  if (page.allocation === 'CORE' && !coreStop) {
    const hash = digest(nextCursor); run.usedCursorHashes.push(hash);
    run.plan = { ...run.plan, pageNumber: page.pageNumber + 1, cursor: seal(nextCursor, secret), cursorHash: hash };
    return;
  }
  let query = null; let allocation = null;
  if (config.rotatingQuery && !run.usedQueries.includes(config.rotatingQuery)) { query = config.rotatingQuery; allocation = 'ROTATING'; }
  else {
    const hasYield = q => state.discoveryYieldHistory.some(p => p.query === q && typeof p.creditsDeducted === 'number' && p.creditsDeducted > 0);
    const candidates = config.adaptiveQueries.filter(q => !run.usedQueries.includes(q) && hasYield(q));
    const score = q => {
      const history = state.discoveryYieldHistory.filter(p => p.query === q && typeof p.creditsDeducted === 'number' && p.creditsDeducted > 0);
      return history.length ? history.reduce((s, p) => s + p.newRelevantVacancies, 0) / history.reduce((s, p) => s + p.creditsDeducted, 0) : -1;
    };
    candidates.sort((a, b) => score(b) - score(a) || (a < b ? -1 : a > b ? 1 : 0));
    if (!run.pages.some(p => p.allocation === 'ADAPTIVE') && candidates.length) { query = candidates[0]; allocation = 'ADAPTIVE'; }
  }
  if (!query) { run.stopReason = coreStop ?? 'ALLOCATION_COMPLETE'; return; }
  run.usedQueries.push(query); run.plan = { allocation, query, pageNumber: 1, cursor: null, cursorHash: null };
}
// Caller must upload the CLAIMED state before calling this function, then upload
// the completed state before acknowledgePersistence. No in-process retry exists.
export async function performPage(state, { transport, now = new Date(), receipt, enabled, freePlanConfirmed, noCashConfirmed,
  secret = process.env.SOCIALAPIS_API_KEY, timeoutMs = 20_000 } = {}) {
  const run = state.orchestration;
  if (run.phase !== 'CLAIMED' || !/^[1-9]\d*$/u.test(String(receipt ?? ''))) throw new Stop('DURABLE_CLAIM_RECEIPT_REQUIRED');
  const attempt = state.requestLedger[run.attemptIndex];
  if (attempt.day !== calendarDay(now)) throw new Stop('RESERVATION_DAY_CHANGED_NO_REQUEST');
  run.phase = 'IN_FLIGHT'; // Synchronous in-process claim; durable file is still CLAIMED.
  let adapter;
  try {
    cursorKey(secret); // Validate codec before any possible HTTP side effect.
    adapter = new SocialApisLiveDiscoveryAdapter({ enabled, freePlanConfirmed, noCashConfirmed,
      query: run.plan.query, ...run.config, ...{ query: run.plan.query, maxRequests: 1, maxCredits: 1 },
      timeoutMs, endCursor: open(run.plan.cursor, secret) }, transport);
    const budget = new ActivityBudget();
    const urls = await adapter.search(undefined, adapter.query, budget);
    // A provider page is bounded to 200 records by the existing adapter. Extract
    // all valid records, not only the old simulator's 20-candidate browser cap.
    const jobs = []; const ids = new Set();
    for (const url of urls) {
      budget.check();
      const raw = adapter.adapter.records.get(url);
      if (ids.has(raw.id)) continue; ids.add(raw.id);
      jobs.push(classifyProfile(new JobPostExtractor().extract(structuredClone(raw)), run.config.profile));
    }
    const stored = new Map(state.jobs.map(j => [j.sourceIdentity, j]));
    const beforeStorage = { ...run.storage };
    const beforeIds = new Set(stored.keys()); const inserted = [];
    for (const job of jobs) {
      const previous = stored.get(job.sourceIdentity);
      if (!previous) { inserted.push(job.id); run.storage.inserted++; }
      else if (JSON.stringify(previous) === JSON.stringify(job)) run.storage.unchanged++;
      else run.storage.updated++;
      stored.set(job.sourceIdentity, job);
      job.vacancies.forEach((v, index) => {
        if (v.match.classification !== 'TARGET_MATCH') return;
        const key = `${job.sourceIdentity}:${index}`;
        if (!run.relevantKeys.includes(key)) run.relevantKeys.push(key);
        if (!run.initialIdentities.includes(job.sourceIdentity) && !run.newRelevantKeys.includes(key)) run.newRelevantKeys.push(key);
      });
    }
    const previousNew = run.pages.reduce((sum, p) => sum + p.newRelevantVacancies, 0);
    const next = adapter.pagination.cursor;
    const validNext = adapter.pagination.hasNext !== false && typeof next === 'string' && next.trim() && next.length <= 4096 && !/[\p{C}]/u.test(next) ? next : null;
    const page = { ...attempt, ...adapter.itemStats, actualRequests: adapter.requestStats.requestCount,
      httpStatus: adapter.requestStats.httpStatus,
      creditsDeducted: adapter.requestStats.creditsDeducted, creditsUsedAlternateHeader: adapter.requestStats.creditsUsedAlternateHeader,
      returnedPostIds: jobs.map(j => j.id), newlyInsertedIds: inserted, nextCursorPresent: !!validNext,
      storage: Object.fromEntries(Object.keys(run.storage).map(key => [key, run.storage[key] - beforeStorage[key]])),
      relevantVacancyKeys: jobs.flatMap(j => j.vacancies.flatMap((v,index) => v.match.classification === 'TARGET_MATCH' ? [`${j.sourceIdentity}:${index}`] : [])),
      relevantVacancies: jobs.reduce((sum, j) => sum + j.vacancies.filter(v => v.match.classification === 'TARGET_MATCH').length, 0),
      newRelevantVacancies: run.newRelevantKeys.length - previousNew, freshnessUnknown: jobs.filter(j => j.match.freshness === 'UNKNOWN').length,
      freshnessUnknownVacancies: jobs.reduce((sum,j) => sum+j.vacancies.filter(v => v.match.freshness === 'UNKNOWN').length,0),
      verifiedRecentVacancies: 0, providerDateFilterBehavior: 'UNVERIFIED',
      overlap: jobs.filter(j => beforeIds.has(j.sourceIdentity) && run.pages.some(p => p.returnedPostIds.includes(j.id))).length,
      outcome: 'COMPLETED_UNCONFIRMED' };
    state.jobs = [...stored.values()]; Object.assign(attempt, { actualRequests: page.actualRequests, creditsDeducted: page.creditsDeducted, outcome: 'COMPLETED_UNCONFIRMED' });
    run.pages.push(page); state.discoveryYieldHistory.push({ query: page.query, newRelevantVacancies: page.newRelevantVacancies, creditsDeducted: page.creditsDeducted });
    state.discoveryYieldHistory = state.discoveryYieldHistory.slice(-100);
    chooseNext(state, page, validNext, secret); run.phase = 'COMPLETED_UNCONFIRMED';
  } catch (error) {
    attempt.actualRequests = adapter?.requestStats.requestCount ?? 0;
    attempt.creditsDeducted = adapter?.requestStats.creditsDeducted ?? null;
    attempt.outcome = 'FAILED_OR_AMBIGUOUS'; run.stopReason = error instanceof Stop ? error.code : 'PAGE_FAILED';
    // Remain CLAIMED even for a known provider failure: no automatic refetch.
    run.phase = 'CLAIMED';
  }
  state.workflowMetrics = workflowMetrics(state);
  return state;
}
export function acknowledgePersistence(state, receipt) {
  if (!/^[1-9]\d*$/u.test(String(receipt ?? ''))) throw new Stop('DURABLE_RESULT_RECEIPT_REQUIRED');
  const run = state.orchestration;
  if (['CLAIMED', 'IN_FLIGHT'].includes(run?.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (run?.phase === 'COMPLETED_UNCONFIRMED') {
    run.phase = 'READY'; run.pages.at(-1).outcome = 'CONFIRMED';
    run.pages.at(-1).artifactReceipt = String(receipt);
    state.requestLedger[run.attemptIndex].outcome = 'CONFIRMED';
  }
  return state;
}
