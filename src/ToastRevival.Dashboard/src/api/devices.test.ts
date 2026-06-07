import { describe, it, expect, vi, afterEach } from 'vitest';
import { normalizeDevice, isRecentlyOnline, type DeviceApiResponse } from './devices';

// CR-P1-005: lock the API field-alias translation layer (REST-L6 "safe
// consolidation boundary") and the presence-window heuristic. These are the
// data-shape contracts that silently drive what the device table renders; a
// regression here is invisible until a customer sees a wrong/blank device.

afterEach(() => {
  vi.useRealTimers();
});

describe('normalizeDevice -- API field-alias contract (REST-L6 boundary)', () => {
  it('resolves the alias fields deviceId/deviceName/lastPing onto canonical names', () => {
    const raw: DeviceApiResponse = {
      deviceId: 'dev-1',
      deviceName: 'WS-ALIAS',
      lastPing: '2026-06-01T10:00:00.000Z',
      isOnline: true,
    };
    const d = normalizeDevice(raw);
    expect(d.id).toBe('dev-1');
    expect(d.machineName).toBe('WS-ALIAS');
    expect(d.lastSeen).toBe('2026-06-01T10:00:00.000Z');
  });

  it('prefers the canonical field over its alias when both are present', () => {
    const raw: DeviceApiResponse = {
      id: 'canonical-id',
      deviceId: 'alias-id',
      machineName: 'CANON',
      deviceName: 'ALIAS',
      lastSeen: '2026-06-02T00:00:00.000Z',
      lastPing: '2026-06-01T00:00:00.000Z',
      isOnline: false,
    };
    const d = normalizeDevice(raw);
    expect(d.id).toBe('canonical-id');
    expect(d.machineName).toBe('CANON');
    expect(d.lastSeen).toBe('2026-06-02T00:00:00.000Z');
  });

  it('applies the documented fallbacks for missing/blank fields', () => {
    const d = normalizeDevice({ isOnline: false });
    expect(d.id).toBe('');
    expect(d.tenantId).toBe('');
    expect(d.machineName).toBe('Unknown device');
    expect(d.username).toBe('Unknown user');
    expect(d.osVersion).toBe('Unknown OS');
    expect(d.agentVersion).toBe('Unknown');
    expect(d.lastSeen).toBeNull();
    expect(d.groupIds).toEqual([]);
    expect(d.wanIpAddress).toBeNull();
    expect(d.lanIpAddress).toBeNull();
    expect(d.registeredAt).toBe(new Date(0).toISOString());
  });

  it('trims whitespace and treats blank strings as missing', () => {
    const d = normalizeDevice({ username: '  alice  ', osVersion: '   ', agentVersion: '0.4.42', isOnline: true });
    expect(d.username).toBe('alice');
    expect(d.osVersion).toBe('Unknown OS'); // blank after trim -> fallback
    expect(d.agentVersion).toBe('0.4.42');
  });

  it('honours an explicit isOnline boolean over the lastSeen heuristic', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-06-07T12:00:00.000Z'));
    const recent = new Date(Date.now() - 60_000).toISOString(); // 1 min ago
    expect(normalizeDevice({ isOnline: false, lastSeen: recent }).isOnline).toBe(false);
    expect(normalizeDevice({ isOnline: true }).isOnline).toBe(true);
  });

  it('falls back to the recency heuristic when isOnline is absent', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-06-07T12:00:00.000Z'));
    const recent = new Date(Date.now() - 60_000).toISOString();
    const stale = new Date(Date.now() - 60 * 60 * 1000).toISOString(); // 60 min ago
    expect(normalizeDevice({ status: 'Active', lastSeen: recent }).isOnline).toBe(true);
    expect(normalizeDevice({ status: 'Active', lastSeen: stale }).isOnline).toBe(false);
  });
});

describe('isRecentlyOnline -- 45-minute presence window', () => {
  const NOW = '2026-06-07T12:00:00.000Z';
  const ago = (ms: number) => new Date(new Date(NOW).getTime() - ms).toISOString();

  function freeze() {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(NOW));
  }

  it('returns false for any non-Active status regardless of recency', () => {
    freeze();
    expect(isRecentlyOnline('Inactive', ago(60_000))).toBe(false);
    expect(isRecentlyOnline('Decommissioned', ago(0))).toBe(false);
  });

  it('returns false when lastSeen is null or unparseable', () => {
    freeze();
    expect(isRecentlyOnline('Active', null)).toBe(false);
    expect(isRecentlyOnline(undefined, 'not-a-date')).toBe(false);
  });

  it('treats undefined status as eligible', () => {
    freeze();
    expect(isRecentlyOnline(undefined, ago(60_000))).toBe(true);
  });

  it('is inclusive at exactly 45 minutes and false just beyond', () => {
    freeze();
    expect(isRecentlyOnline('Active', ago(45 * 60 * 1000))).toBe(true);
    expect(isRecentlyOnline('Active', ago(45 * 60 * 1000 + 1))).toBe(false);
  });
});
