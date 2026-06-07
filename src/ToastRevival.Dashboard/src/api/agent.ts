import { api } from './client';

export interface AgentVersionInfo {
  version: string;
  msiDownloadUrl: string;
}

export const agentApi = {
  // GET /api/agent/version — the latest released agent version the fleet should be
  // on (Agent:LatestVersion, env-overridden in prod). Anonymous endpoint; the
  // dashboard reads it to show the fleet target and how many devices are current.
  version: () => api.get<AgentVersionInfo>('/api/agent/version'),
};

// Normalize a reported agent version ("0.4.44.0") and a feed target ("0.4.44")
// to a comparable numeric tuple. Missing/garbage segments sort as 0.
function parseVersion(v: string | null | undefined): number[] {
  if (!v) return [];
  return v.trim().split('.').map(seg => {
    const n = parseInt(seg, 10);
    return Number.isNaN(n) ? 0 : n;
  });
}

// True when `reported` is the same or newer than `target` across the segments
// that `target` defines (target is 3-part, devices report 4-part — we only
// compare as far as the target specifies so the trailing ".0" never counts as behind).
export function isUpToDate(reported: string | null | undefined, target: string | null | undefined): boolean {
  const t = parseVersion(target);
  if (t.length === 0) return false;
  const r = parseVersion(reported);
  if (r.length === 0) return false;
  for (let i = 0; i < t.length; i++) {
    const rv = r[i] ?? 0;
    if (rv > t[i]) return true;
    if (rv < t[i]) return false;
  }
  return true; // equal across every target segment
}
