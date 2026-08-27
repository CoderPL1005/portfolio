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

function safeUrl(value: string | null | undefined, protocols: string[]): string | null {
  if (!value || /[\u0000-\u001F\u007F]/.test(value)) return null;
  try {
    const parsed = new URL(value);
    return protocols.includes(parsed.protocol.toLowerCase()) ? value : null;
  } catch {
    return null;
  }
}
