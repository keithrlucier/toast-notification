import { useEffect, useState } from 'react';
import { api, ApiError, apiErrorFromResponse, authHeaders } from '../api/client';
import { tenantLogoUrlForBrowser } from '../lib/tenantBranding';

// M12 Device Appearance — two independent cards on Tenant Settings. Design spec:
// two cards, never one; disabled state shows config (never hides it); each card
// owns its Save state. The field keys and positions below MUST match the server's
// TenantAppearance vocabulary (hostname|user|os|ip|tenant|customtext, and the four
// quadrant keys) — the backend drops anything outside this set on write.

const OVERLAY_FIELDS = [
  { key: 'hostname',   label: 'Hostname' },
  { key: 'user',       label: 'Logged-in User' },
  { key: 'os',         label: 'OS Version' },
  { key: 'ip',         label: 'IP Address' },
  { key: 'tenant',     label: 'Tenant Name' },
  { key: 'customtext', label: 'Custom Text' },
] as const;

const POSITIONS = [
  { key: 'bottom-right', label: 'Bottom Right' },
  { key: 'bottom-left',  label: 'Bottom Left' },
  { key: 'top-right',    label: 'Top Right' },
  { key: 'top-left',     label: 'Top Left' },
] as const;

const CUSTOM_TEXT_MAX = 80;

// ── Shared bits ─────────────────────────────────────────────────────────────

function Toggle({ checked, disabled, onChange, label }: {
  checked: boolean; disabled?: boolean; onChange: (v: boolean) => void; label: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      style={{
        position: 'relative', width: 40, height: 22, borderRadius: 11, padding: 0,
        border: 'none', flexShrink: 0,
        cursor: disabled ? 'default' : 'pointer',
        opacity: disabled ? 0.6 : 1,
        background: checked ? 'var(--accent)' : 'var(--bg-tertiary)',
        transition: 'background 150ms ease',
      }}
    >
      <span style={{
        position: 'absolute', top: 2, left: checked ? 20 : 2, width: 18, height: 18,
        borderRadius: '50%', background: '#fff',
        boxShadow: '0 1px 2px rgba(0,0,0,0.25)',
        transition: 'left 150ms ease',
      }} />
    </button>
  );
}

// 16:9 screen with a marker in the selected corner. The only "preview" — it shows
// corner placement, not the rendered text. left/top are always set (not swapped
// with right/bottom) so the marker animates between corners.
function QuadrantPreview({ position }: { position: string }) {
  const left = position.endsWith('left') ? '10%' : '68%';
  const top  = position.startsWith('top') ? '12%' : '66%';
  return (
    <div style={{
      position: 'relative', width: 96, height: 54, flexShrink: 0,
      background: 'var(--bg-tertiary)', border: '1px solid var(--text-dim)',
      borderRadius: 'var(--radius-sm)',
    }}>
      <span style={{
        position: 'absolute', width: '22%', height: '22%', left, top,
        background: 'var(--accent)', borderRadius: 2,
        transition: 'left 150ms ease, top 150ms ease',
      }} />
    </div>
  );
}

function SaveRow({ saving, error, success, onSave, label }: {
  saving: boolean; error: string; success: string; onSave: () => void; label: string;
}) {
  return (
    <>
      {error && <div className="error-banner" style={{ marginTop: 16 }}>{error}</div>}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 12, marginTop: 16 }}>
        {success && <span style={{ fontSize: 13, color: 'var(--status-success)' }}>{success}</span>}
        <button className="btn btn-primary" onClick={onSave} disabled={saving}>
          {saving ? 'Saving...' : label}
        </button>
      </div>
    </>
  );
}

const cardHeaderRow: React.CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12,
};
const subLine: React.CSSProperties = {
  fontSize: 12, color: 'var(--text-dim)', marginTop: 4, marginBottom: 16, lineHeight: 1.5,
};

function disabledBlock(enabled: boolean): React.CSSProperties {
  return { opacity: enabled ? 1 : 0.5, pointerEvents: enabled ? 'auto' : 'none' };
}

