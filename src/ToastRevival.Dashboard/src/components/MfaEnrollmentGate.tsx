import { useEffect, useState } from 'react';
import { authApi } from '../api/auth';
import { useAuth } from '../contexts/AuthContext';
import TwoFactorCard from './TwoFactorCard';

/**
 * Blocking enrollment gate. When the tenant enforces MFA and the signed-in user
 * hasn't enrolled an authenticator yet, this covers the app and requires setup
 * before anything else — that's what makes the workspace policy actually binding
 * (the action gates 403 anyway, but this is the proactive path). Platform admins
 * are exempt. Clears the moment enrollment is confirmed (no reload).
 */
export default function MfaEnrollmentGate() {
  const { user } = useAuth();
  const [blocked, setBlocked] = useState(false);

  useEffect(() => {
    if (!user || user.isPlatformAdmin) { setBlocked(false); return; }
    let cancelled = false;
    authApi.mfaStatus()
      .then(s => { if (!cancelled) setBlocked(s.tenantRequired && !s.enabled); })
      .catch(() => { if (!cancelled) setBlocked(false); });
    return () => { cancelled = true; };
  }, [user]);

  if (!blocked) return null;

  return (
    <div
      style={{
        position: 'fixed', inset: 0, zIndex: 1000,
        background: 'var(--bg-primary)',
        display: 'flex', alignItems: 'flex-start', justifyContent: 'center',
        overflowY: 'auto', padding: '48px 16px',
      }}
    >
      <div style={{ width: '100%', maxWidth: 640 }}>
        <h1 style={{ fontSize: 22, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 8 }}>
          Set up multi-factor authentication
        </h1>
        <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 24, lineHeight: 1.6 }}>
          Your workspace requires MFA. Add an authenticator app to continue — it only takes a minute,
          and you’ll use it to sign in and to confirm sensitive actions.
        </p>
        <TwoFactorCard onStatusChange={enabled => { if (enabled) setBlocked(false); }} />
      </div>
    </div>
  );
}
