import { describe, it, expect } from 'vitest';
import { getModerationStatus } from './assets';

describe('getModerationStatus', () => {
  it('maps each known decision', () => {
    expect(getModerationStatus(JSON.stringify({ decision: 'Pass' }))).toBe('Pass');
    expect(getModerationStatus(JSON.stringify({ decision: 'Review' }))).toBe('Review');
    expect(getModerationStatus(JSON.stringify({ decision: 'Block' }))).toBe('Block');
  });

  it('returns Unknown for missing input', () => {
    expect(getModerationStatus(undefined)).toBe('Unknown');
    expect(getModerationStatus('')).toBe('Unknown');
  });

  it('returns Unknown for malformed JSON (never throws)', () => {
    expect(getModerationStatus('{not json')).toBe('Unknown');
  });

  it('returns Unknown for an unrecognized or absent decision value', () => {
    expect(getModerationStatus(JSON.stringify({ decision: 'pass' }))).toBe('Unknown'); // case-sensitive by contract
    expect(getModerationStatus(JSON.stringify({ decision: 'Quarantine' }))).toBe('Unknown');
    expect(getModerationStatus(JSON.stringify({ other: 'x' }))).toBe('Unknown');
  });
});
