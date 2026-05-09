import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

interface UserResponse {
  id: string;
  email: string;
  role: 'Technician' | 'Admin' | 'SuperAdmin';
  mfaEnabled: boolean;
  lastLogin: string | null;
  createdAt: string;
}

type UserRole = 'Technician' | 'Admin' | 'SuperAdmin';

const ROLE_LABELS: Record<UserRole, string> = {
  Technician: 'Technician',
  Admin: 'Tenant Admin',
  SuperAdmin: 'Tenant Owner',
};

const ROLE_DESCRIPTIONS: Record<UserRole, string> = {
  Technician: 'Standard notification operations.',
  Admin: 'Tenant settings, billing, moderation, and team access.',
  SuperAdmin: 'Tenant owner authority and role assignment.',
};

function formatDate(iso: string | null): string {
  if (!iso) return 'Never';
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
}

function RoleBadge({ role }: { role: UserRole }) {
  const colors: Record<UserRole, string> = {
    Technician: '#64748B',
    Admin: '#1F6FBD',
    SuperAdmin: '#0F766E',
  };
  return (
    <span style={{
      display: 'inline-block',
      padding: '3px 8px',
      borderRadius: 4,
      fontSize: 11,
      fontWeight: 700,
      color: colors[role],
      background: `${colors[role]}12`,
      border: `1px solid ${colors[role]}33`,
      textTransform: 'uppercase',
      letterSpacing: '0.04em',
    }}>
      {ROLE_LABELS[role]}
    </span>
  );
}

const selectStyle: React.CSSProperties = {
  background: '#FFFFFF',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-sm)',
  color: 'var(--text-primary)',
  padding: '4px 8px',
  fontSize: 12,
  height: 30,
  fontFamily: 'inherit',
  cursor: 'pointer',
};

