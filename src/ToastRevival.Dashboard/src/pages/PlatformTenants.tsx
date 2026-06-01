import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import MfaStepUpModal from '../components/MfaStepUpModal';

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

interface CreateTenantResult {
  tenantId: string;
  userId: string;
  subdomain: string;
  setPasswordViaEmail: boolean;
  emailSent: boolean;
}

export default function PlatformTenants() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const isPlatformAdmin = Boolean(user?.isPlatformAdmin);
  const [tenants, setTenants] = useState<TenantRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [showCreate, setShowCreate] = useState(false);

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
          <p className="subtitle">All tenants on the platform — create, suspend, extend, comp, or remove</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
            {loading ? 'Loading…' : 'Refresh'}
          </button>
          <button className="btn btn-primary" onClick={() => setShowCreate(true)}>
            New tenant
          </button>
        </div>
      </div>

      {showCreate && (
        <CreateTenantModal
          onClose={() => setShowCreate(false)}
          onCreated={(result) => {
            setShowCreate(false);
            navigate(`/system/tenants/${result.tenantId}`);
          }}
        />
      )}

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

function CreateTenantModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (result: CreateTenantResult) => void;
}) {
  const [name, setName] = useState('');
  const [subdomain, setSubdomain] = useState('');
  const [ownerEmail, setOwnerEmail] = useState('');
  const [ownerFullName, setOwnerFullName] = useState('');
  const [ownerPhone, setOwnerPhone] = useState('');
  const [passwordMode, setPasswordMode] = useState<'email' | 'set'>('email');
  const [initialPassword, setInitialPassword] = useState('');
  const [billingMode, setBillingMode] = useState<'trial' | 'paid' | 'comp'>('trial');
  const [trialDays, setTrialDays] = useState('14');
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  // FIX-MFA-5: creating a tenant now requires a fresh platform-admin step-up. On a
  // 403 mfa_required, replay submit() after the step-up modal elevates the session.
  const [stepUpRetry, setStepUpRetry] = useState<(() => void) | null>(null);
  const isMfaRequired = (err: unknown) =>
    err instanceof ApiError && err.status === 403 && /mfa verification/i.test(err.message);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (passwordMode === 'set' && initialPassword.trim().length < 8) {
      setError('Initial password must be at least 8 characters.');
      return;
    }

    setSubmitting(true);
    try {
      const body = {
        name: name.trim(),
        subdomain: subdomain.trim() || null,
        ownerEmail: ownerEmail.trim(),
        ownerFullName: ownerFullName.trim() || null,
        ownerPhone: ownerPhone.trim() || null,
        initialPassword: passwordMode === 'set' ? initialPassword : null,
        trialDays: billingMode === 'trial' ? Number.parseInt(trialDays, 10) || 14 : null,
        isComplimentary: billingMode === 'comp',
        note: note.trim() || null,
      };
      const result = await api.post<CreateTenantResult>('/api/system/tenants', body);
      onCreated(result);
    } catch (err) {
      if (isMfaRequired(err)) { setStepUpRetry(() => () => void submit(e)); return; }
      setError(err instanceof ApiError ? err.message : 'Failed to create tenant.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(15, 23, 42, 0.45)',
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        zIndex: 1000,
        padding: '60px 16px',
        overflowY: 'auto',
      }}
    >
      <div className="card" style={{ width: '100%', maxWidth: 640, padding: 24 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 16 }}>
          <div>
            <h2 style={{ margin: 0, fontSize: 20, fontWeight: 700 }}>Create tenant</h2>
            <p style={{ margin: '4px 0 0', color: 'var(--text-secondary)', fontSize: 13 }}>
              Provisions the tenant + a SuperAdmin owner account.
            </p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={onClose}>Close</button>
        </div>

        {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

        <form onSubmit={submit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
            <div className="field">
              <label>Tenant name *</label>
              <input
                type="text"
                required
                placeholder="Acme Corp"
                value={name}
                onChange={e => setName(e.target.value)}
                autoFocus
              />
            </div>
            <div className="field">
              <label>Subdomain (optional)</label>
              <input
                type="text"
                placeholder="auto from name"
                value={subdomain}
                onChange={e => setSubdomain(e.target.value)}
              />
            </div>
          </div>

          <div style={{ borderTop: '1px solid var(--border-subtle)', margin: '0 -24px 16px', padding: '16px 24px 0' }}>
            <h3 style={{ margin: '0 0 12px', fontSize: 14, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--text-dim)' }}>
              Owner account
            </h3>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
              <div className="field">
                <label>Email *</label>
                <input
                  type="email"
                  required
                  placeholder="owner@company.com"
                  value={ownerEmail}
                  onChange={e => setOwnerEmail(e.target.value)}
                />
              </div>
              <div className="field">
                <label>Full name</label>
                <input
                  type="text"
                  value={ownerFullName}
                  onChange={e => setOwnerFullName(e.target.value)}
                />
              </div>
              <div className="field">
                <label>Phone (enables SMS MFA)</label>
                <input
                  type="tel"
                  placeholder="+1 555 555 1212"
                  value={ownerPhone}
                  onChange={e => setOwnerPhone(e.target.value)}
                />
              </div>
              <div className="field">
                <label>Initial credentials</label>
                <select value={passwordMode} onChange={e => setPasswordMode(e.target.value as 'email' | 'set')}>
                  <option value="email">Email set-password link</option>
                  <option value="set">Set password now</option>
                </select>
              </div>
            </div>
            {passwordMode === 'set' && (
              <div className="field" style={{ marginBottom: 16 }}>
                <label>Initial password (min 8 chars)</label>
                <input
                  type="text"
                  required
                  minLength={8}
                  placeholder="Share with owner out-of-band"
                  value={initialPassword}
                  onChange={e => setInitialPassword(e.target.value)}
                />
              </div>
            )}
          </div>

          <div style={{ borderTop: '1px solid var(--border-subtle)', margin: '0 -24px 16px', padding: '16px 24px 0' }}>
            <h3 style={{ margin: '0 0 12px', fontSize: 14, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--text-dim)' }}>
              Billing
            </h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16, marginBottom: 16 }}>
              <BillingChoice
                label="Trial"
                detail={`${trialDays || '0'} days, then expires`}
                active={billingMode === 'trial'}
                onClick={() => setBillingMode('trial')}
              />
              <BillingChoice
                label="Paid / perpetual"
                detail="No expiry, standard billing"
                active={billingMode === 'paid'}
                onClick={() => setBillingMode('paid')}
              />
              <BillingChoice
                label="Complimentary"
                detail="No expiry, no caps, no billing"
                active={billingMode === 'comp'}
                onClick={() => setBillingMode('comp')}
              />
            </div>
            {billingMode === 'trial' && (
              <div className="field" style={{ marginBottom: 16, maxWidth: 200 }}>
                <label>Trial length (days)</label>
                <input
                  type="number"
                  min={1}
                  max={3650}
                  value={trialDays}
                  onChange={e => setTrialDays(e.target.value)}
                />
              </div>
            )}
          </div>

          <div className="field" style={{ marginBottom: 20 }}>
            <label>Note (audit log — optional)</label>
            <input
              type="text"
              placeholder='e.g. "Signed MSA 2026-05-28, comp through M9 GA"'
              value={note}
              onChange={e => setNote(e.target.value)}
            />
          </div>

          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
            <button type="button" className="btn btn-ghost" onClick={onClose} disabled={submitting}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'Creating…' : 'Create tenant'}
            </button>
          </div>
        </form>
      </div>

      {stepUpRetry && (
        <MfaStepUpModal
          action="Creating a tenant"
          onVerified={() => { const retry = stepUpRetry; setStepUpRetry(null); retry?.(); }}
          onCancel={() => setStepUpRetry(null)}
        />
      )}
    </div>
  );
}

function BillingChoice({
  label, detail, active, onClick,
}: { label: string; detail: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '12px 14px',
        borderRadius: 6,
        border: active ? '2px solid var(--accent)' : '1px solid var(--border-subtle)',
        background: active ? 'rgba(31, 111, 189, 0.06)' : 'var(--bg-primary)',
        cursor: 'pointer',
        textAlign: 'left',
        fontFamily: 'inherit',
      }}
    >
      <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 11, color: 'var(--text-dim)' }}>{detail}</div>
    </button>
  );
}
