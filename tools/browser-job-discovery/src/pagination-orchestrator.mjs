import { createHash, randomBytes, createCipheriv, createDecipheriv } from 'node:crypto';
import { Stop, ActivityBudget, JobPostExtractor } from './core.mjs';
import { validateDateFilters, validateQuery } from './discovery-contract.mjs';
import { SocialApisLiveDiscoveryAdapter } from './socialapis-adapter.mjs';
import { classifyCandidateProfile } from './profile-matching.mjs';
import { fingerprint, normalizeQuery, queryYield } from './candidate-technology-profile.mjs';
import { opportunityIds, buildContentRegistry, nearDuplicateCandidates } from './content-opportunities.mjs';
import { targetJobPreferences, preferencesFingerprint } from './target-job-preferences.mjs';
import { classifyBackendProfile, rankBackendOpportunities } from './backend-matching.mjs';

const digest = text => createHash('sha256').update(text).digest('hex');
const fold = text => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
const CONTINUATION_TTL_MS = 24 * 60 * 60 * 1000; // Local safety bound, not a provider validity guarantee.
const stable = value => JSON.stringify(value, (_key, v) => v && typeof v === 'object' && !Array.isArray(v)
  ? Object.fromEntries(Object.keys(v).sort().map(k => [k, v[k]])) : v);
const compatiblePolicy = config => { const { maxRequests, maxCredits, ...policy } = config; return stable(policy); };
const currentAttempts = state => state.requestLedger.filter(p => p.workflowRunId === state.orchestration.workflowRunId);
const validCursor = value => typeof value === 'string' && !!value.trim() && value.length <= 4096 && !/[\p{C}]/u.test(value);
function knownTerminalFailure(state) {
  const run=state.orchestration,attempt=state.requestLedger?.[run?.attemptIndex],stats=attempt?.itemStats;
  const reasons=new Set(['ITEM_NOT_OBJECT','POST_ID_MISSING','POST_ID_INVALID','POST_ID_URL_MISMATCH',
    'POST_URL_INVALID_OR_UNSUPPORTED','POST_URL_MISSING','POST_TEXT_TOO_LARGE','POST_MAPPING_INVALID']);
  return !!(run?.plan && run.config && attempt && Array.isArray(run.pages) && typeof run.logicalExecutionId==='string' &&
    run.logicalExecutionId.length>0 && run.status==='STOPPED_TERMINAL' && run.stopReason==='NO_VALID_PROVIDER_ITEMS' &&
    run.attemptIndex===state.requestLedger.length-1 && attempt.workflowRunId===run.workflowRunId &&
    attempt.logicalExecutionId===run.logicalExecutionId && attempt.query===run.plan.query &&
    attempt.allocation===run.plan.allocation && attempt.pageNumber===run.plan.pageNumber &&
    attempt.startTime===run.config.startTime && attempt.endTime===run.config.endTime &&
    (attempt.inputCursorFingerprint??attempt.inputCursor??null)===(run.plan.cursorHash??null) &&
    attempt.outcome==='FAILED_OR_AMBIGUOUS' && attempt.actualRequests===1 &&
    Number.isFinite(attempt.creditsDeducted) && attempt.creditsDeducted>=0 && attempt.creditsDeducted<=1 &&
    (attempt.httpStatus===undefined || attempt.httpStatus===200) && stats?.outcome==='NO_VALID_ITEMS' &&
    Number.isInteger(stats.totalItems) && stats.totalItems>0 && stats.totalItems<=200 && stats.acceptedItems===0 &&
    stats.rejectedItems===stats.totalItems && Array.isArray(stats.rejectionReasons) && stats.rejectionReasons.length===stats.totalItems &&
    Array.isArray(attempt.rejectionDiagnostics) && attempt.rejectionDiagnostics.length===stats.totalItems &&
    stats.rejectionReasons.every((r,index)=>r.index===index && reasons.has(r.reason) &&
      attempt.rejectionDiagnostics[index]?.index===index && attempt.rejectionDiagnostics[index].reason===r.reason &&
      attempt.rejectionDiagnostics[index].pageNumber===attempt.pageNumber) &&
    !run.pages.some(p=>p.workflowRunId===attempt.workflowRunId && p.query===attempt.query &&
      p.allocation===attempt.allocation && p.pageNumber===attempt.pageNumber));
}

