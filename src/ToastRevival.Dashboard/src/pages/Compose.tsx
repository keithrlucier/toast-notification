import { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { notificationsApi, type SendNotificationRequest, type ActionButton, type TemplateDbRecord, type SaveTemplateRequest } from '../api/notifications';
import { devicesApi, type Device, type DeviceGroup } from '../api/devices';
import ToastPreview, { CharCount } from '../components/ToastPreview';
import BroadcastConfirmModal from '../components/BroadcastConfirmModal';
import { TEMPLATES } from './Templates';
import { ApiError } from '../api/client';

type TargetMode = 'All' | 'Group' | 'Device';
type AudioOption = { label: string; value: string };
type ScenarioOption = { label: string; value: string };

const AUDIO_OPTIONS: AudioOption[] = [
  { label: 'Default',   value: 'ms-winsoundevent:Notification.Default' },
  { label: 'Alarm',     value: 'ms-winsoundevent:Notification.Looping.Alarm' },
  { label: 'Reminder',  value: 'ms-winsoundevent:Notification.Reminder' },
  { label: 'SMS',       value: 'ms-winsoundevent:Notification.SMS' },
  { label: 'Silent',    value: 'silent' },
];

const SCENARIO_OPTIONS: ScenarioOption[] = [
  { label: 'Default',       value: '' },
  { label: 'Urgent',        value: 'urgent' },
  { label: 'Reminder',      value: 'reminder' },
  { label: 'Alarm',         value: 'alarm' },
  { label: 'Incoming Call', value: 'incomingCall' },
];

const BUTTON_STYLES = ['Default', 'Success', 'Critical'] as const;
const BUTTON_TYPES = [
  { label: 'Track click', value: 'Action' },
  { label: 'Open URL', value: 'Url' },
] as const;

const TITLE_MAX  = 48;
const BODY_MAX   = 90;

function slugifyActionId(label: string, fallback: string) {
  const slug = label
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, 48);
  return slug || fallback;
}

function isUrlButton(button: ActionButton) {
  return button.type === 'Url' || Boolean(button.url?.trim());
}

function normalizeButtons(buttons: ActionButton[]): ActionButton[] {
  return buttons.map((button, index) => {
    const label = button.label.trim();
    const actionId = slugifyActionId(button.actionId || label, `button_${index + 1}`);
    const type: ActionButton['type'] = isUrlButton(button) ? 'Url' : 'Action';
    return {
      ...button,
      label,
      actionId,
      type,
      url: type === 'Url' ? button.url?.trim() : undefined,
    };
  });
}

function validateButtons(buttons: ActionButton[]) {
  for (let i = 0; i < buttons.length; i++) {
    const button = buttons[i];
    const label = button.label.trim();
    if (!label) return `Button ${i + 1} needs a label.`;
    if (label.length > 32) return `Button ${i + 1} label must be 32 characters or fewer.`;

    if (isUrlButton(button)) {
      const url = button.url?.trim() ?? '';
      if (!url) return `Button "${label}" needs a URL.`;
      try {
        const parsed = new URL(url);
        if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
          return `Button "${label}" URL must start with http:// or https://.`;
        }
      } catch {
        return `Button "${label}" URL is not valid.`;
      }
    }
  }
  return '';
}

