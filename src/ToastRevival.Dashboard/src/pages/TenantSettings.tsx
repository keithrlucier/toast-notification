import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';

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
  { value: 'Urgent',      label: 'Urgent — breaks through Do Not Disturb' },
  { value: 'Reminder',    label: 'Reminder' },
  { value: 'Alarm',       label: 'Alarm' },
  { value: 'IncomingCall', label: 'Incoming Call' },
];

export default function TenantSettings() {
  const [data, setData]       = useState<TenantSettingsData | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');

  // Controlled form state
  const [logoUrl, setLogoUrl]               = useState('');
  const [primaryColor, setPrimaryColor]     = useState('#00C9A7');
  const [defaultAudio, setDefaultAudio]     = useState('');
  const [defaultScenario, setDefaultScenario] = useState('Default');

  useEffect(() => {
    api.get<TenantSettingsData>('/api/tenant/settings')
      .then(s => {
        setData(s);
        setLogoUrl(s.logoUrl ?? '');
        setPrimaryColor(s.primaryColor ?? '#00C9A7');
        setDefaultAudio(s.defaultAudioSetting ?? '');
        setDefaultScenario(s.defaultScenario ?? 'Default');
        setLoading(false);
      })
      .catch(err => {
        setError(err instanceof ApiError ? err.message : 'Failed to load settings.');
        setLoading(false);
      });
  }, []);

  const save = async () => {
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      await api.put('/api/tenant/settings', {
        logoUrl:             logoUrl.trim() || null,
        primaryColor:        primaryColor.trim() || null,
        defaultAudioSetting: defaultAudio || null,
        defaultScenario,
      });
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
              <label>Logo URL</label>
              <input
                type="url"
                value={logoUrl}
                onChange={e => setLogoUrl(e.target.value)}
                placeholder="https://example.com/logo.png"
              />
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
                    border: '1px solid rgba(255,255,255,0.08)',
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
                  placeholder="#00C9A7"
                  style={{ fontFamily: 'var(--font-mono)', fontSize: 13 }}
                />
              </div>
            </div>

            {logoUrl.trim() && (
              <div style={{
                padding: 12,
                background: 'var(--bg-tertiary)',
                borderRadius: 'var(--radius-sm)',
                display: 'flex',
                alignItems: 'center',
                gap: 12,
              }}>
                <img
                  src={logoUrl}
                  alt="Logo preview"
                  style={{ height: 32, maxWidth: 120, objectFit: 'contain' }}
                  onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                />
                <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>Logo preview</span>
              </div>
            )}
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
          Per-tenant rate limit customization is available in Pro and Enterprise plans.
        </p>
      </div>

      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <button
          className="btn btn-primary"
          onClick={save}
          disabled={saving}
        >
          {saving ? 'Saving…' : 'Save Changes'}
        </button>
      </div>
    </div>
  );
}
