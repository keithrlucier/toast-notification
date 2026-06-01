import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import MfaStepUpModal from '../components/MfaStepUpModal';

interface UserRow {
  id: string;
  email: string;
  fullName: string | null;
  tenantId: string;
  tenantName: string;
  role: string;
  isPlatformAdmin: boolean;
  mfaEnabled: boolean;
  lastLogin: string | null;
  createdAt: string;
}

function formatDateTime(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
}

export default function PlatformUsers() {
  const { user: currentUser } = useAuth();
  const isPlatformAdmin = Boolean(currentUser?.isPlatformAdmin);
  const [query, setQuery] = useState('');
  const [users, setUsers] = useState<UserRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [acting, setActing] = useState<ReadonlySet<string>>(() => new Set());
  // FIX-MFA-5/PE-2: cross-tenant reset/delete now require a fresh step-up. On a
  // 403 mfa_required, stash a replay of the action and show the step-up modal.
  const [stepUpRetry, setStepUpRetry] = useState<(() => void) | null>(null);
  const isMfaRequired = (err: unknown) =>
    err instanceof ApiError && err.status === 403 && /mfa verification/i.test(err.message);

  const load = useCallback(async (search: string) => {
    if (!isPlatformAdmin) return;
    setLoading(true);
    setError('');
    try {
      const trimmed = search.trim();
      const url = trimmed.length > 0
        ? `/api/system/users?search=${encodeURIComponent(trimmed)}&limit=100`
        : '/api/system/users?limit=100';
      const res = await api.get<{ users: UserRow[] }>(url);
      setUsers(res.users);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load users.');
    } finally {
      setLoading(false);
    }
  }, [isPlatformAdmin]);

  // Debounced auto-search.
  useEffect(() => {
    const handle = window.setTimeout(() => { void load(query); }, 250);
    return () => window.clearTimeout(handle);
  }, [query, load]);

  if (!isPlatformAdmin) {
    return (
      <div className="card">
        <h1 style={{ fontSize: 20, marginBottom: 8 }}>Platform access required</h1>
        <p style={{ color: 'var(--text-secondary)' }}>Cross-tenant user search is restricted to platform administrators.</p>
      </div>
    );
  }

  const onResetPassword = async (u: UserRow) => {
    const key = `reset-${u.id}`;
    if (acting.has(key) || !window.confirm(`Send password reset email to ${u.email}?`)) return;
    setActing(prev => new Set(prev).add(key));
    setError('');
    try {
      await api.post(`/api/system/users/${u.id}/reset-password`);
    } catch (err) {
      if (isMfaRequired(err)) { setStepUpRetry(() => () => void onResetPassword(u)); return; }
      setError(err instanceof ApiError ? err.message : 'Failed to send reset email.');
    } finally {
      setActing(prev => { const n = new Set(prev); n.delete(key); return n; });
    }
  };

  const onDelete = async (u: UserRow) => {
    if (u.id === currentUser?.userId) {
      setError('You cannot delete your own account.');
      return;
    }
    const key = `delete-${u.id}`;
    if (acting.has(key) || !window.confirm(`Delete user ${u.email} from tenant "${u.tenantName}"? This cannot be undone.`)) return;
    setActing(prev => new Set(prev).add(key));
    setError('');
    try {
      await api.delete(`/api/system/users/${u.id}`);
      setUsers(prev => prev.filter(x => x.id !== u.id));
    } catch (err) {
      if (isMfaRequired(err)) { setStepUpRetry(() => () => void onDelete(u)); return; }
      setError(err instanceof ApiError ? err.message : 'Failed to delete user.');
    } finally {
      setActing(prev => { const n = new Set(prev); n.delete(key); return n; });
    }
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>All Users</h1>
          <p className="subtitle">Cross-tenant search — find an account, reset a password, or remove a user</p>
        </div>
      </div>

      {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

      <div className="card" style={{ marginBottom: 16, padding: '12px 16px' }}>
        <input
          type="search"
          placeholder="Search by email or full name…"
          value={query}
          onChange={e => setQuery(e.target.value)}
          style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
          autoFocus
        />
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>Loading…</div>
        ) : users.length === 0 ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>
            {query.trim() ? 'No users match that search.' : 'No users in the platform yet.'}
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Email</th>
                <th>Name</th>
                <th>Tenant</th>
                <th>Role</th>
                <th>MFA</th>
                <th>Last login</th>
                <th style={{ width: 220 }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map(u => {
                const isMe = u.id === currentUser?.userId;
                return (
                  <tr key={u.id}>
                    <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>
                      {u.email}
                      {u.isPlatformAdmin && (
                        <span style={{ marginLeft: 8, padding: '2px 6px', borderRadius: 3, background: '#FFE4D2', color: '#9A3412', fontSize: 10, fontWeight: 700, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Platform</span>
                      )}
                      {isMe && (
                        <span style={{ marginLeft: 8, padding: '2px 6px', borderRadius: 3, background: '#E8EEF5', color: 'var(--text-dim)', fontSize: 10, fontWeight: 700, letterSpacing: '0.05em', textTransform: 'uppercase' }}>You</span>
                      )}
                    </td>
                    <td>{u.fullName ?? <span style={{ color: 'var(--text-dim)' }}>—</span>}</td>
                    <td>
                      <Link to={`/system/tenants/${u.tenantId}`} style={{ color: 'var(--accent)' }}>
                        {u.tenantName}
                      </Link>
                    </td>
                    <td>{u.role}</td>
                    <td>{u.mfaEnabled ? 'Enabled' : <span style={{ color: 'var(--text-dim)' }}>Not set</span>}</td>
                    <td>{formatDateTime(u.lastLogin)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 8 }}>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '4px 10px', height: 30 }}
                          disabled={acting.has(`reset-${u.id}`)}
                          onClick={() => void onResetPassword(u)}
                        >
                          {acting.has(`reset-${u.id}`) ? '…' : 'Reset password'}
                        </button>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '4px 10px', height: 30, color: 'var(--status-error)' }}
                          disabled={acting.has(`delete-${u.id}`) || isMe}
                          onClick={() => void onDelete(u)}
                          title={isMe ? 'You cannot delete your own account.' : undefined}
                        >
                          {acting.has(`delete-${u.id}`) ? '…' : 'Delete'}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {stepUpRetry && (
        <MfaStepUpModal
          action="This action"
          onVerified={() => { const retry = stepUpRetry; setStepUpRetry(null); retry?.(); }}
          onCancel={() => setStepUpRetry(null)}
        />
      )}
    </div>
  );
}
