import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';

interface TenantSsoSettings {
  enabled: boolean;
  azureAdTenantId: string | null;
  requireMfa: boolean;
  platformConfigured: boolean;
  microsoftClientId: string | null;
}

const GUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/**
 * Per-tenant Microsoft SSO card on Tenant Settings. The platform owns the Entra
 * app credentials (set under Billing → Single Sign-On by a platform admin); here
 * a tenant admin maps their own Entra Directory (tenant) ID and flips SSO on.
 * Disabled ≠ hidden — the config stays visible (dimmed) when the toggle is off.
 */
export default function SsoSettingsCard() {
  const [data, setData]         = useState<TenantSsoSettings | null>(null);
  const [loading, setLoading]   = useState(true);
  const [enabled, setEnabled]   = useState(false);
  const [dirId, setDirId]       = useState('');
  const [requireMfa, setRequireMfa] = useState(false);
  const [saving, setSaving]     = useState(false);
  const [error, setError]       = useState('');
  const [success, setSuccess]   = useState('');

  useEffect(() => {
    api.get<TenantSsoSettings>('/api/tenant/sso')
      .then(s => {
        setData(s);
        setEnabled(s.enabled);
        setDirId(s.azureAdTenantId ?? '');
        setRequireMfa(s.requireMfa);
      })
      .catch(() => { /* admins only; ignore for non-admin */ })
      .finally(() => setLoading(false));
  }, []);

  if (loading || !data) return null;

  const trimmedDir   = dirId.trim();
  const dirValid     = GUID_RE.test(trimmedDir);
  const configured   = data.platformConfigured;
  const canConsent   = dirValid && !!data.microsoftClientId;
  const consentUrl   = canConsent
    ? `https://login.microsoftonline.com/${trimmedDir}/adminconsent?client_id=${encodeURIComponent(data.microsoftClientId!)}`
    : '';

  const save = async () => {
    setError(''); setSuccess('');
    if (enabled && !dirValid) {
      setError('Enter a valid Directory (tenant) ID before enabling Microsoft sign-in.');
      return;
    }
    setSaving(true);
    try {
      await api.put('/api/tenant/sso', {
        enabled,
        azureAdTenantId: trimmedDir || null,
        requireMfa,
      });
      setData(d => d ? { ...d, enabled, azureAdTenantId: trimmedDir || null, requireMfa } : d);
      setSuccess('SSO settings saved.');
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save SSO settings.');
    } finally {
      setSaving(false);
    }
  };

  // Body dims to 0.5 / non-interactive when the card is off (Diana's rule:
  // disabled ≠ hidden — the admin still sees their settings).
  const bodyStyle: React.CSSProperties = enabled
    ? {}
    : { opacity: 0.5, pointerEvents: 'none' };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600 }}>Microsoft Single Sign-On</h2>
        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: configured ? 'pointer' : 'not-allowed' }}>
          <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{enabled ? 'On' : 'Off'}</span>
          <input
            type="checkbox"
            checked={enabled}
            disabled={!configured}
            onChange={e => setEnabled(e.target.checked)}
            style={{ width: 16, height: 16, cursor: configured ? 'pointer' : 'not-allowed' }}
          />
        </label>
      </div>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16, lineHeight: 1.5 }}>
        Let your team sign in with their Microsoft work or school account. Users must already
        have an account here — Microsoft becomes an additional way to sign in, not a way to self-register.
      </p>

      {!configured && (
        <div style={{
          background: 'var(--bg-tertiary)',
          border: '1px solid rgba(148,148,160,0.25)',
          borderRadius: 'var(--radius-sm)',
          padding: '10px 14px',
          fontSize: 13,
          color: 'var(--text-secondary)',
          marginBottom: 16,
        }}>
          Microsoft sign-in isn’t set up on this server yet. Contact your platform administrator to enable it.
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

      <div style={bodyStyle}>
        <div className="field" style={{ marginBottom: 16, maxWidth: 420 }}>
          <label>Directory (tenant) ID</label>
          <input
            type="text"
            value={dirId}
            onChange={e => setDirId(e.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
            disabled={!configured}
            style={{ fontFamily: 'var(--font-mono)', fontSize: 13 }}
          />
          <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block' }}>
            Find this in your Microsoft Entra admin center → Overview → Directory (tenant) ID.
          </span>
          {dirId.trim() && !dirValid && (
            <span style={{ fontSize: 11, color: 'var(--status-error)', marginTop: 2, display: 'block' }}>
              That doesn’t look like a valid GUID.
            </span>
          )}
        </div>

        <label style={{ display: 'flex', alignItems: 'flex-start', gap: 8, marginBottom: 16, maxWidth: 480, cursor: configured ? 'pointer' : 'default' }}>
          <input
            type="checkbox"
            checked={requireMfa}
            disabled={!configured}
            onChange={e => setRequireMfa(e.target.checked)}
            style={{ marginTop: 3 }}
          />
          <span style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.45 }}>
            Require Microsoft sign-ins to assert verified MFA
            <span style={{ display: 'block', fontSize: 11, color: 'var(--text-dim)', marginTop: 2 }}>
              Applies only to people signing in <em>with Microsoft</em> — the id_token must prove Entra
              enforced a second factor, or the sign-in is rejected. This is <strong>not</strong> how you turn on
              MFA for the workspace: that lives under Security → Two-Factor Authentication. Leave off to trust
              your organization’s own Conditional Access policy.
            </span>
          </span>
        </label>

        <div style={{
          background: 'var(--bg-tertiary)',
          border: '1px solid rgba(148,148,160,0.2)',
          borderRadius: 'var(--radius-sm)',
          padding: '12px 14px',
          marginBottom: 16,
          maxWidth: 560,
        }}>
          <p style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
            One-time setup
          </p>
          <p style={{ fontSize: 12, color: 'var(--text-dim)', lineHeight: 1.5, marginBottom: 10 }}>
            A Global Administrator in your Microsoft tenant grants consent once, then everyone
            in your organization can sign in silently.
          </p>
          <a
            href={consentUrl || undefined}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-secondary"
            style={{
              fontSize: 12, padding: '6px 14px', minHeight: 0,
              pointerEvents: canConsent ? 'auto' : 'none',
              opacity: canConsent ? 1 : 0.5,
            }}
            aria-disabled={!canConsent}
          >
            Grant admin consent in Microsoft ↗
          </a>
          {!canConsent && (
            <span style={{ fontSize: 11, color: 'var(--text-dim)', marginLeft: 10 }}>
              Enter a valid Directory ID first.
            </span>
          )}
        </div>
      </div>

      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <button className="btn btn-primary" onClick={save} disabled={saving || !configured}>
          {saving ? 'Saving...' : 'Save SSO settings'}
        </button>
      </div>
    </div>
  );
}
