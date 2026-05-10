import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import ToastPreview from '../components/ToastPreview';
import { notificationsApi, parseButtons, type ActionButton, type SendNotificationRequest, type TemplateDbRecord } from '../api/notifications';

interface Template {
  id: string;
  name: string;
  description: string;
  scenario?: string;
  audioSetting: string;
  defaults: {
    title: string;
    bodyLine1?: string;
    bodyLine2?: string;
    actionButtons?: ActionButton[];
  };
}

export const TEMPLATES: Template[] = [
  {
    id: 'announcement',
    name: 'Announcement',
    description: 'Company news, policy updates, general information',
    audioSetting: 'ms-winsoundevent:Notification.Default',
    defaults: {
      title: 'Company Announcement',
      bodyLine1: 'We have an important update for the team.',
      bodyLine2: 'Please review the details at your earliest convenience.',
      actionButtons: [{ label: 'View Details', actionId: 'view', style: 'Default' }],
    },
  },
  {
    id: 'alert',
    name: 'Alert',
    description: 'Security warnings, system issues, urgent IT notices',
    scenario: 'urgent',
    audioSetting: 'ms-winsoundevent:Notification.Looping.Alarm',
    defaults: {
      title: 'Security Alert',
      bodyLine1: 'Immediate action required on your device.',
      bodyLine2: 'Please contact IT support or follow the link below.',
      actionButtons: [
        { label: 'Acknowledge', actionId: 'acknowledge', style: 'Critical' },
        { label: 'Dismiss', actionId: 'dismiss', style: 'Default' },
      ],
    },
  },
  {
    id: 'action-required',
    name: 'Action Required',
    description: 'Password resets, software approvals, compliance tasks',
    audioSetting: 'ms-winsoundevent:Notification.Reminder',
    defaults: {
      title: 'Action Required',
      bodyLine1: 'Your password expires in 3 days. Please reset it now.',
      actionButtons: [
        { label: 'Reset Now', actionId: 'reset', style: 'Success' },
        { label: 'Remind Later', actionId: 'snooze', style: 'Default' },
      ],
    },
  },
  {
    id: 'reminder',
    name: 'Reminder',
    description: 'Meetings, deadlines, maintenance windows',
    scenario: 'reminder',
    audioSetting: 'ms-winsoundevent:Notification.Reminder',
    defaults: {
      title: 'Scheduled Maintenance',
      bodyLine1: 'System maintenance begins tonight at 11 PM EST.',
      bodyLine2: 'Please save your work and sign out before then.',
      actionButtons: [{ label: 'Got it', actionId: 'dismiss', style: 'Default' }],
    },
  },
  {
    id: 'celebration',
    name: 'Celebration',
    description: 'Birthdays, milestones, team wins, welcome messages',
    audioSetting: 'ms-winsoundevent:Notification.Default',
    defaults: {
      title: 'Congratulations!',
      bodyLine1: 'The team hit a major milestone today. Great work!',
    },
  },
  {
    id: 'maintenance',
    name: 'Maintenance',
    description: 'Scheduled downtime, update windows, system reboots',
    audioSetting: 'ms-winsoundevent:Notification.Default',
    defaults: {
      title: 'Scheduled Downtime',
      bodyLine1: 'The network will be unavailable Saturday 2–4 AM.',
      bodyLine2: 'Plan accordingly. Contact IT if you have questions.',
      actionButtons: [
        { label: 'Details', actionId: 'details', style: 'Default' },
        { label: 'Acknowledge', actionId: 'ack', style: 'Default' },
      ],
    },
  },
];

export default function Templates() {
  const navigate = useNavigate();
  const [customTemplates, setCustomTemplates] = useState<TemplateDbRecord[]>([]);

  useEffect(() => {
    void notificationsApi.templates()
      .then(ts => setCustomTemplates(ts.filter(t => !t.isDefault)))
      .catch(() => {});
  }, []);

  const handleSelectBuiltin = (template: Template) => {
    const state: Partial<SendNotificationRequest> & { templateId?: string } = {
      templateId: template.id,
      title: template.defaults.title,
      bodyLine1: template.defaults.bodyLine1,
      bodyLine2: template.defaults.bodyLine2,
      actionButtons: template.defaults.actionButtons,
      audioSetting: template.audioSetting,
      scenario: template.scenario,
    };
    navigate('/compose', { state });
  };

  const handleSelectCustom = (t: TemplateDbRecord) => {
    const state: Partial<SendNotificationRequest> = {
      title: t.titleTemplate ?? '',
      bodyLine1: t.bodyLine1Template ?? '',
      bodyLine2: t.bodyLine2Template ?? '',
      actionButtons: parseButtons(t.actionButtonsJson),
      audioSetting: t.audioSetting ?? 'ms-winsoundevent:Notification.Default',
      scenario: t.scenario === 'default' ? '' : t.scenario,
    };
    navigate('/compose', { state });
  };

  const handleDelete = (id: string) => {
    void notificationsApi.deleteTemplate(id).then(() => {
      setCustomTemplates(prev => prev.filter(t => t.id !== id));
    }).catch(() => {});
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Templates</h1>
          <p className="subtitle">Choose a template to start composing your notification</p>
        </div>
      </div>

      {customTemplates.length > 0 && (
        <>
          <h2 style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 16 }}>
            Saved
          </h2>
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(340px, 1fr))',
            gap: 20,
            marginBottom: 32,
          }}>
            {customTemplates.map(t => (
              <CustomTemplateCard
                key={t.id}
                template={t}
                onSelect={handleSelectCustom}
                onDelete={handleDelete}
              />
            ))}
          </div>
          <h2 style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 16 }}>
            Built-in
          </h2>
        </>
      )}

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(340px, 1fr))',
        gap: 20,
      }}>
        {TEMPLATES.map(template => (
          <TemplateCard key={template.id} template={template} onSelect={handleSelectBuiltin} />
        ))}
      </div>
    </div>
  );
}

