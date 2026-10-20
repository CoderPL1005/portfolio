import { createHash } from 'node:crypto';
import { performance } from 'node:perf_hooks';

export class Stop extends Error {
  constructor(code) { super(code); this.code = code; }
}

export const DEFAULT_LIMITS = Object.freeze({ searches: 5, candidates: 20, durationMs: 600_000, sessions: 1 });

/** @implements {import('./contracts').IActivityBudget} */
export class ActivityBudget {
  constructor(limits = {}, now = () => performance.now()) {
    this.limits = Object.freeze({ ...DEFAULT_LIMITS, ...limits });
    for (const [key, value] of Object.entries(this.limits)) {
      if (!(key in DEFAULT_LIMITS) || !Number.isInteger(value) || value < 1 || value > DEFAULT_LIMITS[key]) {
        throw new Stop('INVALID_BUDGET');
      }
    }
    this.now = now;
    this.started = now();
    this.counts = { searches: 0, candidates: 0, sessions: 0, peakSessions: 0 };
  }
  check() {
    if (this.now() - this.started >= this.limits.durationMs) throw new Stop('TIME_BUDGET_EXCEEDED');
  }
  consume(kind) {
    this.check();
    if (!['searches', 'candidates'].includes(kind)) throw new Stop('INVALID_BUDGET_KIND');
    if (this.counts[kind] >= this.limits[kind]) throw new Stop(`${kind.toUpperCase()}_BUDGET_EXCEEDED`);
    this.counts[kind]++;
  }
  acquireSession() {
    this.check();
    if (this.counts.sessions >= this.limits.sessions) throw new Stop('SESSION_BUDGET_EXCEEDED');
    this.counts.sessions++;
    this.counts.peakSessions = Math.max(this.counts.peakSessions, this.counts.sessions);
    let released = false;
    return () => { if (!released) { this.counts.sessions--; released = true; } };
  }
  snapshot() { return { ...this.counts, elapsedMs: Math.max(0, this.now() - this.started) }; }
}

/** @implements {import('./contracts').ISearchPlanner} */
export class SearchPlanner {
  plan({ technologies, locations, seniorities }) {
    if (![technologies, locations, seniorities].every(a => Array.isArray(a) && a.length)) throw new Stop('INVALID_TARGET');
    const clean = value => {
      if (typeof value !== 'string' || !value.trim() || value.length > 80 || /[\r\n]/u.test(value)) throw new Stop('INVALID_TARGET');
      return value.normalize('NFC').trim();
    };
    return [...new Set(technologies.map((technology, i) =>
      [clean(technology), clean(seniorities[i % seniorities.length]), clean(locations[i % locations.length])].join(' ')))].slice(0, 5);
  }
}

export function facebookAccessGate({ technicalReady = false, sessionReady = false } = {}) {
  // No live adapter or authorization-evidence verifier is shipped in V1.
  // A login cookie or caller-supplied flag can never authorize collection.
  return {
    browserTechnical: technicalReady ? 'BROWSER_TECHNICALLY_READY' : 'UNVERIFIED',
    loginSession: sessionReady ? 'LOGIN_SESSION_READY' : 'UNVERIFIED',
    authorization: 'FACEBOOK_ACCESS_UNVERIFIED',
    liveDiscovery: 'LIVE_UNSEEDED_DISCOVERY_NOT_RUN',
    allowed: false
  };
}

export function requireMode(mode) {
  if (mode !== 'offline') throw new Stop('FACEBOOK_LIVE_COLLECTION_DISABLED');
}