// Evidence is supplied by the trusted artifact-download/upload boundary, not by
// provider content. A positive receipt alone is not enough. Historical counters,
// outcome, rejection diagnostics and registry remain byte-for-byte equivalent.
export function reconcileTerminalFailure(state, proof) {
  const run=state.orchestration,attempt=state.requestLedger?.[run?.attemptIndex];
  const positive=value=>/^[1-9]\d*$/u.test(String(value??''));
  const name=typeof proof?.artifactName==='string' ? proof.artifactName.match(/^socialapis-page-([1-9]\d*)-([1-6])-result$/u) : null;
  // A workflow slot is the ordinal reservation in that workflow, not the global
  // ledger index or query page number (which resets for rotating/adaptive queries).
  const slot=state.requestLedger?.slice(0,run?.attemptIndex+1).filter(a=>a.workflowRunId===run?.workflowRunId).length;
  if (!['CLAIMED','TERMINAL_UNCONFIRMED'].includes(run?.phase) || !knownTerminalFailure(state) ||
      !positive(proof?.artifactId) || !positive(proof?.claimArtifactId) || String(proof.artifactId)===String(proof.claimArtifactId) ||
      proof.workflowRunId!==run.workflowRunId || proof.attemptIndex!==run.attemptIndex || proof.artifactKind!=='RESULT' ||
      !['ACTIONS_RESULT','LOCAL_OBSERVED_RESULT_HASH'].includes(proof.provenance) ||
      proof.provenance==='ACTIONS_RESULT' && !name || proof.artifactName!==undefined && (!name || name[1]!==run.workflowRunId || Number(name[2])!==slot) ||
      proof.stateFingerprint!==fingerprint(state) ||
      attempt.claimArtifactReceipt!==undefined && attempt.claimArtifactReceipt!==String(proof.claimArtifactId) ||
      attempt.claimArtifactName!==undefined && attempt.claimArtifactName!==(proof.artifactName??'').replace(/-result$/u,'-claim'))
    throw new Stop('TERMINAL_RECONCILIATION_EVIDENCE_INVALID');
  state.terminalReconciliations??=[];
  state.terminalReconciliations.push({version:1,workflowRunId:run.workflowRunId,logicalExecutionId:run.logicalExecutionId,
    attemptIndex:run.attemptIndex,artifactId:String(proof.artifactId),claimArtifactId:String(proof.claimArtifactId),
    artifactName:proof.artifactName,provenance:proof.provenance,stateFingerprint:proof.stateFingerprint,priorPhase:run.phase,
    stopReason:run.stopReason,actualRequests:attempt.actualRequests,creditsDeducted:attempt.creditsDeducted});
  run.phase='TERMINAL_RECONCILED';
  return state;
}
function stopExecution(run, reason) {
  run.stopReason = reason;
  run.status = ['REQUEST_BUDGET', 'CREDIT_BUDGET', 'DAILY_REQUEST_BUDGET'].includes(reason) && run.continuation
    ? 'STOPPED_RESUMABLE' : 'STOPPED_TERMINAL';
}
function validateContinuation(run, config, now) {
  if (run.schemaVersion !== 3) throw new Stop('LEGACY_CONTINUATION_CURSOR_UNAVAILABLE');
  const continuation = run.continuation;
  if (!continuation || run.status === 'STOPPED_TERMINAL') throw new Stop('CONTINUATION_UNAVAILABLE');
  if (compatiblePolicy(run.config) !== compatiblePolicy(config)) throw new Stop('CONTINUATION_POLICY_MISMATCH');
  if (!Number.isFinite(Date.parse(continuation.expiresAt)) || !Number.isFinite(Date.parse(continuation.savedAt)) ||
      Date.parse(continuation.expiresAt) - Date.parse(continuation.savedAt) !== CONTINUATION_TTL_MS ||
      now.getTime() < Date.parse(continuation.savedAt) || now.getTime() >= Date.parse(continuation.expiresAt)) throw new Stop('CONTINUATION_EXPIRED');
  const plan = continuation.plan; const last = run.pages.filter(p=>p.allocation==='CORE').at(-1);
  const base64 = (text, length) => typeof text === 'string' && text.length <= 24000 &&
    Buffer.from(text, 'base64').toString('base64') === text && (length ? Buffer.from(text, 'base64').length === length : text.length > 0);
  if (!plan || plan.allocation !== 'CORE' || plan.query !== config.query || !Number.isInteger(plan.pageNumber) || plan.pageNumber < 2 || plan.pageNumber > 4 ||
      last?.allocation !== 'CORE' || last.pageNumber + 1 !== plan.pageNumber || !last.nextCursorPresent ||
      !/^[a-f0-9]{64}$/u.test(plan.cursorHash ?? '') || last.nextCursorFingerprint !== plan.cursorHash ||
      run.pages.some(p => (p.inputCursorFingerprint ?? p.inputCursor) === plan.cursorHash) ||
      run.usedCursorHashes.filter(hash => hash === plan.cursorHash).length !== 1 ||
      !base64(plan.cursor?.iv, 12) || !base64(plan.cursor?.tag, 16) || !base64(plan.cursor?.data) ||
      !run.deferredCore && stable(run.plan) !== stable(plan)) throw new Stop('CONTINUATION_INVALID');
  if (run.pages.some(p => p.outcome !== 'CONFIRMED' || !/^[1-9]\d*$/u.test(String(p.artifactReceipt ?? '')) ||
      p.actualRequests !== 1 || !Number.isFinite(p.creditsDeducted) || p.creditsDeducted < 0 || p.creditsDeducted > 1)) throw new Stop('PERSISTENCE_CONFIRMATION_REQUIRED');
}
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
  if (profile.technologyProfile) {
    const { fingerprint: recorded, ...safe }=profile.technologyProfile;
    if (safe.version!=='portfolio-technology-v1' || safe.source!=='OPERATOR_SUPPLIED_PUBLIC_EXPORT_NOT_LIVE_VERIFIED' ||
        !Array.isArray(safe.technologies) || safe.technologies.length>100 ||
        safe.technologies.some(t=>typeof t.name!=='string' || !t.name.trim() || t.name.length>100 || /[\p{C}]/u.test(t.name) || !['USED','LEARNING','EXPLORING'].includes(t.level) || !Array.isArray(t.evidence)) ||
        stable(safe.locations)!==stable(profile.locations) || stable(safe.seniorities)!==stable(profile.seniorities) ||
        stable(safe.technologies.filter(t=>t.level==='USED').map(t=>t.name))!==stable(profile.technologies) || recorded!==fingerprint(safe)) throw new Stop('INVALID_TECHNOLOGY_PROFILE');
  }
  if(input.targetJobPreferences && !profile.technologyProfile)throw new Stop('PORTFOLIO_TECHNOLOGY_PROFILE_REQUIRED_FOR_BACKEND_TARGET');
  if (input.adaptiveQueries && (!Array.isArray(input.adaptiveQueries) || input.adaptiveQueries.length > 16)) throw new Stop('INVALID_ADAPTIVE_POOL');
  return { query: validateQuery(input.query), ...validateDateFilters(input), maxRequests, maxCredits, target, minMarginal,
    ...(input.targetJobPreferences ? {targetJobPreferences:targetJobPreferences(input.targetJobPreferences)} : {}),
    rotatingQuery: input.rotatingQuery ? validateQuery(input.rotatingQuery) : null,
    adaptiveQueries: [...new Set((input.adaptiveQueries ?? []).map(validateQuery))].sort(), profile,
    mode: input.mode ?? 'development' };
}
// Future Portfolio integration supplies this profile, not another technology DB.
// Existing extraction evidence is reused; UNKNOWN publication dates stay UNKNOWN.
export function classifyProfile(job, profile, targetPreferences) {
  return targetPreferences ? classifyBackendProfile(job,profile,targetPreferences) : classifyCandidateProfile(job,profile);
}
export function beginExecution(state, input, workflowRunId, { productionGuard, resumeContinuation = false, now = new Date() } = {}) {
  const config = configuration(input);
  if (config.mode !== 'development' && config.mode !== 'production') throw new Stop('INVALID_MODE');
  if (config.mode === 'production' && (typeof productionGuard !== 'function' || productionGuard() !== true)) throw new Stop('MONTHLY_FREE_BALANCE_UNVERIFIED_SCHEDULE_DISABLED');
  if (!Array.isArray(state.jobs) || state.jobs.some(j => j.provider !== 'SOCIALAPIS' || !j.id || j.sourceIdentity !== `SOCIALAPIS:${j.id}`) ||
      new Set(state.jobs.map(j => j.sourceIdentity)).size !== state.jobs.length) throw new Stop('INVALID_REPLAY_STORE');
  const previous = state.orchestration;
  if (['CLAIMED', 'IN_FLIGHT'].includes(previous?.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (['COMPLETED_UNCONFIRMED','TERMINAL_UNCONFIRMED'].includes(previous?.phase)) throw new Stop('PERSISTENCE_CONFIRMATION_REQUIRED');
  if (resumeContinuation) {
    if (!previous) throw new Stop('LEGACY_CONTINUATION_CURSOR_UNAVAILABLE');
    validateContinuation(previous, config, now);
    if (previous.workflowRunId === workflowRunId) throw new Stop('RESUME_REQUIRES_NEW_WORKFLOW');
    previous.config = config; previous.configHash = digest(JSON.stringify(config));
    previous.workflowRunId = workflowRunId; previous.stopReason = null; previous.status = 'RUNNING';
    previous.plan = structuredClone(previous.continuation.plan);
    previous.deferredCore = false;
    return state;
  }
  if (previous && !previous.stopReason) {
    if (previous.workflowRunId !== workflowRunId && previous.pages.length) throw new Stop('RESUME_CONTINUATION_REQUIRED');
    if (previous.configHash !== digest(JSON.stringify(config))) throw new Stop('PENDING_EXECUTION_CONFIG_MISMATCH');
    previous.workflowRunId = workflowRunId;
    previous.logicalExecutionId ??= `legacy-${workflowRunId}`;
    return state;
  }
  state.evidenceKind = 'SOCIALAPIS_DISCOVERY_STATE'; state.requestLedger ??= [];
  state.discoveryYieldHistory ??= [];
  state.orchestration = { schemaVersion: 3, logicalExecutionId: `${workflowRunId}-${randomBytes(8).toString('hex')}`,
    config, configHash: digest(JSON.stringify(config)), workflowRunId, phase: 'READY', status: 'RUNNING', stopReason: null, continuation: null,
    initialIdentities: state.jobs.map(j => j.sourceIdentity), pages: [], newRelevantKeys: [], relevantKeys: [],
    opportunityMode: 'CONTENT_V1', initialOpportunityIds:[...new Set(state.jobs.flatMap(opportunityIds))], newRelevantOpportunityIds:[],
    plan: { allocation: 'CORE', query: config.query, pageNumber: 1, cursor: null, cursorHash: null },
    usedCursorHashes: [], usedQueries: [config.query], storage: { inserted: 0, updated: 0, unchanged: 0 } };
  // Derived recommendations are evaluated under THIS policy, never reused from
  // a previous target. Historical jobs, identities and the ledger stay intact.
  if(config.targetJobPreferences)state.rankedOpportunities=rankBackendOpportunities(state.jobs,config.profile,config.targetJobPreferences,config.target);
  else delete state.rankedOpportunities;
  return state;
}
export function workflowMetrics(state) {
  const run = state.orchestration;
  const attempts = state.requestLedger.filter(p => p.workflowRunId === run.workflowRunId);
  const pages = run.pages.filter(p => p.workflowRunId === run.workflowRunId);
  const known = attempts.reduce((sum, p) => sum + (p.creditsDeducted ?? 0), 0);
  const knownRequests = attempts.reduce((sum, p) => sum + (p.actualRequests ?? 0), 0);
  const scopedOpportunities=new Set(pages.flatMap(p=>p.opportunityIds??[]));
  const logicalAttempts = [...run.pages]; const pending = state.requestLedger[run.attemptIndex];
  if (pending && ['CLAIMED','IN_FLIGHT','TERMINAL_UNCONFIRMED','TERMINAL_RECONCILED'].includes(run.phase) && !run.pages.some(p => p.workflowRunId === pending.workflowRunId && p.query === pending.query && p.allocation === pending.allocation && p.pageNumber === pending.pageNumber)) logicalAttempts.push(pending);
  return { workflowRunId: run.workflowRunId, logicalExecutionId: run.logicalExecutionId,
    status: run.status, continuationAvailable: run.status!=='STOPPED_TERMINAL' && !!run.continuation && !['CLAIMED','IN_FLIGHT'].includes(run.phase),
    profileVersion:run.config.profile.technologyProfile?.version??'legacy-operator-profile',
    profileFingerprint:run.config.profile.technologyProfile?.fingerprint??fingerprint(run.config.profile),
    canonicalOpportunities:scopedOpportunities.size,newlyDiscoveredOpportunities:pages.reduce((s,p)=>s+(p.newlyDiscoveredOpportunities??0),0),
    newRelevantOpportunities:pages.reduce((s,p)=>s+(p.newRelevantOpportunities??0),0),
    newRelevantOpportunitiesPerCredit:known>0 && attempts.every(p=>p.creditsDeducted!==null) ? pages.reduce((s,p)=>s+(p.newRelevantOpportunities??0),0)/known : null,
    freshnessUnknown:pages.reduce((s,p)=>s+(p.freshnessUnknown??0),0),freshnessUnverified:pages.reduce((s,p)=>s+(p.freshnessUnverified??0),0),
    queryDepths:[...new Set(pages.map(p=>p.normalizedQueryIdentity??normalizeQuery(p.query)))].map(identity=>({normalizedQueryFingerprint:fingerprint(identity),
      pagesSurveyed:pages.filter(p=>(p.normalizedQueryIdentity??normalizeQuery(p.query))===identity).length,
      maximumPageNumber:Math.max(...pages.filter(p=>(p.normalizedQueryIdentity??normalizeQuery(p.query))===identity).map(p=>p.pageNumber)),
      conclusion:'OBSERVED_DEPTH_ONLY_NOT_PROOF_OF_NO_FURTHER_RESULTS'})),
    actualRequests: attempts.some(p => p.actualRequests === null) ? null : knownRequests,
    knownActualRequests: knownRequests, reservedAttempts: attempts.length, uncertainAttempts: attempts.filter(p => p.actualRequests === null).length,
    totalDeductedCredits: attempts.some(p => p.creditsDeducted === null) ? null : known, knownDeductedCredits: known,
    stopReason: run.stopReason, uniqueRelevantVacancies: new Set(pages.flatMap(p => p.relevantVacancyKeys)).size,
    newRelevantVacancies: pages.reduce((sum, p) => sum + p.newRelevantVacancies, 0),
    overlapAcrossPages: pages.reduce((sum, p) => sum + p.overlap, 0),
    storage: { inserted: pages.reduce((sum,p) => sum+p.storage.inserted,0), updated: pages.reduce((sum,p) => sum+p.storage.updated,0),
      unchanged: pages.reduce((sum,p) => sum+p.storage.unchanged,0), totalCandidates: state.jobs.length },
    logicalExecution: { completedPages: run.pages.length, uniqueRelevantVacancies: run.relevantKeys.length, newRelevantVacancies: run.newRelevantKeys.length,
      newRelevantOpportunities:run.newRelevantOpportunityIds?.length??null,
      actualRequests: logicalAttempts.some(p => p.actualRequests === null) ? null : logicalAttempts.reduce((sum,p) => sum+(p.actualRequests ?? 0),0),
      creditsDeducted: logicalAttempts.some(p => p.creditsDeducted === null) ? null : logicalAttempts.reduce((sum,p) => sum+(p.creditsDeducted ?? 0),0) } };
}
export function claimPage(state, now = new Date(), { productionGuard } = {}) {
  const run = state.orchestration; const config = run.config;
  if (['CLAIMED', 'IN_FLIGHT'].includes(run.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (['COMPLETED_UNCONFIRMED','TERMINAL_UNCONFIRMED'].includes(run.phase)) throw new Stop('PERSISTENCE_CONFIRMATION_REQUIRED');
  if (run.stopReason) return false;
  if (config.mode === 'production') {
    if (state.requestLedger.filter(p => p.day === calendarDay(now)).length >= 6) { stopExecution(run, 'DAILY_REQUEST_BUDGET'); return false; }
    if (typeof productionGuard !== 'function' || productionGuard() !== true) throw new Stop('MONTHLY_FREE_BALANCE_UNVERIFIED_SCHEDULE_DISABLED');
  }
  if (currentAttempts(state).length >= config.maxRequests) { stopExecution(run, 'REQUEST_BUDGET'); return false; }
  if (currentAttempts(state).reduce((sum, p) => sum + (p.creditsDeducted ?? 0), 0) + 1 > config.maxCredits) { stopExecution(run, 'CREDIT_BUDGET'); return false; }
  const attempt = { workflowRunId: run.workflowRunId, logicalExecutionId: run.logicalExecutionId, day: calendarDay(now), allocation: run.plan.allocation,
    query: run.plan.query, pageNumber: run.plan.pageNumber, inputCursor: run.plan.cursorHash,
    inputCursorFingerprint: run.plan.cursorHash,
    startTime: config.startTime, endTime: config.endTime,
    actualRequests: null, creditsDeducted: null, outcome: 'RESERVED_AMBIGUOUS_UNTIL_COMPLETED' };
  state.requestLedger.push(attempt); run.attemptIndex = state.requestLedger.length - 1; run.phase = 'CLAIMED';
  return true;
}
function chooseNext(state, page, nextCursor, secret, now) {
  const run = state.orchestration; const config = run.config;
  if (page.allocation==='CORE' || !run.deferredCore) run.continuation = null;
  page.continuationAvailable = !!run.continuation;
  const targetCount=run.opportunityMode==='CONTENT_V1' ? run.newRelevantOpportunityIds.length : run.newRelevantKeys.length;
  if (targetCount >= config.target) { run.continuation=null;page.continuationAvailable=false;stopExecution(run, 'SUFFICIENT_NEW_VACANCIES'); return; }
  let coreStop = null;
  if (!nextCursor) coreStop = 'NO_CURSOR';
  else if (run.usedCursorHashes.includes(digest(nextCursor))) coreStop = 'REPEATED_CURSOR';
  // A low-yield or empty page does not establish that later pages are useless.
  // Keep minMarginal in config for checkpoint/hash compatibility, not as a stop.
  else if (page.pageNumber >= 4) coreStop = 'CORE_BUDGET';
  if (page.allocation === 'CORE' && !coreStop) {
    const hash = digest(nextCursor); run.usedCursorHashes.push(hash);
    run.plan = { ...run.plan, pageNumber: page.pageNumber + 1, cursor: seal(nextCursor, secret), cursorHash: hash };
    run.continuation = { plan: structuredClone(run.plan), savedAt: now.toISOString(), expiresAt: new Date(now.getTime() + CONTINUATION_TTL_MS).toISOString() };
    page.continuationAvailable = true;
  }
  // Persist the next core plan BEFORE stopping for this workflow's budget.
  if (currentAttempts(state).length >= config.maxRequests) { stopExecution(run, 'REQUEST_BUDGET'); return; }
  if (currentAttempts(state).reduce((sum,p) => sum+(p.creditsDeducted ?? 0),0)+1 > config.maxCredits) { stopExecution(run, 'CREDIT_BUDGET'); return; }
  if (run.continuation) {
    const corePages=run.pages.filter(p=>p.allocation==='CORE');
    const alternative=config.rotatingQuery && !run.usedQueries.includes(config.rotatingQuery) ? config.rotatingQuery : null;
    const targetFingerprint=config.targetJobPreferences ? preferencesFingerprint(config.targetJobPreferences) : undefined;
    const measured=alternative && config.profile.technologyProfile ? queryYield(state.discoveryYieldHistory,alternative,config.profile.technologyProfile.fingerprint,targetFingerprint) : null;
    if (page.allocation==='CORE' && corePages.length>=2 && corePages.slice(-2).every(p=>p.newRelevantOpportunities===0) && measured?.score>0) {
      run.deferredCore=true;run.usedQueries.push(alternative);run.plan={allocation:'ROTATING',query:alternative,pageNumber:1,cursor:null,cursorHash:null};
      page.allocationDecision='MEASURED_ROTATING_YIELD_PREFERRED_CORE_CURSOR_RETAINED';return;
    }
    if (run.deferredCore) {run.plan=structuredClone(run.continuation.plan);run.deferredCore=false;}
    return;
  }
  let query = null; let allocation = null;
  if (config.rotatingQuery && !run.usedQueries.includes(config.rotatingQuery)) { query = config.rotatingQuery; allocation = 'ROTATING'; }
  else {
    const profileFingerprint=config.profile.technologyProfile?.fingerprint;
    const targetFingerprint=config.targetJobPreferences ? preferencesFingerprint(config.targetJobPreferences) : undefined;
    const hasYield = q => profileFingerprint ? queryYield(state.discoveryYieldHistory,q,profileFingerprint,targetFingerprint).score!==null
      : state.discoveryYieldHistory.some(p => p.query === q && typeof p.creditsDeducted === 'number' && p.creditsDeducted > 0);
    const candidates = config.adaptiveQueries.filter(q => !run.usedQueries.includes(q) && hasYield(q));
    const score = q => {
      if (profileFingerprint) return queryYield(state.discoveryYieldHistory,q,profileFingerprint,targetFingerprint).score??-1;
      const history = state.discoveryYieldHistory.filter(p => p.query === q && typeof p.creditsDeducted === 'number' && p.creditsDeducted > 0);
      return history.length ? history.reduce((s, p) => s + p.newRelevantVacancies, 0) / history.reduce((s, p) => s + p.creditsDeducted, 0) : -1;
    };
    candidates.sort((a, b) => score(b) - score(a) || (a < b ? -1 : a > b ? 1 : 0));
    if (!run.pages.some(p => p.allocation === 'ADAPTIVE') && candidates.length) { query = candidates[0]; allocation = 'ADAPTIVE'; }
  }
  if (!query) { stopExecution(run, coreStop ?? 'ALLOCATION_COMPLETE'); return; }
  run.usedQueries.push(query); run.plan = { allocation, query, pageNumber: 1, cursor: null, cursorHash: null };
}
// Caller must upload the CLAIMED state before calling this function, then upload
// the completed state before acknowledgePersistence. No in-process retry exists.
export async function performPage(state, { transport, now = new Date(), receipt, enabled, freePlanConfirmed, noCashConfirmed,
  secret = process.env.SOCIALAPIS_API_KEY, timeoutMs = 20_000, claimArtifactName, captureReviews = false } = {}) {
  const run = state.orchestration;
  if (run.phase !== 'CLAIMED' || !/^[1-9]\d*$/u.test(String(receipt ?? ''))) throw new Stop('DURABLE_CLAIM_RECEIPT_REQUIRED');
  const attempt = state.requestLedger[run.attemptIndex];
  if (attempt.day !== calendarDay(now)) throw new Stop('RESERVATION_DAY_CHANGED_NO_REQUEST');
  run.phase = 'IN_FLIGHT'; // Synchronous in-process claim; durable file is still CLAIMED.
  attempt.claimArtifactReceipt=String(receipt);
  if(claimArtifactName!==undefined)attempt.claimArtifactName=claimArtifactName;
  let adapter;
  try {
    cursorKey(secret); // Validate codec before any possible HTTP side effect.
    const inputCursor = open(run.plan.cursor, secret);
    if (run.plan.pageNumber > 1 && (!validCursor(inputCursor) || digest(inputCursor) !== run.plan.cursorHash)) throw new Stop('CONTINUATION_INVALID');
    adapter = new SocialApisLiveDiscoveryAdapter({ enabled, freePlanConfirmed, noCashConfirmed,
      query: run.plan.query, ...run.config, ...{ query: run.plan.query, maxRequests: 1, maxCredits: 1 },
      timeoutMs, endCursor: inputCursor,
      ...(captureReviews ? {reviewContext:{profile:run.config.profile,targetJobPreferences:run.config.targetJobPreferences,evaluatedAt:now.toISOString()}} : {}) }, transport);
    const budget = new ActivityBudget();
    const urls = await adapter.search(undefined, adapter.query, budget);
    // A provider page is bounded to 200 records by the existing adapter. Extract
    // all valid records, not only the old simulator's 20-candidate browser cap.
    const jobs = []; const ids = new Set();
    for (const url of urls) {
      budget.check();
      const raw = adapter.adapter.records.get(url);
      if (ids.has(raw.id)) continue; ids.add(raw.id);
      jobs.push(classifyProfile(new JobPostExtractor().extract(structuredClone(raw)), run.config.profile, run.config.targetJobPreferences));
    }
    const stored = new Map(state.jobs.map(j => [j.sourceIdentity, j]));
    const historicalJobs=[...stored.values()]; const priorOpportunities=new Set(historicalJobs.flatMap(opportunityIds));
    const relevantOpportunities=new Set(jobs.flatMap(j=>opportunityIds(j).filter((_id,index)=>j.vacancies[index].match.classification==='TARGET_MATCH')));
    const responseOpportunities=new Set(jobs.flatMap(opportunityIds));
    const newOpportunities=[...responseOpportunities].filter(id=>!priorOpportunities.has(id));
    run.newRelevantOpportunityIds??=[];
    run.initialOpportunityIds??=[...new Set(historicalJobs.filter(j=>run.initialIdentities.includes(j.sourceIdentity)).flatMap(opportunityIds))];
    for(const id of relevantOpportunities) if(!run.initialOpportunityIds.includes(id) && !run.newRelevantOpportunityIds.includes(id))run.newRelevantOpportunityIds.push(id);
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
      normalizedQueryIdentity:normalizeQuery(attempt.query), profileVersion:run.config.profile.technologyProfile?.version??'legacy-operator-profile',
      profileFingerprint:run.config.profile.technologyProfile?.fingerprint??fingerprint(run.config.profile),
      ...(run.config.targetJobPreferences ? {targetPreferencesFingerprint:preferencesFingerprint(run.config.targetJobPreferences)} : {}),
      uniqueSourcePosts:jobs.length, canonicalOpportunities:responseOpportunities.size, newlyDiscoveredOpportunities:newOpportunities.length,
      opportunityIds:[...responseOpportunities],
      newRelevantOpportunities:[...relevantOpportunities].filter(id=>!priorOpportunities.has(id)).length,
      relevantOpportunityIds:[...relevantOpportunities],
      duplicateRepostRatio:responseOpportunities.size ? 1-newOpportunities.length/jobs.reduce((s,j)=>s+j.vacancies.length,0) : 0,
      nearDuplicateCandidates:nearDuplicateCandidates(jobs,historicalJobs),
      httpStatus: adapter.requestStats.httpStatus,
      creditsDeducted: adapter.requestStats.creditsDeducted, creditsUsedAlternateHeader: adapter.requestStats.creditsUsedAlternateHeader,
      returnedPostIds: jobs.map(j => j.id), newlyInsertedIds: inserted, nextCursorPresent: !!validNext,
      nextCursorFingerprint: validNext ? digest(validNext) : null,
      rejectionReasons: adapter.itemStats.rejectionReasons.map(r => ({ ...r, pageNumber: attempt.pageNumber })),
      rejectionDiagnostics: adapter.rejectionDiagnostics.map(r => ({ ...r, pageNumber: attempt.pageNumber })),
      storage: Object.fromEntries(Object.keys(run.storage).map(key => [key, run.storage[key] - beforeStorage[key]])),
      relevantVacancyKeys: jobs.flatMap(j => j.vacancies.flatMap((v,index) => v.match.classification === 'TARGET_MATCH' ? [`${j.sourceIdentity}:${index}`] : [])),
      relevantVacancies: jobs.reduce((sum, j) => sum + j.vacancies.filter(v => v.match.classification === 'TARGET_MATCH').length, 0),
      fallbackVacancies: jobs.reduce((sum,j) => sum+j.vacancies.filter(v=>v.match.classification==='FALLBACK_MATCH').length,0),
      newRelevantVacancies: run.newRelevantKeys.length - previousNew, freshnessUnknown: jobs.filter(j => j.match.freshness === 'UNKNOWN').length,
      freshnessUnverified:jobs.filter(j=>j.publishedAt!==null).length,
      freshnessUnknownVacancies: jobs.reduce((sum,j) => sum+j.vacancies.filter(v => v.match.freshness === 'UNKNOWN').length,0),
      verifiedRecentVacancies: 0, providerDateFilterBehavior: 'UNVERIFIED',
      overlap: jobs.filter(j => beforeIds.has(j.sourceIdentity) && run.pages.some(p => p.returnedPostIds.includes(j.id))).length,
      outcome: 'COMPLETED_UNCONFIRMED' };
    state.jobs = [...stored.values()]; Object.assign(attempt, { actualRequests: page.actualRequests, creditsDeducted: page.creditsDeducted, outcome: 'COMPLETED_UNCONFIRMED' });
    page.newRelevantOpportunitiesPerCredit=page.creditsDeducted>0 ? page.newRelevantOpportunities/page.creditsDeducted : null;
    state.contentRegistry=buildContentRegistry(state.jobs);
    if(run.config.targetJobPreferences)state.rankedOpportunities=rankBackendOpportunities(state.jobs,run.config.profile,run.config.targetJobPreferences,run.config.target);
    run.pages.push(page); state.discoveryYieldHistory.push({ query: page.query, normalizedQueryIdentity:page.normalizedQueryIdentity,
      profileFingerprint:page.profileFingerprint,pageNumber:page.pageNumber,newRelevantOpportunities:page.newRelevantOpportunities,
      ...(page.targetPreferencesFingerprint ? {targetPreferencesFingerprint:page.targetPreferencesFingerprint} : {}),
      startTime:page.startTime,endTime:page.endTime,
      duplicateRepostRatio:page.duplicateRepostRatio,actualRequests:page.actualRequests,
      newRelevantVacancies: page.newRelevantVacancies, creditsDeducted: page.creditsDeducted });
    state.discoveryYieldHistory = state.discoveryYieldHistory.slice(-100);
    chooseNext(state, page, validNext, secret, now); run.phase = 'COMPLETED_UNCONFIRMED';
  } catch (error) {
    attempt.actualRequests = adapter?.requestStats.requestCount ?? 0;
    attempt.creditsDeducted = adapter?.requestStats.creditsDeducted ?? null;
    if(adapter?.requestStats.httpStatus!==undefined)attempt.httpStatus=adapter.requestStats.httpStatus;
    attempt.outcome = 'FAILED_OR_AMBIGUOUS'; run.stopReason = error instanceof Stop ? error.code : 'PAGE_FAILED';
    if (adapter?.itemStats) {
      attempt.itemStats = adapter.itemStats;
      attempt.rejectionDiagnostics = (adapter.rejectionDiagnostics ?? []).map(r => ({ ...r, pageNumber: attempt.pageNumber }));
    }
    run.status = 'STOPPED_TERMINAL';
    // Only complete, schema-validated all-invalid responses with verified cost
    // qualify. Timeouts, unknown cost and all other failures remain ambiguous.
    run.phase = knownTerminalFailure(state) ? 'TERMINAL_UNCONFIRMED' : 'CLAIMED';
  }
  if(captureReviews && Array.isArray(adapter?.adapter?.reviewItems)) {
    const {reviewExport}=await import('./discovery-review.mjs');
    state.discoveryReviews??=[];
    state.discoveryReviews.push(reviewExport(adapter.adapter.reviewItems,{runId:String(run.workflowRunId),query:attempt.query,pageNumber:attempt.pageNumber}));
  }
  state.workflowMetrics = workflowMetrics(state);
  return state;
}
export function acknowledgePersistence(state, receipt, resultEvidence) {
  if (!/^[1-9]\d*$/u.test(String(receipt ?? ''))) throw new Stop('DURABLE_RESULT_RECEIPT_REQUIRED');
  const run = state.orchestration;
  if(run?.phase==='TERMINAL_UNCONFIRMED')return reconcileTerminalFailure(state,{...resultEvidence,artifactId:String(receipt)});
  if (['CLAIMED', 'IN_FLIGHT'].includes(run?.phase)) throw new Stop('AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH');
  if (run?.phase === 'COMPLETED_UNCONFIRMED') {
    run.phase = 'READY'; run.pages.at(-1).outcome = 'CONFIRMED';
    run.pages.at(-1).artifactReceipt = String(receipt);
    state.requestLedger[run.attemptIndex].outcome = 'CONFIRMED';
  }
  return state;
}
