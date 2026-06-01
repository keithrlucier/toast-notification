import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';

interface TenantMfaPolicy {
  requireMfa: boolean;
  callerEnrolled: boolean;
}

/**
 * Admin-only: require MFA for everyone in the workspace. When on, every member must
 * enroll an authenticator and sending a toast / changing the lock screen require a
 * fresh step-up verification. The toggle can't be turned on until the admin flipping
 * it has their OWN authenticator enrolled (self-lockout guard, enforced server-side).
 *
 * `selfEnrolled` is passed from the parent so the gate reflects an enrollment the
 * user just completed in the Two-Factor card without a page reload.
 */
export default function TenantMfaPolicyCard({ selfEnrolled }: { selfEnrolled?: boolean }) {
  const [data, setData]       = useState<TenantMfaPolicy | null>(null);
  const [loading, setLoading] = useState(true);
  const [enabled, setEnabled] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');

  useEffect(() => {
    api.get<TenantMfaPolicy>('/api/tenant/mfa-policy')
      .then(d => { setData(d); setEnabled(d.requireMfa); })
      .catch(() => { /* admins only — hide for non-admins */ })
      .finally(() => setLoading(false));
  }, []);

  if (loading || !data) return null;

  // Reflect a just-completed self-enrollment without a reload.
  const callerEnrolled = data.callerEnrolled || Boolean(selfEnrolled);
  // Only block turning it ON when not enrolled; turning OFF is always allowed.
  const blockedOn = enabled && !data.requireMfa && !callerEnrolled;

  const save = async () => {
    setError(''); setSuccess('');
    if (blockedOn) {
      setError('Set up your own authenticator above before requiring MFA for the workspace.');
      return;
    }
    setSaving(true);
    try {
      await api.put('/api/tenant/mfa-policy', { requireMfa: enabled });
      setData(d => (d ? { ...d, requireMfa: enabled } : d));
      setSuccess('Saved.');
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save MFA policy.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600 }}>Require MFA for the workspace</h2>
        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
          <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{enabled ? 'On' : 'Off'}</span>
          <input
            type="checkbox"
            checked={enabled}
            onChange={e => setEnabled(e.target.checked)}
            style={{ width: 16, height: 16, cursor: 'pointer' }}
          />
        </label>
      </div>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16, lineHeight: 1.5, maxWidth: 640 }}>
        When on, everyone in this workspace must set up an authenticator, and sending a notification or
        changing the lock screen requires a fresh MFA check. This is separate from Microsoft SSO.
      </p>

      {blockedOn && (
        <div style={{
          background: 'var(--bg-tertiary)', border: '1px solid rgba(148,148,160,0.25)',
          borderRadius: 'var(--radius-sm)', padding: '10px 14px', fontSize: 13,
          color: 'var(--status-warning)', marginBottom: 16,
        }}>
          Set up your own authenticator (Two-Factor Authentication, above) before you can require it for
          everyone — otherwise you’d lock yourself out of sending and lock-screen changes.
        </div>
      )}

      {error   && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}
      {success && (
        <div style={{
          background: 'rgba(74,222,128,0.1)', border: '1px solid rgba(74,222,128,0.3)',
          borderRadius: 'var(--radius-sm)', padding: '10px 14px',
          color: 'var(--status-success)', fontSize: 14, marginBottom: 16,
        }}>{success}</div>
      )}

      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <button className="btn btn-primary" onClick={save} disabled={saving || blockedOn}>
          {saving ? 'Saving...' : 'Save'}
        </button>
      </div>
    </div>
  );
}