export default function Compose() {
  const location = useLocation();
  const navigate = useNavigate();
  const prefill  = location.state as (Partial<SendNotificationRequest> & { templateId?: string }) | null;

  // Content fields
  const [title,    setTitle]    = useState(prefill?.title ?? '');
  const [body1,    setBody1]    = useState(prefill?.bodyLine1 ?? '');
  const [body2,    setBody2]    = useState(prefill?.bodyLine2 ?? '');
  const [heroUrl,  setHeroUrl]  = useState(prefill?.heroImageUrl ?? '');
  const logoUrl = prefill?.logoUrl ?? '';
  const [audio,    setAudio]    = useState(prefill?.audioSetting ?? 'ms-winsoundevent:Notification.Default');
  const [scenario, setScenario] = useState(prefill?.scenario ?? '');
  const [buttons,  setButtons]  = useState<ActionButton[]>(prefill?.actionButtons ?? []);

  // Target
  const [targetMode,     setTargetMode]     = useState<TargetMode>('All');
  const [selectedDevices,setSelectedDevices]= useState<string[]>([]);
  const [selectedGroups, setSelectedGroups] = useState<string[]>([]);
  const [scheduledAt,    setScheduledAt]    = useState('');

  // Resources
  const [devices, setDevices] = useState<Device[]>([]);
  const [groups,  setGroups]  = useState<DeviceGroup[]>([]);

  // Template slug → DB Guid mapping (INFO-M4-001)
  const [templateDbIds, setTemplateDbIds] = useState<Record<string, string>>({});
  const [appliedTemplateSlug, setAppliedTemplateSlug] = useState('');

  // State
  const [sending,      setSending]      = useState(false);
  const [error,        setError]        = useState('');
  const [success,      setSuccess]      = useState('');
  const [showConfirm,  setShowConfirm]  = useState(false);
  const [activeSection,setActiveSection]= useState<'template' | 'content' | 'target'>('content');

  // Save as template
  const [showSaveTemplate, setShowSaveTemplate] = useState(false);
  const [templateName,     setTemplateName]     = useState('');
  const [savingTemplate,   setSavingTemplate]   = useState(false);
  const [saveTemplateMsg,  setSaveTemplateMsg]  = useState('');

  useEffect(() => {
    void devicesApi.list().then(setDevices).catch(() => {});
    void devicesApi.listGroups().then(setGroups).catch(() => {});
    void notificationsApi.templates()
      .then((ts: TemplateDbRecord[]) => {
        const map: Record<string, string> = {};
        ts.forEach(t => { map[t.slug] = t.id; });
        setTemplateDbIds(map);
      })
      .catch(() => {}); // graceful — templateId stays undefined if endpoint fails
  }, []);

  const estimatedDeviceCount = (() => {
    if (targetMode === 'All') return devices.length;
    if (targetMode === 'Device') return selectedDevices.length;
    return groups.filter(g => selectedGroups.includes(g.id)).reduce((s, g) => s + g.deviceCount, 0);
  })();

  const requiresMfa = targetMode === 'All';
  const requiresConfirm = estimatedDeviceCount > 100 || requiresMfa;

  const buildRequest = (): SendNotificationRequest => ({
    title,
    bodyLine1: body1 || undefined,
    bodyLine2: body2 || undefined,
    heroImageUrl: heroUrl || undefined,
    logoUrl: logoUrl || undefined,
    actionButtons: buttons.length > 0 ? normalizeButtons(buttons) : undefined,
    audioSetting: audio,
    scenario: scenario || undefined,
    targetType: targetMode,
    targetIds: targetMode === 'Device' ? selectedDevices : targetMode === 'Group' ? selectedGroups : undefined,
    templateId: appliedTemplateSlug ? (templateDbIds[appliedTemplateSlug] ?? undefined) : undefined,
    scheduledAt: scheduledAt ? new Date(scheduledAt).toISOString() : undefined,
  });

  const handleSend = () => {
    if (!title.trim()) { setError('Title is required.'); return; }
    const buttonError = validateButtons(buttons);
    if (buttonError) { setError(buttonError); return; }
    if (targetMode === 'Device' && selectedDevices.length === 0) { setError('Select at least one device.'); return; }
    if (targetMode === 'Group' && selectedGroups.length === 0)   { setError('Select at least one group.'); return; }
    setError('');
    if (requiresConfirm) { setShowConfirm(true); return; }
    void doSend();
  };

  const doSend = async () => {
    setSending(true);
    setError('');
    try {
      const result = await notificationsApi.send(buildRequest());
      setSuccess(`Notification sent (ID: ${result.id.slice(0, 8)}…). Status: ${result.status}`);
      setTimeout(() => navigate('/history'), 2000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to send notification.');
    } finally {
      setSending(false);
      setShowConfirm(false);
    }
  };

  const doSaveTemplate = async () => {
    if (!templateName.trim()) return;
    setSavingTemplate(true);
    setSaveTemplateMsg('');
    try {
      const req: SaveTemplateRequest = {
        name: templateName.trim(),
        title: title || undefined,
        bodyLine1: body1 || undefined,
        bodyLine2: body2 || undefined,
        actionButtonsJson: buttons.length > 0 ? JSON.stringify(normalizeButtons(buttons)) : undefined,
        audioSetting: audio,
        scenario: scenario || undefined,
      };
      await notificationsApi.saveTemplate(req);
      setSaveTemplateMsg('Template saved.');
      setTemplateName('');
      setTimeout(() => { setShowSaveTemplate(false); setSaveTemplateMsg(''); }, 1500);
    } catch {
      setSaveTemplateMsg('Failed to save template.');
    } finally {
      setSavingTemplate(false);
    }
  };

  const addButton = () => {
    if (buttons.length >= 3) return;
    setButtons(prev => [...prev, { label: 'Open Link', actionId: `url_${prev.length + 1}`, style: 'Default', type: 'Url', url: '' }]);
  };

  const updateButton = (i: number, patch: Partial<ActionButton>) =>
    setButtons(prev => prev.map((b, idx) => idx === i ? { ...b, ...patch } : b));

  const removeButton = (i: number) =>
    setButtons(prev => prev.filter((_, idx) => idx !== i));

  const applyTemplate = (templateId: string) => {
    const t = TEMPLATES.find(t => t.id === templateId);
    if (!t) return;
    setTitle(t.defaults.title);
    setBody1(t.defaults.bodyLine1 ?? '');
    setBody2(t.defaults.bodyLine2 ?? '');
    setButtons(t.defaults.actionButtons ?? []);
    setAudio(t.audioSetting);
    setScenario(t.scenario ?? '');
    setAppliedTemplateSlug(templateId);
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Compose</h1>
          <p className="subtitle">Build and send a managed Windows notification</p>
        </div>
      </div>

      {error   && <div className="error-banner">{error}</div>}
      {success && <div className="success-banner">{success}</div>}

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 420px', gap: 24, alignItems: 'start' }}>
        {/* Left — composer */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>

          {/* Template quick-pick */}
          <div className="card">
            <button
              style={{
                width: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                background: 'none',
                border: 'none',
                color: 'var(--text-primary)',
                cursor: 'pointer',
                padding: 0,
                fontSize: 14,
                fontWeight: 600,
              }}
              onClick={() => setActiveSection(s => s === 'template' ? 'content' : 'template')}
            >
              Start from a template
              <ChevronIcon open={activeSection === 'template'} />
            </button>
            {activeSection === 'template' && (
              <div style={{ marginTop: 16, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                {TEMPLATES.map(t => (
                  <button
                    key={t.id}
                    className="btn btn-secondary"
                    style={{ justifyContent: 'flex-start', fontSize: 13 }}
                    onClick={() => { applyTemplate(t.id); setActiveSection('content'); }}
                  >
                    {t.name}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Content */}
          <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
              <h3 style={{ fontSize: 14, fontWeight: 600, margin: 0 }}>Content</h3>
              <button
                className="btn btn-ghost"
                style={{ fontSize: 12, padding: '4px 10px' }}
                onClick={() => { setShowSaveTemplate(s => !s); setSaveTemplateMsg(''); }}
              >
                Save as Template
              </button>
            </div>

            {showSaveTemplate && (
              <div style={{
                display: 'flex', gap: 8, alignItems: 'center',
                padding: '10px 14px',
                background: 'rgba(0,201,167,0.05)',
                border: '1px solid rgba(0,201,167,0.2)',
                borderRadius: 6,
              }}>
                <input
                  type="text"
                  value={templateName}
                  onChange={e => setTemplateName(e.target.value)}
                  onKeyDown={e => { if (e.key === 'Enter') void doSaveTemplate(); }}
                  placeholder="Template name"
                  maxLength={64}
                  autoFocus
                  style={{
                    flex: 1,
                    background: 'var(--bg-tertiary)',
                    border: '1px solid rgba(15,23,42,0.12)',
                    borderRadius: 4,
                    color: 'var(--text-primary)',
                    padding: '7px 10px',
                    fontSize: 13,
                  }}
                />
                <button
                  className="btn btn-primary"
                  style={{ fontSize: 12, padding: '7px 14px', minHeight: 0 }}
                  onClick={() => void doSaveTemplate()}
                  disabled={savingTemplate || !templateName.trim()}
                >
                  {savingTemplate ? <span className="spinner" /> : 'Save'}
                </button>
                {saveTemplateMsg && (
                  <span style={{ fontSize: 12, color: saveTemplateMsg.includes('Failed') ? 'var(--status-error)' : 'var(--accent)' }}>
                    {saveTemplateMsg}
                  </span>
                )}
              </div>
            )}

            <div className="field">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
                <label htmlFor="title">Title <span style={{ color: 'var(--status-error)' }}>*</span></label>
                <CharCount current={title.length} max={TITLE_MAX} />
              </div>
              <input
                id="title"
                type="text"
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="Notification title"
                maxLength={TITLE_MAX + 10}
                autoFocus
              />
            </div>

            <div className="field">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
                <label htmlFor="body1">Body line 1</label>
                <CharCount current={body1.length} max={BODY_MAX} />
              </div>
              <input
                id="body1"
                type="text"
                value={body1}
                onChange={e => setBody1(e.target.value)}
                placeholder="First line of body text"
                maxLength={BODY_MAX + 10}
              />
            </div>

            <div className="field">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
                <label htmlFor="body2">Body line 2</label>
                <CharCount current={body2.length} max={BODY_MAX} />
              </div>
              <input
                id="body2"
                type="text"
                value={body2}
                onChange={e => setBody2(e.target.value)}
                placeholder="Second line of body text"
                maxLength={BODY_MAX + 10}
              />
            </div>

            <div className="field">
              <label htmlFor="heroUrl">Hero image URL</label>
              <input
                id="heroUrl"
                type="url"
                value={heroUrl}
                onChange={e => setHeroUrl(e.target.value)}
                placeholder="https://cdn.example.com/image.jpg"
              />
              <p style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4 }}>
                Recommended: 364 × 180 px. Upload via <a href="/assets" style={{ color: 'var(--accent)', textDecoration: 'none' }}>Assets</a> to get a hosted URL.
              </p>
            </div>

            {/* Action buttons */}
            <div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
                <label style={{ fontSize: 12, fontWeight: 500, color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                  Action Buttons
                </label>
                {buttons.length < 3 && (
                  <button className="btn btn-ghost" style={{ fontSize: 12, padding: '4px 8px' }} onClick={addButton}>
                    + Add button
                  </button>
                )}
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {buttons.map((btn, i) => (
                  <div
                    key={i}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(140px, 1fr) 110px 100px auto',
                      gap: 8,
                      alignItems: 'center',
                      padding: 10,
                      border: '1px solid rgba(15,23,42,0.10)',
                      borderRadius: 6,
                      background: 'rgba(15,23,42,0.02)',
                    }}
                  >
                    <input
                      type="text"
                      value={btn.label}
                      onChange={e => updateButton(i, { label: e.target.value })}
                      placeholder="Button label"
                      maxLength={40}
                      style={{
                        background: 'var(--bg-tertiary)',
                        border: '1px solid rgba(15,23,42,0.12)',
                        borderRadius: 4,
                        color: 'var(--text-primary)',
                        padding: '8px 10px',
                        fontSize: 13,
                        minWidth: 0,
                      }}
                    />
                    <select
                      value={isUrlButton(btn) ? 'Url' : 'Action'}
                      onChange={e => updateButton(i, {
                        type: e.target.value as 'Action' | 'Url',
                        url: e.target.value === 'Url' ? (btn.url ?? '') : undefined,
                      })}
                      style={{
                        background: 'var(--bg-tertiary)',
                        border: '1px solid rgba(15,23,42,0.12)',
                        borderRadius: 4,
                        color: 'var(--text-primary)',
                        padding: '8px 10px',
                        fontSize: 12,
                        cursor: 'pointer',
                      }}
                    >
                      {BUTTON_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                    </select>
                    <select
                      value={btn.style ?? 'Default'}
                      onChange={e => updateButton(i, { style: e.target.value as 'Default' | 'Success' | 'Critical' })}
                      style={{
                        background: 'var(--bg-tertiary)',
                        border: '1px solid rgba(15,23,42,0.12)',
                        borderRadius: 4,
                        color: 'var(--text-primary)',
                        padding: '8px 10px',
                        fontSize: 12,
                        cursor: 'pointer',
                      }}
                    >
                      {BUTTON_STYLES.map(s => <option key={s} value={s}>{s}</option>)}
                    </select>
                    <button
                      className="btn btn-ghost"
                      style={{ padding: '8px', color: 'var(--text-dim)', flexShrink: 0 }}
                      onClick={() => removeButton(i)}
                      aria-label="Remove button"
                    >
                      <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                        <path d="M2 2l10 10M12 2L2 12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
                      </svg>
                    </button>

                    <input
                      type="text"
                      value={btn.actionId}
                      onChange={e => updateButton(i, { actionId: e.target.value })}
                      placeholder="action-id"
                      style={{
                        gridColumn: isUrlButton(btn) ? '1 / 2' : '1 / 5',
                        background: 'var(--bg-tertiary)',
                        border: '1px solid rgba(15,23,42,0.12)',
                        borderRadius: 4,
                        color: 'var(--text-dim)',
                        padding: '8px 10px',
                        fontSize: 12,
                        fontFamily: 'var(--font-mono)',
                        minWidth: 0,
                      }}
                    />
                    {isUrlButton(btn) && (
                      <input
                        type="url"
                        value={btn.url ?? ''}
                        onChange={e => updateButton(i, { url: e.target.value })}
                        placeholder="https://teams.microsoft.com/l/meetup-join/..."
                        style={{
                          gridColumn: '2 / 5',
                          background: 'var(--bg-tertiary)',
                          border: '1px solid rgba(15,23,42,0.12)',
                          borderRadius: 4,
                          color: 'var(--text-primary)',
                          padding: '8px 10px',
                          fontSize: 13,
                          minWidth: 0,
                        }}
                      />
                    )}
                  </div>
                ))}
                {buttons.length === 0 && (
                  <p style={{ fontSize: 12, color: 'var(--text-dim)' }}>No action buttons. Up to 3 supported.</p>
                )}
              </div>
            </div>

            {/* Audio + Scenario */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
              <div className="field">
                <label htmlFor="audio">Audio</label>
                <select id="audio" value={audio} onChange={e => setAudio(e.target.value)}>
                  {AUDIO_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
              <div className="field">
                <label htmlFor="scenario">Scenario</label>
                <select id="scenario" value={scenario} onChange={e => setScenario(e.target.value)}>
                  {SCENARIO_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
            </div>
          </div>

          {/* Target */}
          <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <h3 style={{ fontSize: 14, fontWeight: 600 }}>Target</h3>

            {/* Target type */}
            <div style={{ display: 'flex', gap: 1, background: 'var(--bg-tertiary)', borderRadius: 4, overflow: 'hidden', border: '1px solid rgba(15,23,42,0.12)' }}>
              {(['All', 'Group', 'Device'] as TargetMode[]).map(mode => (
                <button
                  key={mode}
                  onClick={() => setTargetMode(mode)}
                  style={{
                    flex: 1,
                    padding: '10px',
                    border: 'none',
                    background: targetMode === mode ? 'var(--bg-secondary)' : 'transparent',
                    color: targetMode === mode ? 'var(--text-primary)' : 'var(--text-dim)',
                    fontWeight: targetMode === mode ? 600 : 400,
                    fontSize: 13,
                    cursor: 'pointer',
                    transition: 'background 0.15s',
                  }}
                >
                  {mode === 'All' ? `All Devices (${devices.length})` : mode}
                </button>
              ))}
            </div>

            {targetMode === 'Device' && (
              <DeviceMultiSelect
                devices={devices}
                selected={selectedDevices}
                onChange={setSelectedDevices}
              />
            )}

            {targetMode === 'Group' && (
              <GroupMultiSelect
                groups={groups}
                selected={selectedGroups}
                onChange={setSelectedGroups}
              />
            )}

            {targetMode === 'All' && (
              <div style={{
                background: 'rgba(251,191,36,0.08)',
                border: '1px solid rgba(251,191,36,0.25)',
                borderRadius: 4,
                padding: '10px 14px',
                fontSize: 13,
                color: 'var(--status-warning)',
              }}>
                Broadcasting to all devices requires MFA verification. You'll be prompted before sending.
              </div>
            )}

            {/* Schedule */}
            <div className="field">
              <label htmlFor="schedule">Schedule (optional — leave blank to send now)</label>
              <input
                id="schedule"
                type="datetime-local"
                value={scheduledAt}
                onChange={e => setScheduledAt(e.target.value)}
                style={{ colorScheme: 'dark' }}
              />
              <p style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 4 }}>
                Times are in your local timezone. Converted to UTC on save.
              </p>
            </div>

            {/* Send */}
            <div style={{ display: 'flex', gap: 12, alignItems: 'center', paddingTop: 8, borderTop: '1px solid rgba(15,23,42,0.10)' }}>
              <button
                className="btn btn-primary"
                style={{ flex: 1, justifyContent: 'center', padding: '12px' }}
                onClick={handleSend}
                disabled={sending || !title.trim()}
              >
                {sending ? <span className="spinner" /> : (
                  <>
                    <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                      <path d="M12.5 1.5l-6 11-1.5-4.5L1 6.5l11.5-5z" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" />
                    </svg>
                    {scheduledAt ? 'Schedule' : 'Send Now'}
                    {estimatedDeviceCount > 0 && ` (${estimatedDeviceCount} device${estimatedDeviceCount !== 1 ? 's' : ''})`}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>

        {/* Right — live preview panel, fixed */}
        <div style={{ position: 'sticky', top: 32 }}>
          <h3 style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 12 }}>
            Live Preview
          </h3>
          <ToastPreview
            title={title}
            bodyLine1={body1 || undefined}
            bodyLine2={body2 || undefined}
            heroImageUrl={heroUrl || undefined}
            logoUrl={logoUrl || undefined}
            actionButtons={buttons}
            scenario={scenario || undefined}
          />
          <p style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 10, textAlign: 'center' }}>
            Preview matches Windows 11 Action Center rendering in Segoe UI
          </p>
        </div>
      </div>

      {showConfirm && (
        <BroadcastConfirmModal
          deviceCount={estimatedDeviceCount}
          requiresMfa={requiresMfa}
          onConfirm={() => void doSend()}
          onCancel={() => setShowConfirm(false)}
        />
      )}
    </div>
  );
}

/* Sub-components */

interface DeviceMultiSelectProps {
  devices: Device[];
  selected: string[];
  onChange: (ids: string[]) => void;
}

function DeviceMultiSelect({ devices, selected, onChange }: DeviceMultiSelectProps) {
  const [search, setSearch] = useState('');
  const filtered = devices.filter(d =>
    d.machineName.toLowerCase().includes(search.toLowerCase()) ||
    d.username.toLowerCase().includes(search.toLowerCase())
  );

  const toggle = (id: string) =>
    onChange(selected.includes(id) ? selected.filter(s => s !== id) : [...selected, id]);

  return (
    <div>
      <input
        type="search"
        placeholder="Search devices..."
        value={search}
        onChange={e => setSearch(e.target.value)}
        style={{
          width: '100%',
          background: 'var(--bg-tertiary)',
          border: '1px solid rgba(15,23,42,0.12)',
          borderRadius: 4,
          color: 'var(--text-primary)',
          padding: '8px 10px',
          fontSize: 13,
          marginBottom: 8,
        }}
      />
      <div style={{ maxHeight: 200, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 2 }}>
        {filtered.map(d => (
          <label key={d.id} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 10px', borderRadius: 4, cursor: 'pointer', background: selected.includes(d.id) ? 'rgba(0,201,167,0.06)' : 'transparent' }}>
            <input
              type="checkbox"
              checked={selected.includes(d.id)}
              onChange={() => toggle(d.id)}
              style={{ accentColor: 'var(--accent)' }}
            />
            <span style={{ fontSize: 13, color: 'var(--text-primary)' }}>{d.machineName}</span>
            <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>{d.username}</span>
            <span style={{ marginLeft: 'auto', width: 6, height: 6, borderRadius: '50%', background: d.isOnline ? '#4ADE80' : '#7A7A92', flexShrink: 0 }} />
          </label>
        ))}
        {filtered.length === 0 && <p style={{ fontSize: 12, color: 'var(--text-dim)', padding: 8 }}>No devices found.</p>}
      </div>
      {selected.length > 0 && (
        <p style={{ fontSize: 12, color: 'var(--accent)', marginTop: 8 }}>{selected.length} device{selected.length !== 1 ? 's' : ''} selected</p>
      )}
    </div>
  );
}

interface GroupMultiSelectProps {
  groups: DeviceGroup[];
  selected: string[];
  onChange: (ids: string[]) => void;
}

function GroupMultiSelect({ groups, selected, onChange }: GroupMultiSelectProps) {
  const toggle = (id: string) =>
    onChange(selected.includes(id) ? selected.filter(s => s !== id) : [...selected, id]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {groups.length === 0 && <p style={{ fontSize: 12, color: 'var(--text-dim)' }}>No device groups configured.</p>}
      {groups.map(g => (
        <label key={g.id} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 10px', borderRadius: 4, cursor: 'pointer', background: selected.includes(g.id) ? 'rgba(0,201,167,0.06)' : 'transparent' }}>
          <input
            type="checkbox"
            checked={selected.includes(g.id)}
            onChange={() => toggle(g.id)}
            style={{ accentColor: 'var(--accent)' }}
          />
          <span style={{ fontSize: 13, color: 'var(--text-primary)' }}>{g.name}</span>
          <span style={{ fontSize: 11, color: 'var(--text-dim)', marginLeft: 'auto' }}>{g.deviceCount} devices</span>
        </label>
      ))}
    </div>
  );
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" style={{ transform: open ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s', color: 'var(--text-dim)' }}>
      <path d="M4 6l4 4 4-4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