export function inspectAccess({ url, title = '', body = '', marker = '', hasPassword = false, hasCaptcha = false }) {
  const text = `${title}\n${body}`.normalize('NFC');
  if (marker === 'denied' || hasCaptcha || /captcha|checkpoint|access[-_ ]denied/u.test(url) ||
      /verify you are human|unusual activity|access denied|truy cập bị từ chối|xác minh bạn là người/iu.test(text)) throw new Stop('ACCESS_RESTRICTED');
  if (marker === 'login' || hasPassword || /\/(?:login|signin)(?:[/?#]|$)/iu.test(url) ||
      /log in to continue|đăng nhập để tiếp tục/iu.test(text)) throw new Stop('LOGIN_REQUIRED');
}

const normalize = value => value.replace(/\r\n?/gu, '\n').normalize('NFC').trim();
const hash = value => createHash('sha256').update(normalize(value), 'utf8').digest('hex');

/** @implements {import('./contracts').IJobPostExtractor} */
export class JobPostExtractor {
  extract(post) {
    if (post.body.length > 24_000) throw new Stop('POST_TOO_LARGE');
    return {
      ...post,
      extractionStatus: post.hasImage && !post.body.trim() ? 'IMAGE_REQUIRES_EXTRACTION' : 'TEXT_EXTRACTED',
      contentHash: hash([post.title, post.employer, post.location, post.requirements, post.body].join('\n')),
      sourceIdentity: post.id ? `fixture:${post.id}` : post.sourceUrl,
      // Not a RawJobPosting/JobPosting write model. No guessed required fields.
      provenance: { source: 'OFFLINE_FIXTURE', sourceUrl: post.sourceUrl, evidenceKind: post.evidenceKind }
    };
  }
}

export function parseExperience(requirements) {
  const text = normalize(requirements).toLowerCase();
  const mandatory = text.split(/\n|[.;](?=\s)/u).filter(line =>
    !/nice to have|preferred|optional|là lợi thế|ưu tiên/iu.test(line)).join('\n');
  const values = [];
  const add = (match, min, max) => values.push({ minimum: min, maximum: max, evidence: match[0] });
  // Longer range/lower/upper matches precede bare durations and are masked to
  // prevent interpreting '1–2 years' as a second unrelated '2 years'.
  let remainder = mandatory;
  const patterns = [
    [/\b(\d+(?:\.\d+)?)\s*(?:–|—|-|to|đến)\s*(\d+(?:\.\d+)?)\s*(years?|năm|months?|tháng)\b/giu, m => {
      const factor = /month|tháng/u.test(m[3]) ? 12 : 1; return [+m[1] / factor, +m[2] / factor];
    }],
    [/(?:up to|tối đa)\s*(\d+(?:\.\d+)?)\s*(years?|năm|months?|tháng)\b/giu, m => [null, +m[1] / (/month|tháng/u.test(m[2]) ? 12 : 1)]],
    [/(?:at least|minimum|tối thiểu|từ)\s*(\d+(?:\.\d+)?)\s*(years?|năm|months?|tháng)\b/giu, m => [+m[1] / (/month|tháng/u.test(m[2]) ? 12 : 1), null]],
    [/\b(\d+(?:\.\d+)?)\s*\+?\s*(years?|năm|months?|tháng)(?:\s*(?:of\s+)?(?:experience|kinh nghiệm))?(?:\s*(?:trở lên))?/giu, m => [+m[1] / (/month|tháng/u.test(m[2]) ? 12 : 1), null]]
  ];
  for (const [pattern, bounds] of patterns) {
    remainder = remainder.replace(pattern, (...args) => {
      const match = args.slice(0, -2); const [min, max] = bounds(match); add(match, min, max); return ' '.repeat(match[0].length);
    });
  }
  if (/no experience required|không yêu cầu kinh nghiệm/iu.test(mandatory)) values.push({ minimum: 0, maximum: null, evidence: 'explicit no experience required' });
  const signatures = new Set(values.map(value => `${value.minimum}/${value.maximum}`));
  return {
    minimum: values.length ? values[0].minimum : null,
    maximum: values.length ? values[0].maximum : null,
    conflict: signatures.size > 1 || values.some(v => v.minimum !== null && v.maximum !== null && v.minimum > v.maximum),
    evidence: values.map(v => v.evidence)
  };
}

export function matchTarget(job, { profile = 'fullstack', today = new Date() } = {}) {
  if (!['backend', 'fullstack'].includes(profile)) throw new Stop('INVALID_PROFILE');
  const skills = `${job.title}\n${job.requirements}`.normalize('NFC');
  const backend = /(?:^|[^\p{L}\p{N}])(?:\.net|dotnet|c#|c sharp|asp\.net)(?=$|[^\p{L}\p{N}])/iu.test(skills);
  const angular = /(?:^|[^\p{L}\p{N}])angular(?=$|[^\p{L}\p{N}])/iu.test(skills);
  const locationVerified = /(?:^|[^\p{L}])(?:hanoi|ha noi|hà nội)(?=$|[^\p{L}])/iu.test(job.location ?? '');
  const experience = parseExperience(job.requirements ?? '');
  const junior = /\b(?:intern|internship|fresher|junior)\b/iu.test(job.title);
  const senior = /\b(?:senior|lead|middle|mid-level)\b/iu.test(job.title);
  let classification = 'NOT_MATCHED'; let reason = 'SENIORITY_UNVERIFIED';
  if (experience.conflict || junior && experience.minimum > 2) reason = 'EXPERIENCE_CONFLICT';
  else if (!backend || profile === 'fullstack' && !angular) reason = 'TECHNOLOGY_MISMATCH';
  else if (!locationVerified) reason = job.location ? 'LOCATION_MISMATCH' : 'LOCATION_UNVERIFIED';
  else if (senior || experience.minimum > 2) reason = 'EXPERIENCE_NOT_IN_TARGET';
  else if (junior) { classification = 'EXACT_ENTRY_LEVEL'; reason = 'EXPLICIT_SENIORITY'; }
  else if (experience.minimum !== null && experience.minimum >= 0 && experience.minimum <= 1 &&
      !(experience.minimum === 1 && experience.maximum === null) &&
      (experience.maximum === null || experience.maximum <= 1 || experience.minimum === 0 && experience.maximum === 2)) {
    classification = 'POTENTIAL_ENTRY_LEVEL'; reason = 'EXPLICIT_LOW_MINIMUM';
  } else if (experience.minimum !== null && experience.minimum >= 1 && experience.minimum <= 2) {
    classification = 'STRETCH_OPPORTUNITY'; reason = experience.maximum === null ? 'LOWER_BOUND_ONLY' : 'EXPLICIT_RANGE';
  }
  const published = job.publishedAt && new Date(job.publishedAt);
  const age = published && today - published;
  const freshness = !published || Number.isNaN(age) ? 'UNKNOWN' : age < 0 ? 'INVALID_FUTURE_DATE' : age <= 7 * 86400_000 ? 'RECENT_7D' : 'OLDER';
  return { profile, backend, angular, locationVerified, experience, classification, reason, freshness,
    eligibleForProductionIngestion: false };
}

export function deduplicate(jobs) {
  const identities = new Set(); const hashes = new Set(); const unique = []; const duplicates = [];
  for (const job of jobs) {
    if (identities.has(job.sourceIdentity) || hashes.has(job.contentHash)) duplicates.push(job.sourceIdentity);
    else unique.push(job);
    identities.add(job.sourceIdentity); hashes.add(job.contentHash);
  }
  return { unique, duplicates };
}

/** @implements {import('./contracts').IDiscoveryReporter} */
export class DiscoveryReporter {
  build({ jobs, duplicates, stopCode, budget, queries, blockedRequests = 0, technicalReady }) {
    return {
      evidenceKind: 'OFFLINE_FIXTURE_ONLY',
      gates: { ...facebookAccessGate({ technicalReady }),
        offline: technicalReady && !stopCode && jobs.some(j => j.match.classification !== 'NOT_MATCHED') ? 'OFFLINE_BROWSER_AGENT_PASS' : 'OFFLINE_BROWSER_AGENT_FAIL',
        cloud: 'CLOUD_BROWSER_RUNTIME_UNVERIFIED',
        persistentSession: 'PERSISTENT_SESSION_UNVERIFIED', zeroCost: 'ZERO_COST_UNVERIFIED' },
      stopCode, queries, budget: budget.snapshot(), blockedRequests,
      metrics: { uniquePosts: jobs.length, duplicates: duplicates.length,
        targetMatches: jobs.filter(j => j.match.classification !== 'NOT_MATCHED').length,
        imageExtractionRequired: jobs.filter(j => j.extractionStatus === 'IMAGE_REQUIRES_EXTRACTION').length },
      duplicates, jobs
    };
  }
}

export async function runDiscovery({ sessionProvider, adapter, planner, extractor, reporter, target, budget, signal }) {
  const controller = new AbortController();
  const stopExternal = () => controller.abort();
  signal?.addEventListener('abort', stopExternal, { once: true });
  if (signal?.aborted) controller.abort();
  const timer = setTimeout(() => controller.abort(), budget.limits.durationMs);
  const records = []; const queries = []; let session; let stopCode = null; let technicalReady = false;
  try {
    if (controller.signal.aborted) throw new Stop('TIME_BUDGET_EXCEEDED');
    session = await sessionProvider.open(budget, controller.signal); technicalReady = true;
    const urls = new Set();
    for (const query of planner.plan(target)) {
      budget.check();
      queries.push(query);
      for (const url of await adapter.search(session.page, query, budget)) {
        if (urls.has(url)) continue;
        urls.add(url);
        const job = extractor.extract(await adapter.read(session.page, url, budget));
        job.match = matchTarget(job);
        records.push(job);
      }
    }
  } catch (error) {
    // Never serialize raw Playwright exceptions: they may contain URLs or content.
    stopCode = controller.signal.aborted ? 'TIME_BUDGET_EXCEEDED' : error instanceof Stop ? error.code : 'BROWSER_OPERATION_FAILED';
  } finally {
    clearTimeout(timer); signal?.removeEventListener('abort', stopExternal);
    await session?.close();
  }
  const { unique, duplicates } = deduplicate(records);
  return reporter.build({ jobs: unique, duplicates, queries, stopCode, budget,
    blockedRequests: sessionProvider.blockedRequests ?? 0, technicalReady });
}
