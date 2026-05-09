import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';

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
  Admin: 'Admin',
  SuperAdmin: 'Super Admin',
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
    Technician: 'var(--text-dim)',
    Admin: 'var(--status-info)',
    SuperAdmin: 'var(--accent)',
  };
  return (
    <span style={{
      display: 'inline-block',
      padding: '2px 8px',
      borderRadius: 4,
      fontSize: 12,
      fontWeight: 600,
      color: colors[role],
      background: `${colors[role]}18`,
      border: `1px solid ${colors[role]}40`,
    }}>
      {ROLE_LABELS[role]}
    </span>
  );
}

const selectStyle: React.CSSProperties = {
  background: 'var(--bg-tertiary)',
  border: '1px solid rgba(255,255,255,0.08)',
  borderRadius: 'var(--radius-sm)',
  color: 'var(--text-primary)',
  padding: '4px 8px',
  fontSize: 12,
  height: 30,
  fontFamily: 'inherit',
  cursor: 'pointer',
};

export default function Users() {
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
      setInviteError(err instanceof ApiError ? err.message : 'Failed to create user.');
    } finally {
      setInviting(false);
    }
  };

  const handleRoleChange = async (userId: string, role: UserRole) => {
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
    setRemovingUser(userId);
    try {
      await api.delete(`/api/users/${userId}`);
      setUsers(prev => prev.filter(u => u.id !== userId));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to remove user.');
    } finally {
      setRemovingUser(null);
    }
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Users</h1>
          <p className="subtitle">Manage team members and their permissions</p>
        </div>
        <button
          className="btn btn-primary"
          onClick={() => setShowInvite(s => !s)}
        >
          {showInvite ? 'Cancel' : 'Add User'}
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {showInvite && (
        <div className="card" style={{ marginBottom: 24 }}>
          <h3 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 600 }}>Add New User</h3>
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
                  <option value="Admin">Admin</option>
                  <option value="SuperAdmin">Super Admin</option>
                </select>
              </div>
            </div>
            {inviteError && (
              <div style={{ color: 'var(--status-error)', fontSize: 13, marginBottom: 12 }}>
                {inviteError}
              </div>
            )}
            <button type="submit" className="btn btn-primary" disabled={inviting}>
              {inviting ? 'Creating…' : 'Create User'}
            </button>
          </form>
        </div>
      )}

      <div className="card" style={{ padding: 0 }}>
        {loading ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>Loading…</div>
        ) : users.length === 0 ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>
            No users found. Add a user above to get started.
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
                <th style={{ width: 180 }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map(user => (
                <tr key={user.id}>
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>{user.email}</td>
                  <td><RoleBadge role={user.role} /></td>
                  <td>
                    <span style={{
                      fontSize: 12,
                      fontWeight: 600,
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
                        disabled={updatingRole === user.id}
                        onChange={e => void handleRoleChange(user.id, e.target.value as UserRole)}
                      >
                        <option value="Technician">Technician</option>
                        <option value="Admin">Admin</option>
                        <option value="SuperAdmin">Super Admin</option>
                      </select>
                      <button
                        className="btn btn-ghost"
                        style={{ fontSize: 12, padding: '4px 10px', height: 30, color: 'var(--status-error)' }}
                        disabled={removingUser === user.id}
                        onClick={() => void handleRemove(user.id)}
                      >
                        {removingUser === user.id ? '…' : 'Remove'}
                      </button>
                    </div>
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
