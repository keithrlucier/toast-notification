import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError, apiErrorFromResponse, authHeaders } from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import { notifyTenantBrandingUpdated, tenantLogoUrlForBrowser } from '../lib/tenantBranding';
import DeviceAppearanceCards from '../components/DeviceAppearanceCards';
import SsoSettingsCard from '../components/SsoSettingsCard';
import TwoFactorCard from '../components/TwoFactorCard';
import TenantMfaPolicyCard from '../components/TenantMfaPolicyCard';

interface TenantSettingsData {
  tenantName: string;
  logoUrl: string | null;
  primaryColor: string | null;
  defaultAudioSetting: string | null;
  defaultScenario: string;
  rateLimitPerMinute: number;
  rateLimitPerHour: number;
  rateLimitPerDay: number;
}

const AUDIO_OPTIONS = [
  { value: '',                                              label: 'System default' },
  { value: 'ms-winsoundevent:Notification.Default',        label: 'Notification sound' },
  { value: 'ms-winsoundevent:Notification.Looping.Alarm',  label: 'Alarm (looping)' },
  { value: 'ms-winsoundevent:Notification.Reminder',       label: 'Reminder sound' },
  { value: 'ms-winsoundevent:Notification.IM',             label: 'Instant message' },
  { value: 'silent',                                        label: 'Silent (no sound)' },
];

const SCENARIO_OPTIONS = [
  { value: 'Default',     label: 'Default' },
  { value: 'Urgent',      label: 'Urgent - breaks through Do Not Disturb' },
  { value: 'Reminder',    label: 'Reminder' },
  { value: 'Alarm',       label: 'Alarm' },
  { value: 'IncomingCall', label: 'Incoming Call' },
];

type LogoPreviewState = 'idle' | 'loading' | 'loaded' | 'error';

