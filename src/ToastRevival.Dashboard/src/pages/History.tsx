import { Fragment, useEffect, useState } from 'react';
import { notificationsApi, type NotificationHistoryItem } from '../api/notifications';
import StatusBadge from '../components/StatusBadge';
import { ApiError } from '../api/client';

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
}

function deliveryPct(n: NotificationHistoryItem): string {
  if (n.targetDeviceCount === 0) return '—';
  return `${Math.round((n.deliveredCount / n.targetDeviceCount) * 100)}%`;
}

function interactionPct(n: NotificationHistoryItem): string {
  if (n.deliveredCount === 0) return '—';
  return `${Math.round((n.clickedCount / n.deliveredCount) * 100)}%`;
}

export default function History() {
  const [notifications, setNotifications] = useState<NotificationHistoryItem[]>([]);
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState('');
  const [page, setPage]         = useState(1);
  const [search, setSearch]     = useState('');
  const [expanded, setExpanded] = useState<string | null>(null);

  const PAGE_SIZE = 25;

  const load = async (p: number) => {
    setLoading(true);
    try {
      const data = await notificationsApi.list(p, PAGE_SIZE);
      setNotifications(data);
      setPage(p);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load notification history.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(1); }, []);

  const filtered = notifications.filter(n =>
    !search ||
    n.title.toLowerCase().includes(search.toLowerCase()) ||
    n.status.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>History</h1>
          <p className="subtitle">Sent notifications with delivery and interaction tracking</p>
        </div>
        <button className="btn btn-ghost" onClick={() => void load(page)} disabled={loading}>
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
            <path d="M12.5 7A5.5 5.5 0 112.3 3.8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            <path d="M2 1v3h3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          Refresh
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      <div style={{ marginBottom: 16 }}>
        <input
          type="search"
          placeholder="Search by title or status..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{
            width: '100%',
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(255,255,255,0.08)',
            borderRadius: 4,
            color: 'var(--text-primary)',
            padding: '9px 12px',
            fontSize: 14,
          }}
        />
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
            <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
          </div>
        ) : filtered.length === 0 ? (
          <div className="empty-state">
            <p>{search ? 'No notifications match your search.' : 'No notifications sent yet.'}</p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Status</th>
                <th>Delivered</th>
                <th>Interactions</th>
                <th>Sent</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(n => (
                <Fragment key={n.id}>
                  <tr
                    style={{ cursor: 'pointer' }}
                    onClick={() => setExpanded(prev => prev === n.id ? null : n.id)}
                  >
                    <td style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{n.title}</td>
                    <td><StatusBadge status={n.status} /></td>
                    <td>
                      <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{n.deliveredCount}</span>
                      <span style={{ color: 'var(--text-dim)' }}>/{n.targetDeviceCount}</span>
                      {' '}
                      <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>({deliveryPct(n)})</span>
                    </td>
                    <td>
                      <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{n.clickedCount}</span>
                      {' '}
                      <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>({interactionPct(n)})</span>
                    </td>
                    <td style={{ color: 'var(--text-dim)', whiteSpace: 'nowrap' }}>
                      {formatDateTime(n.createdAt)}
                    </td>
                    <td>
                      <svg
                        width="14" height="14" viewBox="0 0 14 14" fill="none"
                        style={{ transform: expanded === n.id ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s', color: 'var(--text-dim)' }}
                      >
                        <path d="M3 5l4 4 4-4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                    </td>
                  </tr>
                  {expanded === n.id && (
                    <tr style={{ background: 'rgba(255,255,255,0.01)' }}>
                      <td colSpan={6} style={{ padding: '16px 24px' }}>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16 }}>
                          <div>
                            <div style={{ fontSize: 11, color: 'var(--text-dim)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Notification ID</div>
                            <code style={{ fontSize: 11, color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' }}>{n.id}</code>
                          </div>
                          {n.sentAt && (
                            <div>
                              <div style={{ fontSize: 11, color: 'var(--text-dim)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Sent at</div>
                              <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{formatDateTime(n.sentAt)}</div>
                            </div>
                          )}
                          <div>
                            <div style={{ fontSize: 11, color: 'var(--text-dim)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Target devices</div>
                            <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{n.targetDeviceCount}</div>
                          </div>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && notifications.length > 0 && (
        <div style={{ display: 'flex', gap: 8, justifyContent: 'center', marginTop: 16 }}>
          <button
            className="btn btn-ghost"
            onClick={() => void load(page - 1)}
            disabled={page === 1 || loading}
          >
            Previous
          </button>
          <span style={{ lineHeight: '38px', color: 'var(--text-dim)', fontSize: 13 }}>Page {page}</span>
          <button
            className="btn btn-ghost"
            onClick={() => void load(page + 1)}
            disabled={notifications.length < PAGE_SIZE || loading}
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}
