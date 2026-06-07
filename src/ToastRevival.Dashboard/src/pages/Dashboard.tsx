import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { devicesApi, type Device } from '../api/devices';
import { agentApi, isUpToDate } from '../api/agent';
import { notificationsApi, type NotificationHistoryItem } from '../api/notifications';
import StatusBadge, { DeviceStatus } from '../components/StatusBadge';
import { ApiError } from '../api/client';
import DeployCommand from '../components/DeployCommand';

function formatRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1)  return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24)   return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

export default function Dashboard() {
  const [devices, setDevices]             = useState<Device[]>([]);
  const [notifications, setNotifications] = useState<NotificationHistoryItem[]>([]);
  const [targetVersion, setTargetVersion] = useState<string | null>(null);
  const [loading, setLoading]             = useState(true);
  const [error, setError]                 = useState('');

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      devicesApi.list().catch(() => [] as Device[]),
      notificationsApi.list(1, 10).catch(() => [] as NotificationHistoryItem[]),
      agentApi.version().then(v => v.version).catch(() => null),
    ]).then(([d, n, v]) => {
      if (cancelled) return;
      setDevices(d);
      setNotifications(n);
      setTargetVersion(v);
      setLoading(false);
    }).catch(err => {
      if (cancelled) return;
      setError(err instanceof ApiError ? err.message : 'Failed to load dashboard data.');
      setLoading(false);
    });
    return () => { cancelled = true; };
  }, []);

  const online       = devices.filter(d => d.isOnline).length;
  const offline      = devices.length - online;
  const sent7d = useMemo(
    () => notifications.filter(n => Date.now() - new Date(n.createdAt).getTime() < 7 * 86400 * 1000).length,
    [notifications],
  );

  const deliveryRate = useMemo(() => {
    const relevant = notifications.filter(n => n.targetDeviceCount > 0);
    if (!relevant.length) return null;
    const total = relevant.reduce((s, n) => s + n.targetDeviceCount, 0);
    const delivered = relevant.reduce((s, n) => s + n.deliveredCount, 0);
    return Math.round((delivered / total) * 100);
  }, [notifications]);

  // How many registered devices are reporting the latest released version.
  // Devices report 4-part ("0.4.44.0"); the feed target is 3-part ("0.4.44").
  const upToDate = useMemo(
    () => targetVersion ? devices.filter(d => isUpToDate(d.agentVersion, targetVersion)).length : 0,
    [devices, targetVersion],
  );

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
          <h1>Dashboard</h1>
          <p className="subtitle">Overview of your managed endpoints and notifications</p>
        </div>
        <Link to="/compose">
          <button className="btn btn-primary">
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
              <path d="M7 1.5v11M1.5 7h11" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
            </svg>
            New Notification
          </button>
        </Link>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Deploy command — shown when no devices registered */}
      {devices.length === 0 && !loading && <DeployCommand />}

      {/* Metrics */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 16, marginBottom: 32 }}>
        <div className="metric-card">
          <div className="metric-label">Agent Version</div>
          <div className="metric-value" style={{ fontFamily: 'var(--font-mono, monospace)' }}>
            {targetVersion ? `v${targetVersion}` : '—'}
          </div>
          <div className="metric-sub">
            {targetVersion
              ? `latest release · ${upToDate} of ${devices.length} up to date`
              : 'latest release version'}
          </div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Total Devices</div>
          <div className="metric-value">{devices.length}</div>
          <div className="metric-sub">registered endpoints</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Online Now</div>
          <div className="metric-value" style={{ color: online > 0 ? 'var(--status-success)' : 'var(--text-dim)' }}>
            {online}
          </div>
          <div className="metric-sub">{offline} offline</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Sent (7 days)</div>
          <div className="metric-value">{sent7d}</div>
          <div className="metric-sub">notifications</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Delivery Rate</div>
          <div className="metric-value">
            {deliveryRate !== null ? `${deliveryRate}%` : '—'}
          </div>
          <div className="metric-sub">across recent sends</div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
        {/* Recent notifications */}
        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          <div style={{ padding: '20px 24px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <h2 style={{ fontSize: 16, fontWeight: 600 }}>Recent Notifications</h2>
            <Link to="/history" style={{ fontSize: 13, color: 'var(--accent)', textDecoration: 'none' }}>View all</Link>
          </div>
          {notifications.length === 0 ? (
            <div className="empty-state">
              <p>No notifications sent yet.</p>
              <Link to="/compose">
                <button className="btn btn-secondary" style={{ fontSize: 13 }}>Send your first one</button>
              </Link>
            </div>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Status</th>
                  <th>Sent</th>
                </tr>
              </thead>
              <tbody>
                {notifications.slice(0, 6).map(n => (
                  <tr key={n.id}>
                    <td style={{ maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {n.title}
                    </td>
                    <td><StatusBadge status={n.status} /></td>
                    <td style={{ color: 'var(--text-dim)' }}>{formatRelative(n.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Device health */}
        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          <div style={{ padding: '20px 24px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <h2 style={{ fontSize: 16, fontWeight: 600 }}>Device Health</h2>
            <Link to="/devices" style={{ fontSize: 13, color: 'var(--accent)', textDecoration: 'none' }}>Manage</Link>
          </div>
          {devices.length === 0 ? (
            <div className="empty-state">
              <p>No devices yet.</p>
              <Link to="/devices" style={{ fontSize: 13, color: 'var(--accent)', textDecoration: 'none' }}>
                View deploy command →
              </Link>
            </div>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Machine</th>
                  <th>Status</th>
                  <th>Last seen</th>
                </tr>
              </thead>
              <tbody>
                {devices.slice(0, 6).map(d => (
                  <tr key={d.id}>
                    <td style={{ maxWidth: 140, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {d.machineName}
                    </td>
                    <td><DeviceStatus online={d.isOnline} /></td>
                    <td style={{ color: 'var(--text-dim)' }}>
                      {d.lastSeen ? formatRelative(d.lastSeen) : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}