interface TemplateCardProps {
  template: Template;
  onSelect: (t: Template) => void;
}

function TemplateCard({ template, onSelect }: TemplateCardProps) {
  return (
    <div
      style={{
        background: 'var(--bg-secondary)',
        borderRadius: 'var(--radius-md)',
        overflow: 'hidden',
        border: '1px solid rgba(15,23,42,0.10)',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
      }}
      onClick={() => onSelect(template)}
      onMouseEnter={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(0,201,167,0.4)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = '0 0 0 1px rgba(0,201,167,0.15)';
      }}
      onMouseLeave={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(15,23,42,0.10)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = 'none';
      }}
    >
      <div style={{ overflow: 'hidden', height: 140, position: 'relative' }}>
        <div style={{
          position: 'absolute',
          inset: 0,
          transform: 'scale(0.55)',
          transformOrigin: 'center center',
          pointerEvents: 'none',
        }}>
          <div style={{ padding: 16 }}>
            <ToastPreview
              title={template.defaults.title}
              bodyLine1={template.defaults.bodyLine1}
              bodyLine2={template.defaults.bodyLine2}
              actionButtons={template.defaults.actionButtons}
              scenario={template.scenario}
            />
          </div>
        </div>
      </div>

      <div style={{
        padding: '16px 20px',
        borderTop: '1px solid rgba(15,23,42,0.10)',
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 12,
      }}>
        <div>
          <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 4 }}>
            {template.name}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-dim)', lineHeight: 1.5 }}>
            {template.description}
          </div>
        </div>
        <button
          className="btn btn-primary"
          style={{ flexShrink: 0, fontSize: 12, padding: '6px 12px' }}
          onClick={e => { e.stopPropagation(); onSelect(template); }}
        >
          Use
        </button>
      </div>
    </div>
  );
}

interface CustomTemplateCardProps {
  template: TemplateDbRecord;
  onSelect: (t: TemplateDbRecord) => void;
  onDelete: (id: string) => void;
}

function CustomTemplateCard({ template, onSelect, onDelete }: CustomTemplateCardProps) {
  const [deleteArmed, setDeleteArmed] = useState(false);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const buttons = parseButtons(template.actionButtonsJson);

  return (
    <div
      style={{
        background: 'var(--bg-secondary)',
        borderRadius: 'var(--radius-md)',
        overflow: 'hidden',
        border: '1px solid rgba(0,201,167,0.18)',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
      }}
      onClick={() => onSelect(template)}
      onMouseEnter={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(0,201,167,0.5)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = '0 0 0 1px rgba(0,201,167,0.2)';
      }}
      onMouseLeave={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(0,201,167,0.18)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = 'none';
      }}
    >
      <div style={{ overflow: 'hidden', height: 140, position: 'relative' }}>
        <div style={{
          position: 'absolute',
          inset: 0,
          transform: 'scale(0.55)',
          transformOrigin: 'center center',
          pointerEvents: 'none',
        }}>
          <div style={{ padding: 16 }}>
            <ToastPreview
              title={template.titleTemplate ?? ''}
              bodyLine1={template.bodyLine1Template ?? undefined}
              bodyLine2={template.bodyLine2Template ?? undefined}
              actionButtons={buttons}
              scenario={template.scenario === 'default' ? undefined : template.scenario}
            />
          </div>
        </div>
        <span style={{
          position: 'absolute', top: 8, right: 8,
          fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em',
          padding: '2px 6px', borderRadius: 4,
          background: 'rgba(0,201,167,0.15)', color: 'var(--accent)',
        }}>
          Saved
        </span>
      </div>

      <div style={{
        padding: '16px 20px',
        borderTop: '1px solid rgba(15,23,42,0.10)',
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 12,
      }}>
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 4, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {template.name}
          </div>
          {template.titleTemplate && (
            <div style={{ fontSize: 12, color: 'var(--text-dim)', lineHeight: 1.5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {template.titleTemplate}
            </div>
          )}
        </div>
        <div style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
          <button
            className="btn btn-primary"
            style={{ fontSize: 12, padding: '6px 12px' }}
            onClick={e => { e.stopPropagation(); onSelect(template); }}
          >
            Use
          </button>
          {deleteArmed ? (
            <button
              ref={confirmRef}
              className="btn btn-ghost"
              style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-error)' }}
              onClick={e => { e.stopPropagation(); onDelete(template.id); }}
              onBlur={() => setDeleteArmed(false)}
            >
              Delete?
            </button>
          ) : (
            <button
              className="btn btn-ghost"
              style={{ fontSize: 12, padding: '6px 10px', color: 'var(--text-dim)' }}
              onClick={e => { e.stopPropagation(); setDeleteArmed(true); setTimeout(() => confirmRef.current?.focus(), 0); }}
            >
              ×
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