export default function TenantSettings() {
  useAuth();
  // Tracks the caller's own authenticator state so the workspace-MFA policy card
  // reflects an enrollment just completed in the Two-Factor card (no reload needed).
  const [selfMfaEnabled, setSelfMfaEnabled] = useState<boolean | undefined>(undefined);
  const [data, setData]       = useState<TenantSettingsData | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');
  // Controlled form state
  const [tenantName, setTenantName]         = useState('');
  const [logoUrl, setLogoUrl]               = useState('');
  const [uploading, setUploading]           = useState(false);
  const [logoPreviewState, setLogoPreviewState] = useState<LogoPreviewState>('idle');
  const [logoDimensions, setLogoDimensions] = useState('');
  const [primaryColor, setPrimaryColor]     = useState('#1F6FBD');
  const [defaultAudio, setDefaultAudio]     = useState('');
  const [defaultScenario, setDefaultScenario] = useState('Default');
  const logoPreviewUrl = tenantLogoUrlForBrowser(logoUrl);

  useEffect(() => {
    api.get<TenantSettingsData>('/api/tenant/settings')
      .then(s => {
        setData(s);
        setTenantName(s.tenantName ?? '');
        setLogoUrl(s.logoUrl ?? '');
        setPrimaryColor(s.primaryColor ?? '#1F6FBD');
        setDefaultAudio(s.defaultAudioSetting ?? '');
        setDefaultScenario(s.defaultScenario ?? 'Default');
        setLoading(false);
      })
      .catch(err => {
        setError(err instanceof ApiError ? err.message : 'Failed to load settings.');
        setLoading(false);
      });
  }, []);

  useEffect(() => {
    setLogoPreviewState(logoPreviewUrl ? 'loading' : 'idle');
    setLogoDimensions('');
  }, [logoPreviewUrl]);

  const save = async () => {
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      await api.put('/api/tenant/settings', {
        tenantName:          tenantName.trim() || undefined,
        logoUrl:             logoUrl.trim() || null,
        primaryColor:        primaryColor.trim() || null,
        defaultAudioSetting: defaultAudio || null,
        defaultScenario,
      });
      setData(current => current
        ? { ...current, tenantName: tenantName.trim() || current.tenantName, logoUrl: logoUrl.trim() || null }
        : current);
      notifyTenantBrandingUpdated();
      setSuccess('Settings saved.');
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save settings.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: 200 }}>
        <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Tenant Settings</h1>
          <p className="subtitle">Branding and notification defaults for {data?.tenantName}</p>
        </div>
      </div>

      {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

      {success && (
        <div style={{
          background: 'rgba(74,222,128,0.1)',
          border: '1px solid rgba(74,222,128,0.3)',
          borderRadius: 'var(--radius-sm)',
          padding: '10px 14px',
          color: 'var(--status-success)',
          fontSize: 14,
          marginBottom: 16,
        }}>
          {success}
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, marginBottom: 24 }}>
        {/* Branding */}
        <div className="card">
          <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 20 }}>Branding</h2>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="field">
              <label>Display Name</label>
              <input
                type="text"
                value={tenantName}
                onChange={e => setTenantName(e.target.value)}
                placeholder="Your company name"
              />
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block' }}>
                Shown as the notification attribution on endpoints
              </span>
            </div>

            <div className="field">
              <label>Logo</label>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                {logoPreviewUrl && logoPreviewState !== 'error' && (
                  <img
                    src={logoPreviewUrl}
                    alt="Tenant logo preview"
                    style={{ width: 72, height: 72, objectFit: 'contain', border: '1px solid rgba(15,23,42,0.14)', borderRadius: 4, padding: 8, background: 'var(--bg-tertiary)' }}
                    onLoad={e => {
                      const img = e.currentTarget;
                      setLogoDimensions(`${img.naturalWidth} x ${img.naturalHeight} px`);
                      setLogoPreviewState('loaded');
                    }}
                    onError={() => {
                      setLogoDimensions('');
                      setLogoPreviewState('error');
                    }}
                  />
                )}
                {logoPreviewUrl && logoPreviewState === 'error' && (
                  <span style={{
                    width: 72,
                    height: 72,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    textAlign: 'center',
                    fontSize: 10,
                    lineHeight: 1.25,
                    color: 'var(--status-error)',
                    border: '1px solid rgba(15,23,42,0.14)',
                    borderRadius: 4,
                    padding: 8,
                    background: 'var(--bg-tertiary)',
                  }}>
                    Preview unavailable
                  </span>
                )}
                <label
                  style={{
                    cursor: uploading ? 'default' : 'pointer',
                    opacity: uploading ? 0.6 : 1,
                  }}
                >
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.gif,.webp"
                    style={{ display: 'none' }}
                    disabled={uploading}
                    onChange={async e => {
                      const file = e.target.files?.[0];
                      if (!file) return;
                      setUploading(true);
                      setError('');
                      try {
                        const form = new FormData();
                        form.append('file', file);
                        const res = await fetch('/api/tenant/logo', {
                          method: 'POST',
                          headers: authHeaders(),
                          body: form,
                        });
                        if (!res.ok) throw await apiErrorFromResponse(res, '/api/tenant/logo', 'Upload failed');
                        const { url } = await res.json() as { url?: string };
                        if (!url) throw new Error('Upload failed.');
                        setLogoPreviewState('loading');
                        setLogoDimensions('');
                        setLogoUrl(url);
                        notifyTenantBrandingUpdated();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Upload failed.');
                      } finally {
                        setUploading(false);
                        e.target.value = '';
                      }
                    }}
                  />
                  <span className="btn btn-secondary" style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, pointerEvents: 'none' }}>
                    {uploading ? 'Uploading...' : logoUrl ? 'Replace' : 'Upload logo'}
                  </span>
                </label>
                {logoUrl && (
                  <button
                    className="btn btn-ghost"
                    style={{ fontSize: 12, padding: '6px 10px', minHeight: 0 }}
                    onClick={() => setLogoUrl('')}
                  >
                    Remove
                  </button>
                )}
              </div>
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 6, display: 'block' }}>
                Recommended dimensions: square 48 x 48 px or 64 x 64 px.
              </span>
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 2, display: 'block' }}>
                PNG, JPG, GIF, or WebP. Max 2 MB. Used in the sidebar and toast attribution.
              </span>
              {logoPreviewUrl && (
                <span style={{ fontSize: 11, color: logoPreviewState === 'error' ? 'var(--status-error)' : 'var(--text-dim)', marginTop: 2, display: 'block' }}>
                  {logoPreviewState === 'error'
                    ? 'The stored logo URL could not be loaded in the browser.'
                    : logoDimensions
                      ? `Current image: ${logoDimensions}`
                      : 'Checking image dimensions...'}
                </span>
              )}
            </div>

            <div className="field">
              <label>Primary Color</label>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                <input
                  type="color"
                  value={primaryColor}
                  onChange={e => setPrimaryColor(e.target.value)}
                  style={{
                    width: 40,
                    height: 38,
                    padding: 2,
                    border: '1px solid rgba(15,23,42,0.12)',
                    borderRadius: 'var(--radius-sm)',
                    background: 'var(--bg-tertiary)',
                    cursor: 'pointer',
                    flexShrink: 0,
                  }}
                />
                <input
                  type="text"
                  value={primaryColor}
                  onChange={e => setPrimaryColor(e.target.value)}
                  placeholder="#1F6FBD"
                  style={{ fontFamily: 'var(--font-mono)', fontSize: 13 }}
                />
              </div>
            </div>

          </div>
        </div>

        {/* Notification Defaults */}
        <div className="card">
          <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 20 }}>Notification Defaults</h2>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="field">
              <label>Default Audio</label>
              <select value={defaultAudio} onChange={e => setDefaultAudio(e.target.value)}>
                {AUDIO_OPTIONS.map(opt => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>

            <div className="field">
              <label>Default Scenario</label>
              <select value={defaultScenario} onChange={e => setDefaultScenario(e.target.value)}>
                {SCENARIO_OPTIONS.map(opt => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>

            <p style={{ fontSize: 12, color: 'var(--text-dim)', lineHeight: 1.5 }}>
              These defaults apply to new notifications when no override is specified.
              Individual notifications can still override these values.
            </p>
          </div>
        </div>
      </div>

      {/* Rate Limits — platform defaults, read-only */}
      <div className="card" style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <h2 style={{ fontSize: 16, fontWeight: 600 }}>Rate Limits</h2>
          <span style={{
            fontSize: 11,
            fontWeight: 600,
            color: 'var(--text-dim)',
            background: 'var(--bg-tertiary)',
            padding: '3px 8px',
            borderRadius: 4,
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
          }}>
            Platform defaults
          </span>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16, marginBottom: 12 }}>
          <div className="metric-card">
            <div className="metric-label">Per Minute</div>
            <div className="metric-value">{data?.rateLimitPerMinute ?? 60}</div>
            <div className="metric-sub">requests</div>
          </div>
          <div className="metric-card">
            <div className="metric-label">Per Hour</div>
            <div className="metric-value">{data?.rateLimitPerHour ?? 500}</div>
            <div className="metric-sub">requests</div>
          </div>
          <div className="metric-card">
            <div className="metric-label">Per Day</div>
            <div className="metric-value">{(data?.rateLimitPerDay ?? 5000).toLocaleString()}</div>
            <div className="metric-sub">requests</div>
          </div>
        </div>
        <p style={{ fontSize: 12, color: 'var(--text-dim)' }}>
          Per-tenant rate limit customization is handled by support on the standard plan.
        </p>
      </div>


      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 32 }}>
        <button
          className="btn btn-primary"
          onClick={save}
          disabled={saving}
        >
          {saving ? 'Saving...' : 'Save Changes'}
        </button>
      </div>

      {/* Content Moderation pointer — full editor lives on /moderation → Settings */}
      <div className="card" style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 8 }}>
          <h2 style={{ fontSize: 16, fontWeight: 600 }}>Content Moderation</h2>
          <Link to="/moderation" style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
            Open Moderation →
          </Link>
        </div>
        <p style={{ fontSize: 13, color: 'var(--text-dim)', lineHeight: 1.6, marginBottom: 0, maxWidth: 720 }}>
          Per-tenant moderation policy (scanning toggles, severity thresholds, blocked-content
          message, bring-your-own Azure Content Safety credentials, and require-admin-approval
          override) is configured on the{' '}
          <Link to="/moderation" style={{ color: 'var(--text-secondary)' }}>Moderation</Link>{' '}
          page under the <strong>Settings</strong> tab. Banned terms are managed on the{' '}
          <strong>Blocklist</strong> tab. See the{' '}
          <Link to="/docs/moderation" style={{ color: 'var(--text-secondary)' }}>moderation guide</Link>{' '}
          for the full reference.
        </p>
      </div>

      {/* M14 Microsoft SSO — per-tenant directory mapping + opt-in */}
      {/* Security — native authenticator MFA (all users) + workspace enforcement (admins) */}
      <TwoFactorCard onStatusChange={setSelfMfaEnabled} />
      <TenantMfaPolicyCard selfEnrolled={selfMfaEnabled} />

      <SsoSettingsCard />

      {/* M12 Device Appearance — Desktop Overlay + Lock Screen Branding (two cards) */}
      <DeviceAppearanceCards />
    </div>
  );
}
