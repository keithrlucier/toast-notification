import type { NotificationStatus } from '../api/notifications';

const STATUS_CONFIG: Record<string, { label: string; color: string }> = {
  Queued:             { label: 'Queued',     color: '#7A7A92' },
  Sending:            { label: 'Sending',    color: '#60A5FA' },
  Sent:               { label: 'Sent',       color: '#4ADE80' },
  PartialFailure:     { label: 'Partial',    color: '#FBBF24' },
  PartiallyDelivered: { label: 'Partial',    color: '#FBBF24' },
  Failed:             { label: 'Failed',     color: '#F87171' },
  PendingReview:      { label: 'Review',     color: '#FBBF24' },
  Rejected:           { label: 'Rejected',   color: '#F87171' },
};

interface Props {
  status: NotificationStatus | string;
}

export default function StatusBadge({ status }: Props) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, color: '#7A7A92' };
  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 5,
      fontSize: 11,
      fontWeight: 600,
      color: cfg.color,
      background: `${cfg.color}18`,
      borderRadius: 4,
      padding: '2px 8px',
      letterSpacing: '0.04em',
      textTransform: 'uppercase',
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: cfg.color, flexShrink: 0 }} />
      {cfg.label}
    </span>
  );
}

interface DeviceStatusProps {
  online: boolean;
}

export function DeviceStatus({ online }: DeviceStatusProps) {
  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 5,
      fontSize: 11,
      fontWeight: 600,
      color: online ? '#4ADE80' : '#7A7A92',
      background: online ? '#4ADE8018' : '#7A7A9218',
      borderRadius: 4,
      padding: '2px 8px',
      letterSpacing: '0.04em',
      textTransform: 'uppercase',
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: online ? '#4ADE80' : '#7A7A92', flexShrink: 0 }} />
      {online ? 'Online' : 'Offline'}
    </span>
  );
}
