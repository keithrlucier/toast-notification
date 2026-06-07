import { describe, it, expect } from 'vitest';
import { isUpToDate } from './agent';

describe('isUpToDate', () => {
  it('treats a 4-part report equal to the 3-part target as current', () => {
    // Devices report "0.4.44.0"; the feed target is "0.4.44".
    expect(isUpToDate('0.4.44.0', '0.4.44')).toBe(true);
  });

  it('treats an exact match as current', () => {
    expect(isUpToDate('0.4.44', '0.4.44')).toBe(true);
  });

  it('flags an older patch as behind', () => {
    expect(isUpToDate('0.4.42.0', '0.4.44')).toBe(false);
  });

  it('flags an older minor as behind', () => {
    expect(isUpToDate('0.3.99.0', '0.4.44')).toBe(false);
  });

  it('treats a newer build as current (never "behind" on a higher version)', () => {
    expect(isUpToDate('0.4.45.0', '0.4.44')).toBe(true);
  });

  it('is not fooled by lexical comparison (10 > 9)', () => {
    expect(isUpToDate('0.4.10.0', '0.4.9')).toBe(true);
    expect(isUpToDate('0.4.9.0', '0.4.10')).toBe(false);
  });

  it('returns false for unknown/empty reported version', () => {
    expect(isUpToDate('Unknown', '0.4.44')).toBe(false);
    expect(isUpToDate('', '0.4.44')).toBe(false);
    expect(isUpToDate(null, '0.4.44')).toBe(false);
  });

  it('returns false when target is missing (feed unreachable)', () => {
    expect(isUpToDate('0.4.44.0', null)).toBe(false);
    expect(isUpToDate('0.4.44.0', '')).toBe(false);
  });

  it('treats non-numeric garbage segments as zero rather than throwing', () => {
    expect(isUpToDate('0.4.x', '0.4.0')).toBe(true);
    expect(isUpToDate('0.4.x', '0.4.1')).toBe(false);
  });
});