export default function Users() {
  const { user: currentUser } = useAuth();
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showInvite, setShowInvite] = useState(false);
  const [inviteEmail, setInviteEmail] = useState('');
  const [invitePassword, setInvitePassword] = useState('');
  const [inviteRole, setInviteRole] = useState<UserRole>('Technician');
  const [inviting, setInviting] = useState(false);
  const [inviteError, setInviteError] = useState('');
  const [updatingRole, setUpdatingRole] = useState<string | null>(null);
  const [removingUser, setRemovingUser] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await api.get<UserResponse[]>('/api/users');
      setUsers(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load users.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const handleInvite = async (e: React.FormEvent) => {
    e.preventDefault();
    setInviting(true);
    setInviteError('');
    try {
      await api.post('/api/users/invite', {
        email: inviteEmail,
        password: invitePassword,
        role: inviteRole,
      });
      setInviteEmail('');
      setInvitePassword('');
      setInviteRole('Technician');
      setShowInvite(false);
      await load();
    } catch (err) {
      setInviteError(err instanceof ApiError ? err.message : 'Failed to create account.');
    } finally {
      setInviting(false);
    }
  };

  const handleRoleChange = async (userId: string, role: UserRole) => {
    if (userId === currentUser?.userId) return;
    setUpdatingRole(userId);
    try {
      await api.put(`/api/users/${userId}/role`, { role });
      setUsers(prev => prev.map(u => u.id === userId ? { ...u, role } : u));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to update role.');
    } finally {
      setUpdatingRole(null);
    }
  };

  const handleRemove = async (userId: string) => {
    if (userId === currentUser?.userId) return;
    const target = users.find(u => u.id === userId);
    if (!confirm(`Remove ${target?.email ?? 'this account'} from this tenant?`)) return;

    setRemovingUser(userId);
    try {
      await api.delete(`/api/users/${userId}`);
      setUsers(prev => prev.filter(u => u.id !== userId));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to remove account.');
    } finally {
      setRemovingUser(null);
    }
  };

  const roleCounts = users.reduce<Record<UserRole, number>>((counts, u) => {
    counts[u.role] += 1;
    return counts;
  }, { Technician: 0, Admin: 0, SuperAdmin: 0 });

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Access Control</h1>
          <p className="subtitle">Provision operators and assign administrative authority</p>
        </div>
        <button
          className="btn btn-primary"
          onClick={() => setShowInvite(s => !s)}
        >
          {showInvite ? 'Cancel' : 'Provision Account'}
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 16, marginBottom: 20 }}>
        {(['SuperAdmin', 'Admin', 'Technician'] as UserRole[]).map(role => (
          <div className="metric-card" key={role} style={{ padding: 18 }}>
            <div className="metric-label">{ROLE_LABELS[role]}</div>
            <div className="metric-value" style={{ fontSize: 26 }}>{roleCounts[role]}</div>
            <div className="metric-sub">{ROLE_DESCRIPTIONS[role]}</div>
          </div>
        ))}
      </div>

      {showInvite && (
        <div className="card" style={{ marginBottom: 24 }}>
          <h3 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 700 }}>Provision Account</h3>
          <form onSubmit={handleInvite}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: 16, marginBottom: 16 }}>
              <div className="field">
                <label>Email</label>
                <input
                  type="email"
                  required
                  placeholder="technician@company.com"
                  value={inviteEmail}
                  onChange={e => setInviteEmail(e.target.value)}
                />
              </div>
              <div className="field">
                <label>Initial Password</label>
                <input
                  type="password"
                  required
                  minLength={8}
                  placeholder="Min 8 characters"
                  value={invitePassword}
                  onChange={e => setInvitePassword(e.target.value)}
                />
              </div>
              <div className="field">
                <label>Role</label>
                <select
                  value={inviteRole}
                  onChange={e => setInviteRole(e.target.value as UserRole)}
                >
                  <option value="Technician">Technician</option>
                  <option value="Admin">Tenant Admin</option>
                  <option value="SuperAdmin">Tenant Owner</option>
                </select>
              </div>
            </div>
            {inviteError && (
              <div style={{ color: 'var(--status-error)', fontSize: 13, marginBottom: 12 }}>
                {inviteError}
              </div>
            )}
            <button type="submit" className="btn btn-primary" disabled={inviting}>
              {inviting ? 'Creating...' : 'Create Account'}
            </button>
          </form>
        </div>
      )}

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>Loading...</div>
        ) : users.length === 0 ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>
            No accounts found. Provision an account above to get started.
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Email</th>
                <th>Role</th>
                <th>MFA</th>
                <th>Last Active</th>
                <th>Member Since</th>
                <th style={{ width: 190 }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map(user => {
                const isCurrentUser = user.id === currentUser?.userId;

                return (
                  <tr key={user.id}>
                    <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>
                      {user.email}
                      {isCurrentUser && (
                        <span style={{
                          marginLeft: 8,
                          padding: '2px 6px',
                          borderRadius: 3,
                          background: '#E8EEF5',
                          color: 'var(--text-dim)',
                          fontFamily: 'var(--font-sans)',
                          fontSize: 10,
                          fontWeight: 700,
                          letterSpacing: '0.05em',
                          textTransform: 'uppercase',
                        }}>
                          Current
                        </span>
                      )}
                    </td>
                    <td><RoleBadge role={user.role} /></td>
                    <td>
                      <span style={{
                        fontSize: 12,
                        fontWeight: 700,
                        color: user.mfaEnabled ? 'var(--status-success)' : 'var(--text-dim)',
                      }}>
                        {user.mfaEnabled ? 'Enabled' : 'Not set'}
                      </span>
                    </td>
                    <td>{formatDate(user.lastLogin)}</td>
                    <td>{formatDate(user.createdAt)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                        <select
                          style={selectStyle}
                          value={user.role}
                          disabled={updatingRole === user.id || isCurrentUser}
                          onChange={e => void handleRoleChange(user.id, e.target.value as UserRole)}
                          title={isCurrentUser ? 'You cannot change your own role.' : undefined}
                        >
                          <option value="Technician">Technician</option>
                          <option value="Admin">Tenant Admin</option>
                          <option value="SuperAdmin">Tenant Owner</option>
                        </select>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '4px 10px', height: 30, color: 'var(--status-error)' }}
                          disabled={removingUser === user.id || isCurrentUser}
                          onClick={() => void handleRemove(user.id)}
                          title={isCurrentUser ? 'You cannot remove your own account.' : undefined}
                        >
                          {removingUser === user.id ? '...' : 'Remove'}
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
    </div>
  );
}
