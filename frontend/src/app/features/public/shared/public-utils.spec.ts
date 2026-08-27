import { describe, expect, it } from 'vitest';
import { safeHttpUrl, safeSocialUrl } from './public-utils';
describe('public URL safety', () => {
  it('allows HTTP links and safe mail links', () => { expect(safeHttpUrl('https://example.com')).toBe('https://example.com'); expect(safeSocialUrl('mailto:a@example.com')).toBe('mailto:a@example.com'); });
  it('rejects executable and data URLs', () => { expect(safeHttpUrl('javascript:alert(1)')).toBeNull(); expect(safeSocialUrl('data:text/html,x')).toBeNull(); });
});
