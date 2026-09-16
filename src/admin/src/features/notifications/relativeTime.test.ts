import { formatRelativeTime } from './relativeTime';

describe('formatRelativeTime', () => {
  const now = new Date('2026-09-16T12:00:00Z');
  const ago = (ms: number) => new Date(now.getTime() - ms).toISOString();

  it('labels recent, minute, hour and day ranges', () => {
    expect(formatRelativeTime(ago(10_000), now)).toBe('just now');
    expect(formatRelativeTime(ago(5 * 60_000), now)).toBe('5 min ago');
    expect(formatRelativeTime(ago(60 * 60_000), now)).toBe('1 hr ago');
    expect(formatRelativeTime(ago(3 * 3_600_000), now)).toBe('3 hr ago');
    expect(formatRelativeTime(ago(24 * 3_600_000), now)).toBe('yesterday');
    expect(formatRelativeTime(ago(3 * 86_400_000), now)).toBe('3 days ago');
  });

  it('falls back to a calendar date after a week', () => {
    const iso = ago(10 * 86_400_000);
    expect(formatRelativeTime(iso, now)).toBe(new Date(iso).toLocaleDateString());
  });

  it('returns an empty string for an unparseable value', () => {
    expect(formatRelativeTime('not-a-date', now)).toBe('');
  });
});
