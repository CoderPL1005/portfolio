// Cloudflare Worker: scheduled-only GitHub Actions dispatcher.
// This Worker NEVER calls Render, Neon, SocialAPIs or Gemini directly.
export const PRIMARY_CRON = '0 0,3,6,9,12,15 * * *';
export const FALLBACK_CRON = '30 0,3,6,9,12 * * *';
const ALLOWED_HOURS = new Set([0, 3, 6, 9, 12, 15]);
const MAX_CLOUDFLARE_DELAY_MS = 20 * 60_000;

/** Maps Cloudflare's grouped cron to the already-authorized backend slot expression. */
export function resolveScheduledSlot(cron, scheduledTime, nowMs = Date.now()) {
  const minute = cron === PRIMARY_CRON ? 0 : cron === FALLBACK_CRON ? 30 : null;
  const scheduled = new Date(scheduledTime);
  const validTime = Number.isFinite(scheduled.getTime());
  const actualMinute = validTime ? scheduled.getUTCMinutes() : null;
  if (minute === null) {
    return {
      state: 'UNRECOGNIZED_CRON',
      reason: 'UNRECOGNIZED_CRON_EXPRESSION',
      expectedMinute: null,
      actualMinute,
    };
  }
  if (!validTime) {
    return {
      state: 'INVALID_SCHEDULED_TIME',
      reason: 'INVALID_SCHEDULED_TIME_VALUE',
      expectedMinute: minute,
      actualMinute: null,
    };
  }
  const hour = scheduled.getUTCHours();
  if (!ALLOWED_HOURS.has(hour)) {
    return {
      state: 'CRON_TIME_MISMATCH',
      reason: 'UNAUTHORIZED_UTC_HOUR',
      expectedMinute: minute,
      actualMinute,
      actualHour: hour,
    };
  }
  if (minute === 30 && hour === 15) {
    return {
      state: 'CRON_TIME_MISMATCH',
      reason: 'RESERVED_SLOT_HAS_NO_FALLBACK',
      expectedMinute: minute,
      actualMinute,
      actualHour: hour,
    };
  }
  if (actualMinute !== minute) {
    return {
      state: 'CRON_TIME_MISMATCH',
      reason: 'SCHEDULED_MINUTE_MISMATCH',
      expectedMinute: minute,
      actualMinute,
      actualHour: hour,
    };
  }
  const ageMs = nowMs - scheduled.getTime();
  if (ageMs < -60_000 || ageMs > MAX_CLOUDFLARE_DELAY_MS) {
    return {
      state: 'SKIPPED_DELAYED',
      reason: ageMs < -60_000 ? 'FUTURE_SCHEDULED_TIME' : 'EXPIRED_SCHEDULED_TIME',
      delayMs: ageMs,
      expectedMinute: minute,
      actualMinute,
    };
  }
  const canonical = new Date(Date.UTC(
    scheduled.getUTCFullYear(),
    scheduled.getUTCMonth(),
    scheduled.getUTCDate(),
    hour,
    minute,
    0,
    0
  ));
  return {
    state: 'READY',
    slot: `${minute} ${hour} * * *`,
    scheduledUtc: canonical.toISOString().replace('.000Z', 'Z'),
    kind: hour === 15 ? 'RESERVED' : minute === 0 ? 'PRIMARY' : 'FALLBACK',
  };
}

/** Builds safe, secret-free diagnostic metadata for Cloudflare Observability logs. */
export function buildScheduleDiagnostics(cron, scheduledTime, nowMs = Date.now(), evaluation = resolveScheduledSlot(cron, scheduledTime, nowMs)) {
  const expectedCronMinute = cron === PRIMARY_CRON ? 0 : cron === FALLBACK_CRON ? 30 : null;
  const parsed = new Date(scheduledTime);
  const validParsed = Number.isFinite(parsed.getTime());
  return {
    cron: cron ?? null,
    rawScheduledTime: scheduledTime ?? null,
    parsedScheduledUtc: validParsed ? parsed.toISOString() : null,
    actualInvocationUtc: new Date(nowMs).toISOString(),
    expectedCronMinute,
    actualParsedMinute: validParsed ? parsed.getUTCMinutes() : null,
    canonicalScheduledUtc: evaluation.scheduledUtc ?? null,
    state: evaluation.state,
    rejectionReason: evaluation.state === 'READY' ? null : (evaluation.reason ?? evaluation.state),
  };
}

export default {
  async fetch() {
    // No public HTTP entrypoint exists for invoking the scheduler.
    return new Response('Not found', { status: 404 });
  },
  async scheduled(controller, env) {
    const nowMs = Date.now();
    const item = resolveScheduledSlot(controller.cron, controller.scheduledTime, nowMs);
    const diagnostics = buildScheduleDiagnostics(controller.cron, controller.scheduledTime, nowMs, item);
    console.log('SCHEDULER_DIAGNOSTICS', JSON.stringify(diagnostics));
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