/**
 * Clamps to [10,100] and snaps to the nearest 5% step. Mirrors the server-side
 * normalizer in TenantAppearance.NormalizeOpacity so what the UI displays is
 * exactly what the server will persist — no surprise rounding on Save.
 */
function clampOpacity(raw: number): number {
  const clamped = Math.min(100, Math.max(10, Number.isFinite(raw) ? raw : 85));
  return Math.min(100, Math.max(10, Math.round(clamped / 5) * 5));
}

function Spinner() {
  return (
    <div style={{ display: 'flex', justifyContent: 'center', padding: '24px 0' }}>
      <span className="spinner" style={{ width: 20, height: 20, borderWidth: 3 }} />
    </div>
  );
}

// ── Card 1 — Desktop Overlay ─────────────────────────────────────────────────

function DesktopOverlayCard() {
  const [loaded, setLoaded]         = useState(false);
  const [enabled, setEnabled]       = useState(false);
  const [fields, setFields]         = useState<string[]>([]);
  const [position, setPosition]     = useState('bottom-right');
  const [customText, setCustomText] = useState('');
  // 0.4.15 — admin-controlled panel translucency, 10..100 in 5% steps.
  // Initial 85 matches the agent's pre-control hardcoded default; the GET
  // overwrites this once the live tenant value loads.
  const [opacityPercent, setOpacityPercent] = useState(85);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');

  useEffect(() => {
    api.get<{ enabled: boolean; fields: string[] | null; position: string | null; customText: string | null; opacityPercent: number | null }>('/api/tenant/overlay')
      .then(c => {
        setEnabled(c.enabled);
        setFields(c.fields ?? []);
        setPosition(c.position ?? 'bottom-right');
        setCustomText(c.customText ?? '');
        // Tolerate a pre-0.4.15 API that omits the field; clamp anything else
        // to the supported range so a hand-edited DB row can't break the UI.
        setOpacityPercent(clampOpacity(c.opacityPercent ?? 85));
      })
      .catch(() => { /* defaults stand; any real error resurfaces on Save */ })
      .finally(() => setLoaded(true));
  }, []);

  const customTextChecked = fields.includes('customtext');
  const toggleField = (key: string) =>
    setFields(prev => prev.includes(key) ? prev.filter(f => f !== key) : [...prev, key]);

  const save = async () => {
    setSaving(true); setError(''); setSuccess('');
    try {
      await api.put('/api/tenant/overlay', {
        enabled,
        fields,
        position,
        customText: customTextChecked ? (customText.trim() || null) : null,
        opacityPercent,
      });
      setSuccess('Saved.');
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save overlay.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div style={cardHeaderRow}>
        <h2 style={{ fontSize: 16, fontWeight: 600 }}>Desktop Overlay</h2>
        <Toggle checked={enabled} disabled={!loaded} onChange={setEnabled} label="Enable desktop overlay" />
      </div>
      <p style={subLine}>A read-only info panel shown on the desktop. Does not change the user&rsquo;s wallpaper.</p>

      {!loaded ? <Spinner /> : (
        <div style={disabledBlock(enabled)}>
          <div className="field" style={{ marginBottom: 16 }}>
            <label style={{ fontSize: 13, fontWeight: 500, color: 'var(--text-secondary)' }}>Show these fields</label>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 8 }}>
              {OVERLAY_FIELDS.map(f => (
                <label key={f.key} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, cursor: enabled ? 'pointer' : 'default' }}>
                  <input
                    type="checkbox"
                    checked={fields.includes(f.key)}
                    disabled={!enabled}
                    onChange={() => toggleField(f.key)}
                    style={{ width: 16, height: 16, accentColor: 'var(--accent)' }}
                  />
                  {f.label}
                </label>
              ))}
            </div>

            {customTextChecked && (
              <div style={{ marginTop: 8 }}>
                <input
                  type="text"
                  value={customText}
                  disabled={!enabled}
                  maxLength={CUSTOM_TEXT_MAX}
                  onChange={e => setCustomText(e.target.value)}
                  placeholder="e.g. Property of Acme Corp — IT Support x4500"
                />
                <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4, display: 'block', textAlign: 'right' }}>
                  {customText.length} / {CUSTOM_TEXT_MAX}
                </span>
              </div>
            )}
          </div>

          <div className="field">
            <label style={{ fontSize: 13, fontWeight: 500, color: 'var(--text-secondary)' }}>Position</label>
            <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 8, flexWrap: 'wrap' }}>
              <div style={{ display: 'inline-flex', borderRadius: 'var(--radius-sm)', overflow: 'hidden', border: '1px solid var(--bg-tertiary)' }}>
                {POSITIONS.map(p => (
                  <button
                    key={p.key}
                    type="button"
                    disabled={!enabled}
                    onClick={() => setPosition(p.key)}
                    style={{
                      padding: '6px 12px', fontSize: 12, border: 'none',
                      cursor: enabled ? 'pointer' : 'default',
                      background: position === p.key ? 'var(--accent)' : 'var(--bg-tertiary)',
                      color: position === p.key ? '#fff' : 'var(--text-secondary)',
                      fontWeight: position === p.key ? 600 : 400,
                    }}
                  >
                    {p.label}
                  </button>
                ))}
              </div>
              <QuadrantPreview position={position} />
            </div>
          </div>

          <div className="field" style={{ marginTop: 20 }}>
            <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 8 }}>
              <label style={{ fontSize: 13, fontWeight: 500, color: 'var(--text-secondary)' }}>
                Panel translucency
              </label>
              <span style={{ fontSize: 13, fontVariantNumeric: 'tabular-nums', color: 'var(--text-secondary)' }}>
                {opacityPercent}%
              </span>
            </div>
            <input
              type="range"
              min={10}
              max={100}
              step={5}
              value={opacityPercent}
              disabled={!enabled}
              onChange={e => setOpacityPercent(clampOpacity(Number(e.target.value)))}
              style={{ width: '100%', accentColor: 'var(--accent)' }}
              aria-label="Panel translucency percent"
            />
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, color: 'var(--text-dim)', marginTop: 4 }}>
              <span>Faint (10%)</span>
              <span>Solid (100%)</span>
            </div>
          </div>
        </div>
      )}

      <SaveRow saving={saving} error={error} success={success} onSave={save} label="Save Overlay" />
    </div>
  );
}

