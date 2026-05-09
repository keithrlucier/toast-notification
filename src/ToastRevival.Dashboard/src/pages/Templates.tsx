import { useNavigate } from 'react-router-dom';
import ToastPreview from '../components/ToastPreview';
import type { ActionButton, SendNotificationRequest } from '../api/notifications';

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

  const handleSelect = (template: Template) => {
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

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Templates</h1>
          <p className="subtitle">Choose a template to start composing your notification</p>
        </div>
      </div>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(340px, 1fr))',
        gap: 20,
      }}>
        {TEMPLATES.map(template => (
          <TemplateCard key={template.id} template={template} onSelect={handleSelect} />
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
        border: '1px solid rgba(255,255,255,0.06)',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
      }}
      onClick={() => onSelect(template)}
      onMouseEnter={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(0,201,167,0.4)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = '0 0 0 1px rgba(0,201,167,0.15)';
      }}
      onMouseLeave={e => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'rgba(255,255,255,0.06)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = 'none';
      }}
    >
      {/* Mini preview — scaled-down live CSS toast */}
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

      {/* Info */}
      <div style={{
        padding: '16px 20px',
        borderTop: '1px solid rgba(255,255,255,0.06)',
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
