import { useEffect, useState } from 'react';
import { moderationApi, type PendingNotification, type BlocklistEntry } from '../api/moderation';
import { ApiError } from '../api/client';
import ModerationSettingsForm from '../components/ModerationSettingsForm';

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
}

export default function Moderation() {
  const [pending, setPending]     = useState<PendingNotification[]>([]);
  const [blocklist, setBlocklist] = useState<BlocklistEntry[]>([]);
  const [tab, setTab]             = useState<'pending' | 'blocklist' | 'settings'>('pending');
  const [loading, setLoading]     = useState(true);
  const [error, setError]         = useState('');
  const [acting, setActing]       = useState<string | null>(null);
  const [newTerm, setNewTerm]     = useState('');
  const [addingTerm, setAddingTerm] = useState(false);

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const [p, b] = await Promise.all([
        moderationApi.pending().catch(() => [] as PendingNotification[]),
        moderationApi.blocklist().catch(() => [] as BlocklistEntry[]),
      ]);
      setPending(p);
      setBlocklist(b);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load moderation data.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const handleApprove = async (id: string) => {
    setActing(id);
    try {
      await moderationApi.approve(id);
      setPending(prev => prev.filter(n => n.notificationId !== id));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Approve failed.');
    } finally {
      setActing(null);
    }
  };

  const handleReject = async (id: string) => {
    if (!confirm('Reject this notification? It will not be delivered.')) return;
    setActing(id);
    try {
      await moderationApi.reject(id);
      setPending(prev => prev.filter(n => n.notificationId !== id));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Reject failed.');
    } finally {
      setActing(null);
    }
  };

  const handleAddTerm = async () => {
    if (!newTerm.trim()) return;
    setAddingTerm(true);
    try {
      const entry = await moderationApi.addBlocklistTerm(newTerm.trim());
      setBlocklist(prev => [entry, ...prev]);
      setNewTerm('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to add term.');
    } finally {
      setAddingTerm(false);
    }
  };

  const handleRemoveTerm = async (id: string, term: string) => {
    if (!confirm(`Remove "${term}" from the blocklist?`)) return;
    try {
      await moderationApi.removeBlocklistTerm(id);
      setBlocklist(prev => prev.filter(b => b.id !== id));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to remove term.');
    }
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Moderation</h1>
          <p className="subtitle">Review flagged notifications, manage the content blocklist, and configure tenant moderation policy</p>
        </div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
            <path d="M12.5 7A5.5 5.5 0 112.3 3.8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            <path d="M2 1v3h3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          Refresh
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 1, background: 'var(--bg-secondary)', borderRadius: 4, border: '1px solid rgba(15,23,42,0.12)', overflow: 'hidden', width: 'fit-content', marginBottom: 20 }}>
        {(['pending', 'blocklist', 'settings'] as const).map(t => (
          <button
            key={t}
            onClick={() => setTab(t)}
            style={{
              padding: '10px 20px',
              border: 'none',
              background: tab === t ? 'var(--bg-tertiary)' : 'transparent',
              color: tab === t ? 'var(--text-primary)' : 'var(--text-dim)',
              fontWeight: tab === t ? 600 : 400,
              fontSize: 13,
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              transition: 'background 0.15s',
              textTransform: 'capitalize',
            }}
          >
            {t === 'pending' ? 'Pending Review' : t === 'blocklist' ? 'Blocklist' : 'Settings'}
            {t === 'pending' && pending.length > 0 && (
              <span style={{ background: 'var(--status-error)', color: '#FFFFFF', borderRadius: 10, padding: '1px 7px', fontSize: 11, fontWeight: 700 }}>
                {pending.length}
              </span>
            )}
            {t === 'blocklist' && (
              <span style={{ background: 'var(--bg-tertiary)', color: 'var(--text-dim)', borderRadius: 10, padding: '1px 7px', fontSize: 11 }}>
                {blocklist.length}
              </span>
            )}
          </button>
        ))}
      </div>

      {loading && tab !== 'settings' ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
          <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
        </div>
      ) : tab === 'pending' ? (
        <PendingTab
          items={pending}
          acting={acting}
          onApprove={handleApprove}
          onReject={handleReject}
        />
      ) : tab === 'blocklist' ? (
        <BlocklistTab
          items={blocklist}
          newTerm={newTerm}
          onNewTermChange={setNewTerm}
          onAdd={handleAddTerm}
          onRemove={handleRemoveTerm}
          adding={addingTerm}
        />
      ) : (
        <ModerationSettingsForm />
      )}
    </div>
  );
}