// ── Card 2 — Lock Screen Branding ────────────────────────────────────────────

type PreviewState = 'idle' | 'loading' | 'loaded' | 'error';

function LockScreenCard() {
  const [loaded, setLoaded]       = useState(false);
  const [enabled, setEnabled]     = useState(false);
  const [imageUrl, setImageUrl]   = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [previewState, setPreviewState] = useState<PreviewState>('idle');
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');
  const [removeArmed, setRemoveArmed] = useState(false);
  const [cacheBust, setCacheBust]     = useState(0);
  const [mountTime]                   = useState(() => Date.now());

  const previewUrl = imageUrl
    ? `${tenantLogoUrlForBrowser(imageUrl)}?v=${mountTime + cacheBust}`
    : '';

  useEffect(() => {
    api.get<{ enabled: boolean; imageUrl: string | null }>('/api/tenant/lockscreen')
      .then(c => { setEnabled(c.enabled); setImageUrl(c.imageUrl); })
      .catch(() => {})
      .finally(() => setLoaded(true));
  }, []);

  useEffect(() => { setPreviewState(previewUrl ? 'loading' : 'idle'); }, [previewUrl]);
  useEffect(() => { setRemoveArmed(false); }, [imageUrl]);

  const onUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true); setError('');
    try {
      const form = new FormData();
      form.append('file', file);
      const res = await fetch('/api/tenant/lockscreen-image', {
        method: 'POST',
        headers: authHeaders(),
        body: form,
      });
      if (!res.ok) throw await apiErrorFromResponse(res, '/api/tenant/lockscreen-image', 'Upload failed');
      const { url } = await res.json() as { url?: string };
      if (!url) throw new Error('Upload failed.');
      setImageUrl(url);
      setCacheBust(b => b + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed.');
    } finally {
      setUploading(false);
      e.target.value = '';
    }
  };

  const save = async () => {
    setSaving(true); setError(''); setSuccess('');
    try {
      await api.put('/api/tenant/lockscreen', { enabled, imageUrl });
      setSuccess('Saved.');
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save lock screen.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div style={cardHeaderRow}>
        <h2 style={{ fontSize: 16, fontWeight: 600 }}>Lock Screen Branding</h2>
        <Toggle checked={enabled} disabled={!loaded} onChange={setEnabled} label="Enable lock screen branding" />
      </div>
      <p style={subLine}>A branded image shown when a device is locked (Win+L, screensaver, lid close).</p>

      {!loaded ? <Spinner /> : (
        <div style={disabledBlock(enabled)}>
          <div className="field">
            <label>Image</label>
            <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
              {previewUrl && previewState !== 'error' && (
                <img
                  src={previewUrl}
                  alt="Lock screen preview"
                  style={{ width: 240, height: 135, objectFit: 'cover', border: '1px solid rgba(15,23,42,0.14)', borderRadius: 'var(--radius-sm)', background: 'var(--bg-tertiary)' }}
                  onLoad={() => setPreviewState('loaded')}
                  onError={() => setPreviewState('error')}
                />
              )}
              {previewUrl && previewState === 'error' && (
                <span style={{
                  width: 240, height: 135, display: 'flex', alignItems: 'center', justifyContent: 'center',
                  textAlign: 'center', fontSize: 11, color: 'var(--status-error)',
                  border: '1px solid rgba(15,23,42,0.14)', borderRadius: 'var(--radius-sm)', background: 'var(--bg-tertiary)',
                }}>
                  Preview unavailable
                </span>
              )}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <label style={{ cursor: (enabled && !uploading) ? 'pointer' : 'default', opacity: uploading ? 0.6 : 1 }}>
                  <input
                    type="file"
                    accept=".jpg,.jpeg,.png"
                    style={{ display: 'none' }}
                    disabled={!enabled || uploading}
                    onChange={onUpload}
                  />
                  <span className="btn btn-secondary" style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, pointerEvents: 'none' }}>
                    {uploading ? 'Uploading...' : imageUrl ? 'Replace' : 'Upload image'}
                  </span>
                </label>
                {imageUrl && (
                  <button
                    className="btn btn-ghost"
                    style={{
                      fontSize: 12,
                      padding: '6px 10px',
                      minHeight: 0,
                      color: removeArmed ? 'var(--status-error)' : undefined,
                    }}
                    disabled={!enabled}
                    onClick={() => {
                      if (removeArmed) {
                        setImageUrl(null);
                        setRemoveArmed(false);
                      } else {
                        setRemoveArmed(true);
                      }
                    }}
                    onBlur={() => setRemoveArmed(false)}
                  >
                    {removeArmed ? 'Confirm remove?' : 'Remove'}
                  </button>
                )}
              </div>
            </div>
            <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 8, display: 'block' }}>
              Recommended: 1920 × 1080 (16:9). JPG or PNG. Max 5 MB.
            </span>
            <span style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 2, display: 'block' }}>
              Applied to each device&rsquo;s lock screen at agent startup. On Group-Policy-managed
              endpoints, a policy-set lock screen may take precedence.
            </span>
          </div>
        </div>
      )}

      <SaveRow saving={saving} error={error} success={success} onSave={save} label="Save Lock Screen" />
    </div>
  );
}

export default function DeviceAppearanceCards() {
  return (
    <>
      <DesktopOverlayCard />
      <LockScreenCard />
    </>
  );
}
