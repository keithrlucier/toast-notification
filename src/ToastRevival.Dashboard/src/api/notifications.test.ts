import { describe, it, expect } from 'vitest';
import { parseButtons } from './notifications';

describe('parseButtons', () => {
  it('parses a valid action-button JSON array', () => {
    const json = JSON.stringify([
      { label: 'Open', actionId: 'open', type: 'Action' },
      { label: 'Docs', actionId: 'docs', type: 'Url', url: 'https://example.com' },
    ]);
    const result = parseButtons(json);
    expect(result).toHaveLength(2);
    expect(result?.[0].label).toBe('Open');
    expect(result?.[1].url).toBe('https://example.com');
  });

  it('returns an empty array for "[]"', () => {
    expect(parseButtons('[]')).toEqual([]);
  });

  it('returns undefined for null / undefined / empty string', () => {
    expect(parseButtons(null)).toBeUndefined();
    expect(parseButtons(undefined)).toBeUndefined();
    expect(parseButtons('')).toBeUndefined();
  });

  it('swallows malformed JSON and returns undefined (never throws)', () => {
    expect(parseButtons('{not json')).toBeUndefined();
    expect(parseButtons('[{"label":')).toBeUndefined();
  });
});
