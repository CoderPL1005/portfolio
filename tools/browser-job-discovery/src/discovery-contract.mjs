import { Stop } from './core.mjs';

export function validateDateFilters({ startTime, endTime } = {}) {
  const validate = value => {
    if (value === undefined || value === null) return null;
    if (typeof value !== 'string' || value.length !== 10 || !/^\d{4}-\d{2}-\d{2}$/u.test(value)) throw new Stop('INVALID_DATE_FILTER');
    const [year, month, day] = value.split('-').map(Number);
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) throw new Stop('INVALID_DATE_FILTER');
    return value;
  };
  const start = validate(startTime); const end = validate(endTime);
  if (start && end && start > end) throw new Stop('INVALID_DATE_RANGE');
  return { startTime: start, endTime: end };
}

export function validateQuery(query) {
  if (typeof query !== 'string' || !query.trim() || query.length > 300 || /[\p{C}]/u.test(query)) throw new Stop('INVALID_QUERY');
  return query.normalize('NFC').trim();
}

export function buildFacebookSearchUrl(query) {
  const url = new URL('/search/posts/', 'https://www.facebook.com');
  url.search = new URLSearchParams({ q: validateQuery(query) }).toString();
  return url.href;
}

// Deliberately narrow grammar: no redirect wrappers, search pages, profiles,
// comments, reels, guessed IDs, arbitrary hosts or URL credentials.
export function canonicalFacebookPost(input) {
  if (typeof input !== 'string' || input.length > 2048 || /[\s\\]/u.test(input)) return null;
  let url;
  try { url = new URL(input); } catch { return null; }
  if (url.protocol !== 'https:' || url.username || url.password || url.port ||
      !['facebook.com', 'www.facebook.com', 'm.facebook.com'].includes(url.hostname)) return null;
  const id = '(?:[0-9]+|pfbid[A-Za-z0-9]+)';
  const path = url.pathname.replace(/\/$/u, '');
  let match = path.match(new RegExp(`^/groups/([A-Za-z0-9.]+)/(?:(?:posts)|(?:permalink))/(${id})$`, 'u'));
  if (match) return { url: `https://www.facebook.com/groups/${match[1]}/posts/${match[2]}`, id: match[2] };
  match = path.match(new RegExp(`^/([A-Za-z0-9.]+)/posts/(${id})$`, 'u'));
  if (match && !['search', 'groups', 'pages', 'profile.php', 'login'].includes(match[1])) {
    return { url: `https://www.facebook.com/${match[1]}/posts/${match[2]}`, id: match[2] };
  }
  if (path === '/story.php' || path === '/permalink.php') {
    const story = url.searchParams.getAll('story_fbid'); const owner = url.searchParams.getAll('id');
    if (story.length === 1 && owner.length === 1 && new RegExp(`^${id}$`, 'u').test(story[0]) && /^[0-9]+$/u.test(owner[0])) {
      return { url: `https://www.facebook.com/${owner[0]}/posts/${story[0]}`, id: story[0] };
    }
  }
  return null;
}

export function candidateRecord(post, query, strategy, evidence, retrievedAt) {
  return Object.freeze({ source: 'FACEBOOK', query, candidate_post_url: post.url, source_post_id: post.id,
    retrieved_at: retrievedAt, evidence: Object.freeze(evidence),
    authorization_status: 'OFFLINE_FIXTURE_ONLY', discovery_strategy: strategy });
}

export function operationTimeout(budget, requestedMs) {
  budget.check();
  if (!Number.isInteger(requestedMs) || requestedMs < 1 || requestedMs > 10_000) throw new Stop('INVALID_TIMEOUT');
  return Math.max(1, Math.min(requestedMs, budget.limits.durationMs - budget.snapshot().elapsedMs));
}

export async function boundedOperation(operation, budget, timeoutMs) {
  const controller = new AbortController(); let timer;
  const timeout = operationTimeout(budget, timeoutMs);
  try {
    return await Promise.race([
      Promise.resolve().then(() => operation(controller.signal, timeout)),
      new Promise((_, reject) => { timer = setTimeout(() => {
        controller.abort(); reject(new Stop('DISCOVERY_TIMEOUT'));
      }, timeout); })
    ]);
  } catch (error) {
    // Do not return raw browser/provider errors, which may include secrets.
    throw error instanceof Stop ? error : new Stop('DISCOVERY_OPERATION_FAILED');
  } finally { clearTimeout(timer); }
}
