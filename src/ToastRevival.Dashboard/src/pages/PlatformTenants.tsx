import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

interface TenantRow {
  id: string;
  name: string;
  subdomain: string;
  deviceCount: number;
  userCount: number;
  billingStatus: string;
  subscriptionStartedAt: string | null;
  subscriptionEndsAt: string | null;
  monthlyBill: number;
  suspendedAt: string | null;
  suspendedReason: string | null;
  isComplimentary: boolean;
  complimentaryReason: string | null;
  createdAt: string;
}

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

function formatCurrency(value: number): string {
  return value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });
}

function StatusPill({ tenant }: { tenant: TenantRow }) {
  if (tenant.suspendedAt) {
    return <Pill color="#B91C1C" label="Suspended" />;
  }
  if (tenant.isComplimentary) {
    return <Pill color="#7C3AED" label="Complimentary" />;
  }
  const map: Record<string, string> = {
    Active: '#0F766E',
    Trialing: '#1F6FBD',
    PastDue: '#B45309',
    Canceled: '#64748B',
  };
  return <Pill color={map[tenant.billingStatus] ?? '#64748B'} label={tenant.billingStatus} />;
}

function Pill({ color, label }: { color: string; label: string }) {
  return (
    <span style={{
      display: 'inline-block',
      padding: '3px 8px',
      borderRadius: 4,
      fontSize: 11,
      fontWeight: 700,
      color,
      background: `${color}12`,
      border: `1px solid ${color}33`,
      textTransform: 'uppercase',
      letterSpacing: '0.04em',
    }}>{label}</span>
  );
}

export default function PlatformTenants() {
  const { user } = useAuth();
  const isPlatformAdmin = Boolean(user?.isPlatformAdmin);
  const [tenants, setTenants] = useState<TenantRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');

  const load = useCallback(async () => {
    if (!isPlatformAdmin) return;
    setLoading(true);
    setError('');
    try {
      const res = await api.get<{ tenants: TenantRow[] }>('/api/system/tenants');
      setTenants(res.tenants);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load tenants.');
    } finally {
      setLoading(false);
    }
  }, [isPlatformAdmin]);

  useEffect(() => { void load(); }, [load]);

  if (!isPlatformAdmin) {
    return (
      <div className="card">
        <h1 style={{ fontSize: 20, marginBottom: 8 }}>Platform access required</h1>
        <p style={{ color: 'var(--text-secondary)' }}>Tenant administration is restricted to platform administrators.</p>
      </div>
    );
  }

  const needle = query.trim().toLowerCase();
  const filtered = needle
    ? tenants.filter(t =>
        t.name.toLowerCase().includes(needle) ||
        t.subdomain.toLowerCase().includes(needle))
    : tenants;

  const summary = {
    total: tenants.length,
    active: tenants.filter(t => !t.suspendedAt && t.billingStatus === 'Active').length,
    trialing: tenants.filter(t => !t.suspendedAt && t.billingStatus === 'Trialing').length,
    complimentary: tenants.filter(t => t.isComplimentary).length,
    suspended: tenants.filter(t => t.suspendedAt).length,
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Tenants</h1>
          <p className="subtitle">All tenants on the platform — suspend, extend, comp, or remove</p>
        </div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </div>

      {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(0, 1fr))', gap: 16, marginBottom: 20 }}>
        <Metric label="Total" value={summary.total} />
        <Metric label="Active" value={summary.active} />
        <Metric label="Trialing" value={summary.trialing} />
        <Metric label="Complimentary" value={summary.complimentary} />
        <Metric label="Suspended" value={summary.suspended} />
      </div>

      <div className="card" style={{ marginBottom: 16, padding: '12px 16px' }}>
        <input
          type="search"
          placeholder="Filter by name or subdomain…"
          value={query}
          onChange={e => setQuery(e.target.value)}
          style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
        />
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>Loading…</div>
        ) : filtered.length === 0 ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-dim)' }}>
            {tenants.length === 0 ? 'No tenants yet.' : 'No tenants match that filter.'}
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Subdomain</th>
                <th>Status</th>
                <th style={{ textAlign: 'right' }}>Devices</th>
                <th style={{ textAlign: 'right' }}>Users</th>
                <th style={{ textAlign: 'right' }}>MRR</th>
                <th>License Ends</th>
                <th>Created</th>
                <th style={{ width: 100 }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(t => (
                <tr key={t.id}>
                  <td style={{ fontWeight: 600 }}>{t.name}</td>
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>{t.subdomain}</td>
                  <td><StatusPill tenant={t} /></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{t.deviceCount}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{t.userCount}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {t.isComplimentary ? '—' : formatCurrency(t.monthlyBill)}
                  </td>
                  <td>{t.isComplimentary ? <span style={{ color: 'var(--text-dim)' }}>Never</span> : formatDate(t.subscriptionEndsAt)}</td>
                  <td>{formatDate(t.createdAt)}</td>
                  <td>
                    <Link to={`/system/tenants/${t.id}`} className="btn btn-ghost" style={{ fontSize: 12, padding: '4px 10px', height: 30 }}>
                      Manage
                    </Link>
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

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="metric-card" style={{ padding: 18 }}>
      <div className="metric-label">{label}</div>
      <div className="metric-value" style={{ fontSize: 26 }}>{value}</div>
    </div>
  );
}
