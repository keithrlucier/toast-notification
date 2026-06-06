import { api } from './client';

interface TenantSettingsLite {
  enrollmentKey: string | null;
}

let _enrollmentKeyCache: Promise<string | null> | null = null;

/**
 * Returns this tenant's enrollment key (or null if unset/unreadable), cached for
 * the page lifetime. Shared by every deployment surface that pre-fills an install
 * command — DeployCommand, DeploymentScripts, and IntuneDeployment — so the
 * /api/tenant/settings call happens once, not once per component.
 */
export function getEnrollmentKey(): Promise<string | null> {
  if (!_enrollmentKeyCache) {
    _enrollmentKeyCache = api.get<TenantSettingsLite>('/api/tenant/settings')
      .then(res => res.enrollmentKey ?? null)
      .catch(() => null);
  }
  return _enrollmentKeyCache;
}
