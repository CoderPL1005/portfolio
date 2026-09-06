export function safeHttpUrl(value: string | null | undefined): string | null {
  return safeUrl(value, ['http:', 'https:']);
}

export function safeSocialUrl(value: string | null | undefined): string | null {
  return safeUrl(value, ['http:', 'https:', 'mailto:']);
}

export function isExternalUrl(value: string): boolean {
  return value.startsWith('http://') || value.startsWith('https://');
}

export function formatPortfolioDate(value: string | null | undefined): string | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const [year, month, day] = value.split('-').map(Number);
  return new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short' })
    .format(new Date(Date.UTC(year, month - 1, day)));
}

export function formatPortfolioExactDate(value: string | null | undefined): string | null {
  const timestamp = portfolioDateValue(value);
  if (timestamp === null) return null;
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC',
  }).format(new Date(timestamp));
}

export function portfolioDurationDays(
  start: string | null | undefined,
  end: string | null | undefined,
): number | null {
  const startValue = portfolioDateValue(start);
  const endValue = portfolioDateValue(end);
  if (startValue === null || endValue === null || endValue < startValue) return null;
  return (endValue - startValue) / 86_400_000;
}

function portfolioDateValue(value: string | null | undefined): number | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const [year, month, day] = value.split('-').map(Number);
  const timestamp = Date.UTC(year, month - 1, day);
  const parsed = new Date(timestamp);
  return parsed.getUTCFullYear() === year
    && parsed.getUTCMonth() === month - 1
    && parsed.getUTCDate() === day
      ? timestamp
      : null;
}

function safeUrl(value: string | null | undefined, protocols: string[]): string | null {
  if (!value || /[\u0000-\u001F\u007F]/.test(value)) return null;
  try {
    const parsed = new URL(value);
    return protocols.includes(parsed.protocol.toLowerCase()) ? value : null;
  } catch {
    return null;
  }
}
