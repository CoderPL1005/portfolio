// Cloudflare Worker: scheduled-only GitHub Actions dispatcher.
// This Worker NEVER calls Render, Neon, SocialAPIs or Gemini directly.
export const PRIMARY_CRON = '0 0,3,6,9,12,15 * * *';
export const FALLBACK_CRON = '30 0,3,6,9,12 * * *';
const ALLOWED_HOURS = new Set([0, 3, 6, 9, 12, 15]);
const MAX_CLOUDFLARE_DELAY_MS = 20 * 60_000;

/** Maps Cloudflare's grouped cron to the already-authorized backend slot expression. */
export function resolveScheduledSlot(cron, scheduledTime, nowMs = Date.now()) {
  const minute = cron === PRIMARY_CRON ? 0 : cron === FALLBACK_CRON ? 30 : null;
  if (minute === null) return { state: 'UNRECOGNIZED_CRON' };
  const scheduled = new Date(scheduledTime);
  if (!Number.isFinite(scheduled.getTime())) return { state: 'INVALID_SCHEDULED_TIME' };
  const hour = scheduled.getUTCHours();
  if (!ALLOWED_HOURS.has(hour) || (minute === 30 && hour === 15) ||
      scheduled.getUTCMinutes() !== minute || scheduled.getUTCSeconds() !== 0 ||
      scheduled.getUTCMilliseconds() !== 0) {
    return { state: 'CRON_TIME_MISMATCH' };
  }
  const ageMs = nowMs - scheduled.getTime();
  if (ageMs < -60_000 || ageMs > MAX_CLOUDFLARE_DELAY_MS) {
    return { state: 'SKIPPED_DELAYED', delayMs: ageMs };
  }
  return {
    state: 'READY',
    slot: `${minute} ${hour} * * *`,
    scheduledUtc: scheduled.toISOString().replace('.000Z', 'Z'),
    kind: hour === 15 ? 'RESERVED' : minute === 0 ? 'PRIMARY' : 'FALLBACK',
  };
}

export default {
  async fetch() {
    // No public HTTP entrypoint exists for invoking the scheduler.
    return new Response('Not found', { status: 404 });
  },
  async scheduled(controller, env) {
    const item = resolveScheduledSlot(controller.cron, controller.scheduledTime);
    console.log('SCHEDULER_EVALUATION', JSON.stringify(item));
    if (item.state !== 'READY') return;
    if (env.DISPATCH_ENABLED !== 'true') {
      console.log('DRY_RUN_NO_GITHUB_DISPATCH', item.slot);
      return;
    }
    if (typeof env.GITHUB_TOKEN !== 'string' || env.GITHUB_TOKEN.length < 10) {
      throw new Error('MISSING_GITHUB_TOKEN');
    }
    const endpoint = 'https://api.github.com/repos/CoderPL1005/portfolio/actions/workflows/job-discovery-daily.yml/dispatches';
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${env.GITHUB_TOKEN}`,
        'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'portfolio-daily-cloudflare-worker',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        ref: 'main',
        inputs: {
          mode: 'external_scheduled',
          slot: item.slot,
          scheduled_utc: item.scheduledUtc,
        },
      }),
      signal: AbortSignal.timeout(15_000),
    });
    console.log('GITHUB_DISPATCH_HTTP_STATUS', response.status);
    if (!response.ok) throw new Error(`GITHUB_DISPATCH_FAILED_HTTP_${response.status}`);
    console.log('GITHUB_DISPATCH_ACCEPTED', item.slot, item.scheduledUtc);
    // Never retry this POST on timeout / unknown result. GitHub run and backend have idempotency gates.
  },
};
