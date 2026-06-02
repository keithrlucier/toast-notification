import { api } from './client';

// XT-1 — per-device single-use enrollment tokens. The issued token is pasted
// into the MSI deploy command's ENROLLMENTKEY=... slot in place of the reusable
// per-tenant enrollment key.

export type EnrollmentTokenStatus = 'active' | 'used' | 'expired' | 'revoked';

export interface EnrollmentToken {
  id: string;
  label: string | null;
  status: EnrollmentTokenStatus;
  createdAt: string;
  expiresAt: string;
  usedAt: string | null;
  usedByDeviceName: string | null;
  usedByUsername: string | null;
  revokedAt: string | null;
}

// Returned ONCE at issue time — the plaintext token never comes back from the list.
export interface IssuedEnrollmentToken {
  id: string;
  token: string;
  expiresAt: string;
  label: string | null;
}

export interface IssueEnrollmentTokenRequest {
  label?: string | null;
  ttlHours?: number | null;
}

export const enrollmentTokensApi = {
  list: () => api.get<EnrollmentToken[]>('/api/devices/enrollment-tokens'),
  issue: (req: IssueEnrollmentTokenRequest) =>
    api.post<IssuedEnrollmentToken>('/api/devices/enrollment-tokens', req),
  revoke: (id: string) => api.delete(`/api/devices/enrollment-tokens/${id}`),
};
