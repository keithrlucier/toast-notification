import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

interface TenantUser {
  id: string;
  email: string;
  role: string;
  isPlatformAdmin: boolean;
  mfaEnabled: boolean;
  lastLogin: string | null;
  createdAt: string;
}

interface TenantDetail {
  tenant: {
    id: string;
    name: string;
    subdomain: string;
    billingStatus: string;
    licenseStart: string | null;
    licenseEnd: string | null;
    stripeCustomerId: string | null;
    stripeSubscriptionId: string | null;
    suspendedAt: string | null;
    suspendedReason: string | null;
    isComplimentary: boolean;
    complimentaryReason: string | null;
    activeDeviceCount: number;
    monthlyBill: number;
    recentNotificationVolume: number;
    createdAt: string;
    updatedAt: string;
  };
  users: TenantUser[];
  deviceStatusCounts: { status: string; count: number }[];
}

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

function formatDateTime(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
}

function formatCurrency(value: number): string {
  return value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });
}

export default function PlatformTenantDetail() {
  const { id = '' } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { user } = useAuth();
  const isPlatformAdmin = Boolean(user?.isPlatformAdmin);

  const [data, setData] = useState<TenantDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [acting, setActing] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!isPlatformAdmin || !id) return;
    setLoading(true);
    setError('');
    try {
      const res = await api.get<TenantDetail>(`/api/system/tenants/${id}`);
      setData(res);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load tenant.');
    } finally {
      setLoading(false);
    }
  }, [isPlatformAdmin, id]);

  useEffect(() => { void load(); }, [load]);

  const runAction = async (key: string, fn: () => Promise<void>) => {
    setActing(key);
    setError('');
    try {
      await fn();
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Action failed.');
    } finally {
      setActing(null);
    }
  };

  if (!isPlatformAdmin) {
    return (
      <div className="card">
        <h1 style={{ fontSize: 20, marginBottom: 8 }}>Platform access required</h1>
        <p style={{ color: 'var(--text-secondary)' }}>Tenant administration is restricted to platform administrators.</p>
      </div>
    );
  }

  if (loading && !data) return <div className="card">Loading tenant…</div>;
  if (!data) {
    return (
      <div className="card">
        {error || 'Tenant not found.'}
        <div style={{ marginTop: 16 }}>
          <Link to="/system/tenants" className="btn btn-ghost">← Back to tenants</Link>
        </div>
      </div>
    );
  }

  const { tenant, users, deviceStatusCounts } = data;
  const suspended = Boolean(tenant.suspendedAt);
  const comp = tenant.isComplimentary;

  const onSuspend = () => {
    const reason = window.prompt(`Suspend ${tenant.name}? Reason (visible in audit log):`);
    if (reason === null) return;
    void runAction('suspend', () => api.post(`/api/system/tenants/${tenant.id}/suspend`, { reason }));
  };

  const onResume = () => {
    if (!window.confirm(`Resume ${tenant.name}? Users will be able to sign in again.`)) return;
    void runAction('resume', () => api.post(`/api/system/tenants/${tenant.id}/resume`));
  };

  const onExtend = () => {
    const raw = window.prompt(`Extend ${tenant.name} by how many days?`, '30');
    if (raw === null) return;
    const days = Number.parseInt(raw, 10);
    if (!Number.isFinite(days) || days <= 0 || days > 3650) {
      setError('Days must be a positive integer (max 3650).');
      return;
    }
    void runAction('extend', () => api.post(`/api/system/tenants/${tenant.id}/extend`, { days }));
  };

  const onGrantComp = () => {
    const reason = window.prompt(`Grant ${tenant.name} complimentary access (indefinite, unlimited devices, no billing)? Reason:`);
    if (reason === null) return;
    void runAction('grant-comp', () => api.post(`/api/system/tenants/${tenant.id}/grant-complimentary`, { reason }));
  };

  const onRevokeComp = () => {
    if (!window.confirm(`Revoke complimentary access for ${tenant.name}? Normal billing rules will resume.`)) return;
    void runAction('revoke-comp', () => api.post(`/api/system/tenants/${tenant.id}/revoke-complimentary`));
  };

  const onDeleteTenant = () => {
    const confirmName = window.prompt(
      `DELETE ${tenant.name} and all of its users, devices, notifications, and assets. This cannot be undone.\n\nType the tenant name to confirm:`);
    if (confirmName === null) return;
    if (confirmName !== tenant.name) {
      setError('Confirmation text did not match the tenant name. Aborted.');
      return;
    }
    void runAction('delete-tenant', async () => {
      await api.delete(`/api/system/tenants/${tenant.id}?confirm=${encodeURIComponent(tenant.name)}`);
      navigate('/system/tenants');
    });
  };

  const onResetUserPassword = (u: TenantUser) => {
    if (!window.confirm(`Send password reset email to ${u.email}?`)) return;
    void runAction(`reset-${u.id}`, async () => {
      await api.post(`/api/system/users/${u.id}/reset-password`);
    });
  };

  const onDeleteUser = (u: TenantUser) => {
    if (u.id === user?.userId) {
      setError('You cannot delete your own account.');
      return;
    }
    if (!window.confirm(`Delete user ${u.email}? This removes them from the tenant entirely.`)) return;
    void runAction(`delete-user-${u.id}`, async () => {
      await api.delete(`/api/system/users/${u.id}`);
    });
  };

  return (
    <div>
      <div style={{ marginBottom: 12 }}>
        <Link to="/system/tenants" style={{ color: 'var(--text-dim)', fontSize: 13 }}>← All tenants</Link>
      </div>
      <div className="page-header">
        <div>
          <h1>{tenant.name}</h1>
          <p className="subtitle" style={{ fontFamily: 'var(--font-mono)' }}>{tenant.subdomain}</p>
        </div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </div>

      {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

      <div style={{ display: 'flex', gap: 8, marginBottom: 20 }}>
        {suspended && <Banner color="#B91C1C" title="Suspended" detail={tenant.suspendedReason ? `Reason: ${tenant.suspendedReason}` : null} />}
        {comp && <Banner color="#7C3AED" title="Complimentary access" detail={tenant.complimentaryReason ? `Note: ${tenant.complimentaryReason}` : 'No billing, no device cap, never expires.'} />}
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 16, marginBottom: 24 }}>
        <Metric label="Status" value={suspended ? 'Suspended' : tenant.billingStatus} />
        <Metric label="Active devices" value={String(tenant.activeDeviceCount)} />
        <Metric label="Monthly bill" value={comp ? '—' : formatCurrency(tenant.monthlyBill)} />
        <Metric label="Notifications (30d)" value={String(tenant.recentNotificationVolume)} />
      </div>

      <div className="card" style={{ marginBottom: 24 }}>
        <h3 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 700 }}>Tenant controls</h3>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          {!suspended ? (
            <button className="btn btn-secondary" onClick={onSuspend} disabled={acting === 'suspend'}>
              {acting === 'suspend' ? 'Suspending…' : 'Suspend tenant'}
            </button>
          ) : (
            <button className="btn btn-primary" onClick={onResume} disabled={acting === 'resume'}>
              {acting === 'resume' ? 'Resuming…' : 'Resume tenant'}
            </button>
          )}
          <button className="btn btn-secondary" onClick={onExtend} disabled={acting === 'extend' || comp}>
            {acting === 'extend' ? 'Extending…' : 'Extend license'}
          </button>
          {!comp ? (
            <button className="btn btn-secondary" onClick={onGrantComp} disabled={acting === 'grant-comp'}>
              {acting === 'grant-comp' ? 'Granting…' : 'Grant complimentary'}
            </button>
          ) : (
            <button className="btn btn-secondary" onClick={onRevokeComp} disabled={acting === 'revoke-comp'}>
              {acting === 'revoke-comp' ? 'Revoking…' : 'Revoke complimentary'}
            </button>
          )}
          <button
            className="btn btn-ghost"
            onClick={onDeleteTenant}
            disabled={acting === 'delete-tenant'}
            style={{ color: 'var(--status-error)', marginLeft: 'auto' }}
          >
            {acting === 'delete-tenant' ? 'Deleting…' : 'Delete tenant'}
          </button>
        </div>
        {comp && (
          <p style={{ color: 'var(--text-dim)', fontSize: 12, marginTop: 12 }}>
            Extension is disabled while complimentary access is active — there is no expiration to extend.
          </p>
        )}
      </div>

      <div className="card" style={{ marginBottom: 24 }}>
        <h3 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 700 }}>Subscription</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 20 }}>
          <Field label="License start" value={formatDate(tenant.licenseStart)} />
          <Field label="License end" value={comp ? 'Never' : formatDate(tenant.licenseEnd)} />
          <Field label="Stripe customer" value={tenant.stripeCustomerId ?? '—'} mono />
          <Field label="Stripe subscription" value={tenant.stripeSubscriptionId ?? '—'} mono />
          <Field label="Created" value={formatDate(tenant.createdAt)} />
          <Field label="Updated" value={formatDate(tenant.updatedAt)} />
        </div>
      </div>

      <div className="card" style={{ marginBottom: 24 }}>
        <h3 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 700 }}>Devices</h3>
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
          {deviceStatusCounts.length === 0 ? (
            <span style={{ color: 'var(--text-dim)', fontSize: 13 }}>No devices registered.</span>
          ) : (
            deviceStatusCounts.map(d => (
              <div key={d.status} style={{ fontSize: 13 }}>
                <span style={{ color: 'var(--text-dim)', textTransform: 'uppercase', fontSize: 11, fontWeight: 700, letterSpacing: '0.04em' }}>{d.status}</span>
                <span style={{ marginLeft: 8, fontWeight: 700 }}>{d.count}</span>
              </div>
            ))
          )}
        </div>
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        <div style={{ padding: '16px 18px', borderBottom: '1px solid var(--border-subtle)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Users ({users.length})</h3>
        </div>
        {users.length === 0 ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>No users in this tenant.</div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Email</th>
                <th>Role</th>
                <th>MFA</th>
                <th>Last login</th>
                <th>Created</th>
                <th style={{ width: 200 }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map(u => {
                const isMe = u.id === user?.userId;
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
                    <td>{u.role}</td>
                    <td>{u.mfaEnabled ? 'Enabled' : <span style={{ color: 'var(--text-dim)' }}>Not set</span>}</td>
                    <td>{formatDateTime(u.lastLogin)}</td>
                    <td>{formatDate(u.createdAt)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 8 }}>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '4px 10px', height: 30 }}
                          disabled={acting === `reset-${u.id}`}
                          onClick={() => onResetUserPassword(u)}
                        >
                          {acting === `reset-${u.id}` ? '…' : 'Reset password'}
                        </button>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '4px 10px', height: 30, color: 'var(--status-error)' }}
                          disabled={acting === `delete-user-${u.id}` || isMe}
                          onClick={() => onDeleteUser(u)}
                          title={isMe ? 'You cannot delete your own account.' : undefined}
                        >
                          {acting === `delete-user-${u.id}` ? '…' : 'Delete'}
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

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric-card" style={{ padding: 18 }}>
      <div className="metric-label">{label}</div>
      <div className="metric-value" style={{ fontSize: 22 }}>{value}</div>
    </div>
  );
}

function Field({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 13, color: 'var(--text-primary)', fontFamily: mono ? 'var(--font-mono)' : undefined, wordBreak: 'break-all' }}>{value}</div>
    </div>
  );
}

function Banner({ color, title, detail }: { color: string; title: string; detail: string | null }) {
  return (
    <div style={{
      padding: '10px 14px',
      borderRadius: 6,
      background: `${color}10`,
      border: `1px solid ${color}33`,
      color,
      flex: 1,
    }}>
      <div style={{ fontWeight: 700, fontSize: 13, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{title}</div>
      {detail && <div style={{ fontSize: 13, color: 'var(--text-primary)', marginTop: 4 }}>{detail}</div>}
    </div>
  );
}
