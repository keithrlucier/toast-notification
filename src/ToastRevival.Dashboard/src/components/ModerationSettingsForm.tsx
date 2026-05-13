import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';

export interface ModerationSettings {
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

/**
 * Shared admin-only form for editing per-tenant content moderation policy.
 * Used by:
 *   - /moderation → Settings tab (operator's primary entry point)
 *   - /settings/tenant → Content Moderation card (alternate path for users who
 *     look for "settings" rather than the Moderation page)
 *
 * Both surfaces fetch and PUT the same endpoint (/api/tenant/moderation), so
 * saves from one surface are immediately visible on the other.
 */
export default function ModerationSettingsForm() {
  const [mod, setMod] = useState<ModerationSettings | null>(null);
  const [enabled, setEnabled]                       = useState(true);
  const [scanText, setScanText]                     = useState(true);
  const [scanImages, setScanImages]                 = useState(true);
  const [reviewSeverity, setReviewSeverity]         = useState(2);
  const [blockSeverity, setBlockSeverity]           = useState(5);
  const [requireApprovalAll, setRequireApprovalAll] = useState(false);
  const [customEndpoint, setCustomEndpoint]         = useState('');
  const [keyInput, setKeyInput]                     = useState('');
  const [keyTouched, setKeyTouched]                 = useState(false);
  const [blockedMessage, setBlockedMessage]         = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');

  useEffect(() => {
    api.get<ModerationSettings>('/api/tenant/moderation')
      .then(m => {
        setMod(m);
        setEnabled(m.enabled);
        setScanText(m.scanText);
        setScanImages(m.scanImages);
        setReviewSeverity(m.reviewSeverity);
        setBlockSeverity(m.blockSeverity);
        setRequireApprovalAll(m.requireApprovalAll);
        setCustomEndpoint(m.customEndpoint ?? '');
        setBlockedMessage(m.blockedMessage ?? '');
      })
      .catch(err => setError(err instanceof ApiError ? err.message : 'Failed to load moderation settings.'))
      .finally(() => setLoading(false));
  }, []);

  const save = async () => {
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      await api.put('/api/tenant/moderation', {
        enabled,
        scanText,
        scanImages,
        reviewSeverity,
        blockSeverity,
        requireApprovalAll,
        customEndpoint: customEndpoint.trim() || null,
        customKey: keyTouched ? (keyInput.trim() || '__clear__') : null,
        blockedMessage: blockedMessage.trim() || null,
      });
      setSuccess('Moderation settings saved.');
      setKeyTouched(false);
      setKeyInput('');
      const fresh = await api.get<ModerationSettings>('/api/tenant/moderation');
      setMod(fresh);
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save moderation settings.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
        <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
      </div>
    );
  }

  return (
    <div>
      <p style={{ fontSize: 13, color: 'var(--text-dim)', marginBottom: 20, maxWidth: 720, lineHeight: 1.5 }}>
        Every outgoing notification's text and (optional) hero image is scanned against Azure
        Content Safety before it ships. Any category score at or above the <strong>Review</strong>{' '}
        threshold routes the notification to{' '}
        <Link to="/moderation" style={{ color: 'var(--text-secondary)' }}>Pending Review</Link>;
        at or above the <strong>Block</strong> threshold it is rejected outright (HTTP 422).
        Banned terms are managed on the{' '}
        <Link to="/moderation" style={{ color: 'var(--text-secondary)' }}>Blocklist</Link> tab.
        Full reference:{' '}
        <Link to="/docs/moderation" style={{ color: 'var(--text-secondary)' }}>moderation guide</Link>.
      </p>

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

      <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>

        {/* Toggles */}
        <div>
          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
            Scanning
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <CheckboxRow
              checked={enabled}
              onChange={setEnabled}
              label="Moderation enabled"
              help="Master switch. When off, no scanning runs and every notification is treated as Pass. Blocklist terms still apply."
            />
            <CheckboxRow
              checked={scanText}
              onChange={setScanText}
              label="Scan notification text"
              help="Run title and body lines through Azure Content Safety. Disabling this does not disable the blocklist."
              disabled={!enabled}
            />
            <CheckboxRow
              checked={scanImages}
              onChange={setScanImages}
              label="Scan hero images"
              help="Run inline hero image URLs through Azure Content Safety image moderation. Asset-library images approved on upload are not re-scanned."
              disabled={!enabled}
            />
            <CheckboxRow
              checked={requireApprovalAll}
              onChange={setRequireApprovalAll}
              label="Require admin approval for every notification"
              help="Routes every outgoing notification to the Pending Review queue regardless of scan results."
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
                value={reviewSeverity}
                onChange={e => setReviewSeverity(parseInt(e.target.value, 10))}
                disabled={!enabled}
              >
                {[0,1,2,3,4,5,6].map(n => (
                  <option key={n} value={n}>{n}</option>
                ))}
              </select>
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                Scores at or above this level go to Pending Review. Platform default is <strong>2</strong>.
              </span>
            </div>
            <div className="field">
              <label>Block at or above</label>
              <select
                value={blockSeverity}
                onChange={e => setBlockSeverity(parseInt(e.target.value, 10))}
                disabled={!enabled}
              >
                {[0,1,2,3,4,5,6].map(n => (
                  <option key={n} value={n}>{n}</option>
                ))}
              </select>
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                Scores at or above this level are rejected outright. Default <strong>5</strong>. Must be greater than Review.
              </span>
            </div>
          </div>
        </div>

        {/* Blocked message */}
        <div className="field">
          <label>Blocked content message</label>
          <input
            type="text"
            value={blockedMessage}
            maxLength={500}
            onChange={e => setBlockedMessage(e.target.value)}
            placeholder="Content blocked by moderation policy."
          />
          <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
            Shown to senders when their notification is rejected. Blocklist hits still surface the matched term. Up to 500 characters.
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
                value={customEndpoint}
                onChange={e => setCustomEndpoint(e.target.value)}
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
                value={keyInput}
                onChange={e => { setKeyInput(e.target.value); setKeyTouched(true); }}
                placeholder={mod?.customKeyMasked ?? 'Paste a new key, or leave blank'}
                autoComplete="new-password"
              />
              <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', lineHeight: 1.5 }}>
                {mod?.customKeyMasked
                  ? <>Existing key: <code style={{ fontFamily: 'var(--font-mono)' }}>{mod.customKeyMasked}</code>. Paste a new key to rotate, or clear and save to remove.</>
                  : <>Paste your Azure Content Safety key. We mask it once stored — it is not returned to the dashboard after first save.</>}
              </span>
            </div>
          </div>
        </div>
      </div>

      <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 20 }}>
        <button className="btn btn-primary" onClick={save} disabled={saving}>
          {saving ? 'Saving...' : 'Save Moderation Settings'}
        </button>
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
