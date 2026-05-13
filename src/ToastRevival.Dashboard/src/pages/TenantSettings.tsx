import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

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

interface ModerationSettings {
  enabled: boolean;
  scanText: boolean;
  scanImages: boolean;
  reviewSeverity: number;
  blockSeverity: number;
  requireApprovalAll: boolean;
  customEndpoint: string | null;
  customKeyMasked: string | null;
  blockedMessage: string | null;
  platformEndpointConfigured: boolean;
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

export default function TenantSettings() {
  useAuth();
  const [data, setData]       = useState<TenantSettingsData | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');
  // Controlled form state
  const [tenantName, setTenantName]         = useState('');
  const [logoUrl, setLogoUrl]               = useState('');
  const [uploading, setUploading]           = useState(false);
  const [primaryColor, setPrimaryColor]     = useState('#1F6FBD');
  const [defaultAudio, setDefaultAudio]     = useState('');
  const [defaultScenario, setDefaultScenario] = useState('Default');

  // Moderation card state (M11) — independent of branding/defaults save
  const [mod, setMod] = useState<ModerationSettings | null>(null);
  const [modEnabled, setModEnabled]             = useState(true);
  const [modScanText, setModScanText]           = useState(true);
  const [modScanImages, setModScanImages]       = useState(true);
  const [modReview, setModReview]               = useState(2);
  const [modBlock, setModBlock]                 = useState(5);
  const [modRequireAll, setModRequireAll]       = useState(false);
  const [modEndpoint, setModEndpoint]           = useState('');
  const [modKeyInput, setModKeyInput]           = useState('');     // new raw key to save
  const [modKeyTouched, setModKeyTouched]       = useState(false);  // user typed in the key field
  const [modBlockedMessage, setModBlockedMessage] = useState('');
  const [modSaving, setModSaving]               = useState(false);
  const [modError, setModError]                 = useState('');
  const [modSuccess, setModSuccess]             = useState('');

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

    api.get<ModerationSettings>('/api/tenant/moderation')
      .then(m => {
        setMod(m);
        setModEnabled(m.enabled);
        setModScanText(m.scanText);
        setModScanImages(m.scanImages);
        setModReview(m.reviewSeverity);
        setModBlock(m.blockSeverity);
        setModRequireAll(m.requireApprovalAll);
        setModEndpoint(m.customEndpoint ?? '');
        setModBlockedMessage(m.blockedMessage ?? '');
      })
      .catch(() => { /* card surfaces its own error on save attempt */ });
  }, []);

  const saveModeration = async () => {
    setModSaving(true);
    setModError('');
    setModSuccess('');
    try {
      await api.put('/api/tenant/moderation', {
        enabled: modEnabled,
        scanText: modScanText,
        scanImages: modScanImages,
        reviewSeverity: modReview,
        blockSeverity: modBlock,
        requireApprovalAll: modRequireAll,
        customEndpoint: modEndpoint.trim() || null,
        // Only send a key if the admin actually typed in the field this session.
        // Otherwise null preserves whatever's already stored.
        customKey: modKeyTouched ? (modKeyInput.trim() || '__clear__') : null,
        blockedMessage: modBlockedMessage.trim() || null,
      });
      setModSuccess('Moderation settings saved.');
      setModKeyTouched(false);
      setModKeyInput('');
      // Reload masked key surface
      const fresh = await api.get<ModerationSettings>('/api/tenant/moderation');
      setMod(fresh);
      setTimeout(() => setModSuccess(''), 3000);
    } catch (err) {
      setModError(err instanceof ApiError ? err.message : 'Failed to save moderation settings.');
    } finally {
      setModSaving(false);
    }
  };

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
                {logoUrl && (
                  <img
                    src={logoUrl}
                    alt="Logo"
                    style={{ height: 36, maxWidth: 100, objectFit: 'contain', border: '1px solid rgba(255,255,255,0.08)', borderRadius: 4, padding: 4, background: 'var(--bg-tertiary)' }}
                    onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                  />
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
                          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` },
                          body: form,
                        });
                        if (!res.ok) { const b = await res.json(); throw new Error(b.message ?? 'Upload failed'); }
                        const { url } = await res.json();
                        setLogoUrl(url);
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Upload failed.');
                      } finally {
                        setUploading(false);
                        e.target.value = '';
                      }
                    }}
                  />
                  <span className="btn btn-secondary" style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, pointerEvents: 'none' }}>
                    {uploading ? 'Uploading…' : logoUrl ? 'Replace' : '↑ Upload logo'}
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
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block' }}>
                PNG, JPG or WebP · max 2 MB · appears in notification toasts
              </span>
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

      {/* Content Moderation (M11) — independent save scope from branding/defaults */}
      <div className="card" style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 4 }}>
          <h2 style={{ fontSize: 16, fontWeight: 600 }}>Content Moderation</h2>
          <Link to="/docs/moderation" style={{ fontSize: 12, color: 'var(--text-dim)' }}>
            Read the moderation guide →
          </Link>
        </div>
        <p style={{ fontSize: 13, color: 'var(--text-dim)', marginBottom: 20, maxWidth: 720, lineHeight: 1.5 }}>
          Toast Notification scans every outgoing notification's text and (optional) hero image
          against Azure Content Safety before it ships to endpoints. Tune the policy for your
          tenant below. Severity follows the Azure 0–6 scale — any category score at or above
          the <strong>Review</strong> threshold routes the notification to the{' '}
          <Link to="/moderation" style={{ color: 'var(--text-secondary)' }}>Moderation queue</Link>{' '}
          for admin approval; at or above the <strong>Block</strong> threshold it is rejected outright.
          Tenant-specific banned terms are managed on the same queue page.
        </p>

        {modError && <div className="error-banner" style={{ marginBottom: 16 }}>{modError}</div>}

        {modSuccess && (
          <div style={{
            background: 'rgba(74,222,128,0.1)',
            border: '1px solid rgba(74,222,128,0.3)',
            borderRadius: 'var(--radius-sm)',
            padding: '10px 14px',
            color: 'var(--status-success)',
            fontSize: 14,
            marginBottom: 16,
          }}>
            {modSuccess}
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>

          {/* Toggles */}
          <div>
            <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Scanning
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <CheckboxRow
                checked={modEnabled}
                onChange={setModEnabled}
                label="Moderation enabled"
                help="Master switch. When off, no scanning runs and every notification is treated as Pass. Blocklist terms still apply (managed on the Moderation page)."
              />
              <CheckboxRow
                checked={modScanText}
                onChange={setModScanText}
                label="Scan notification text"
                help="Run title and body lines through Azure Content Safety. Disabling this does not disable the blocklist."
                disabled={!modEnabled}
              />
              <CheckboxRow
                checked={modScanImages}
                onChange={setModScanImages}
                label="Scan hero images"
                help="Run inline hero image URLs through Azure Content Safety image moderation. Asset-library images that were approved on upload are not re-scanned."
                disabled={!modEnabled}
              />
              <CheckboxRow
                checked={modRequireAll}
                onChange={setModRequireAll}
                label="Require admin approval for every notification"
                help="When enabled, every outgoing notification is routed to the Moderation queue regardless of scan results. Use this when your policy requires human-in-the-loop on every send."
              />
            </div>
          </div>

          {/* Thresholds */}
          <div>
            <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Severity thresholds (0–6 scale)
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
              <div className="field">
                <label>Review at or above</label>
                <select
                  value={modReview}
                  onChange={e => setModReview(parseInt(e.target.value, 10))}
                  disabled={!modEnabled}
                >
                  {[0,1,2,3,4,5,6].map(n => (
                    <option key={n} value={n}>{n}</option>
                  ))}
                </select>
                <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                  Scores at or above this level go to the Moderation queue for admin approval.
                  Platform default is <strong>2</strong>.
                </span>
              </div>
              <div className="field">
                <label>Block at or above</label>
                <select
                  value={modBlock}
                  onChange={e => setModBlock(parseInt(e.target.value, 10))}
                  disabled={!modEnabled}
                >
                  {[0,1,2,3,4,5,6].map(n => (
                    <option key={n} value={n}>{n}</option>
                  ))}
                </select>
                <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                  Scores at or above this level are rejected outright with HTTP 422.
                  Platform default is <strong>5</strong>. Must be greater than the Review threshold.
                </span>
              </div>
            </div>
          </div>

          {/* Blocked message */}
          <div className="field">
            <label>Blocked content message</label>
            <input
              type="text"
              value={modBlockedMessage}
              maxLength={500}
              onChange={e => setModBlockedMessage(e.target.value)}
              placeholder="Content blocked by moderation policy."
            />
            <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
              Shown to senders when their notification is rejected. Blocklist hits still surface
              the matched term. Up to 500 characters.
            </span>
          </div>

          {/* Azure credentials */}
          <div>
            <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Azure Content Safety credentials
            </div>
            <p style={{ fontSize: 12, color: 'var(--text-dim)', marginBottom: 12, lineHeight: 1.5 }}>
              {mod?.platformEndpointConfigured
                ? <>Using the platform-default Azure Content Safety resource. Provide your own credentials below to bill scans to your subscription and isolate your tenant's content.</>
                : <>The platform default has no Azure Content Safety key configured — without your own credentials below, all content will Pass without scanning. Blocklist terms still apply.</>}
            </p>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
              <div className="field">
                <label>Endpoint URL</label>
                <input
                  type="url"
                  value={modEndpoint}
                  onChange={e => setModEndpoint(e.target.value)}
                  placeholder="https://your-resource.cognitiveservices.azure.com/"
                />
                <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                  Your Azure Content Safety resource endpoint. Leave blank to use the platform default.
                </span>
              </div>
              <div className="field">
                <label>API key</label>
                <input
                  type="password"
                  value={modKeyInput}
                  onChange={e => { setModKeyInput(e.target.value); setModKeyTouched(true); }}
                  placeholder={mod?.customKeyMasked ?? 'Paste a new key, or leave blank'}
                  autoComplete="new-password"
                />
                <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                  {mod?.customKeyMasked
                    ? <>Existing key: <code style={{ fontFamily: 'var(--font-mono)' }}>{mod.customKeyMasked}</code>. Paste a new key to rotate, or clear the field and save to remove.</>
                    : <>Paste your Azure Content Safety key. We mask it once stored — it is not returned to the dashboard after first save.</>}
                </span>
              </div>
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 24 }}>
          <button
            className="btn btn-primary"
            onClick={saveModeration}
            disabled={modSaving}
          >
            {modSaving ? 'Saving...' : 'Save Moderation Settings'}
          </button>
        </div>
      </div>
    </div>
  );
}

interface CheckboxRowProps {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: string;
  help: string;
  disabled?: boolean;
}

function CheckboxRow({ checked, onChange, label, help, disabled }: CheckboxRowProps) {
  return (
    <label style={{
      display: 'grid',
      gridTemplateColumns: '20px 1fr',
      gap: 12,
      alignItems: 'start',
      cursor: disabled ? 'not-allowed' : 'pointer',
      opacity: disabled ? 0.55 : 1,
    }}>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={e => onChange(e.target.checked)}
        style={{ marginTop: 3 }}
      />
      <div>
        <div style={{ fontSize: 14, color: 'var(--text-primary)', lineHeight: 1.4 }}>{label}</div>
        <div style={{ fontSize: 12, color: 'var(--text-dim)', lineHeight: 1.5, marginTop: 2 }}>{help}</div>
      </div>
    </label>
  );
}