interface PendingTabProps {
  items: PendingNotification[];
  acting: string | null;
  onApprove: (id: string) => void;
  onReject: (id: string) => void;
}

function PendingTab({ items, acting, onApprove, onReject }: PendingTabProps) {
  if (items.length === 0) {
    return (
      <div className="empty-state">
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none">
          <path d="M16 3l13 23H3L16 3z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
          <path d="M16 13v6M16 22v1" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
        <p>No notifications pending review.</p>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {items.map(n => (
        <div key={n.notificationId} className="card" style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 6 }}>
              {n.title}
            </div>
            {n.bodyLine1 && <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: n.bodyLine2 ? 2 : 0 }}>{n.bodyLine1}</p>}
            {n.bodyLine2 && <p style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{n.bodyLine2}</p>}
            <div style={{ display: 'flex', gap: 16, marginTop: 10, flexWrap: 'wrap' }}>
              <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>
                By <strong style={{ color: 'var(--text-secondary)' }}>{n.submittedByEmail}</strong>
              </span>
              <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>
                Target: <strong style={{ color: 'var(--text-secondary)', textTransform: 'capitalize' }}>{n.targetType}</strong>
                {n.deviceCount > 0 && ` (${n.deviceCount} devices)`}
              </span>
              <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>{formatDateTime(n.submittedAt)}</span>
            </div>
            {n.moderationReason && (
              <div style={{
                marginTop: 10, padding: '8px 12px',
                background: 'rgba(251,191,36,0.08)',
                border: '1px solid rgba(251,191,36,0.2)',
                borderRadius: 4, fontSize: 12, color: 'var(--status-warning)',
              }}>
                Flagged: {n.moderationReason}
              </div>
            )}
          </div>
          <div style={{ display: 'flex', gap: 8, flexShrink: 0 }}>
            <button
              className="btn btn-secondary"
              style={{ fontSize: 13 }}
              onClick={() => onApprove(n.notificationId)}
              disabled={acting === n.notificationId}
            >
              {acting === n.notificationId ? <span className="spinner" /> : 'Approve'}
            </button>
            <button
              className="btn btn-ghost"
              style={{ fontSize: 13, color: 'var(--status-error)' }}
              onClick={() => onReject(n.notificationId)}
              disabled={acting === n.notificationId}
            >
              Reject
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}

interface BlocklistTabProps {
  items: BlocklistEntry[];
  newTerm: string;
  onNewTermChange: (v: string) => void;
  onAdd: () => void;
  onRemove: (id: string, term: string) => void;
  adding: boolean;
}

function BlocklistTab({ items, newTerm, onNewTermChange, onAdd, onRemove, adding }: BlocklistTabProps) {
  return (
    <div>
      <div style={{ display: 'flex', gap: 8, marginBottom: 20 }}>
        <input
          type="text"
          placeholder="Add a blocked term or phrase..."
          value={newTerm}
          onChange={e => onNewTermChange(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && onAdd()}
          style={{
            flex: 1,
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.12)',
            borderRadius: 4,
            color: 'var(--text-primary)',
            padding: '10px 12px',
            fontSize: 14,
          }}
        />
        <button
          className="btn btn-primary"
          onClick={onAdd}
          disabled={adding || !newTerm.trim()}
        >
          {adding ? <span className="spinner" /> : 'Add'}
        </button>
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {items.length === 0 ? (
          <div className="empty-state">
            <p>No blocked terms. Add terms above to prevent matching content from sending.</p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Term</th>
                <th>Added</th>
                <th>Added by</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map(b => (
                <tr key={b.id}>
                  <td>
                    <code style={{ fontFamily: 'var(--font-mono)', fontSize: 13, color: 'var(--text-primary)' }}>
                      {b.term}
                    </code>
                  </td>
                  <td style={{ color: 'var(--text-dim)' }}>
                    {new Date(b.createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}
                  </td>
                  <td style={{ color: 'var(--text-dim)' }}>
                    {b.createdByEmail ?? '—'}
                  </td>
                  <td>
                    <button
                      className="btn btn-ghost"
                      style={{ fontSize: 12, padding: '5px 10px', color: 'var(--status-error)' }}
                      onClick={() => onRemove(b.id, b.term)}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
