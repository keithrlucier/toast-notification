import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { billingApi, type BillingPlan, type Invoice } from '../api/billing';

const TIER_DETAILS: Record<string, { price: string; description: string; color: string }> = {
  Free:       { price: 'Free',           description: 'Up to 10 devices. No credit card required.', color: 'var(--text-secondary)' },
  Pro:        { price: '$3/device/mo',   description: 'Up to 250 devices. Priority support.', color: 'var(--accent)' },
  Enterprise: { price: 'Custom pricing', description: 'Unlimited devices. Dedicated support.', color: '#60A5FA' },
};

const STATUS_COLOR: Record<string, string> = {
  Active:   'var(--status-success)',
  Trialing: '#60A5FA',
  PastDue:  'var(--status-warning)',
  Canceled: 'var(--status-error)',
};

export default function Billing() {
  const [plan, setPlan]       = useState<BillingPlan | null>(null);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState('');
  const [checkoutLoading, setCheckoutLoading] = useState<string | null>(null);
  const [portalLoading, setPortalLoading] = useState(false);
  const [searchParams] = useSearchParams();

  const load = useCallback(async () => {
    try {
      const [p, inv] = await Promise.all([billingApi.getPlan(), billingApi.getInvoices()]);
      setPlan(p);
      setInvoices(inv.invoices);
    } catch {
      setError('Failed to load billing information.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const handleCheckout = async (tier: 'Pro' | 'Enterprise') => {
    setCheckoutLoading(tier);
    try {
      const { url } = await billingApi.createCheckout(tier);
      window.location.href = url;
    } catch {
      setError('Could not start checkout. Please try again.');
      setCheckoutLoading(null);
    }
  };

  const handlePortal = async () => {
    setPortalLoading(true);
    try {
      const { url } = await billingApi.createPortal();
      window.location.href = url;
    } catch {
      setError('Could not open billing portal. Please try again.');
      setPortalLoading(false);
    }
  };

  const usedPct = plan
    ? plan.deviceLimit === null ? 0 : Math.min(100, Math.round((plan.consumedCount / plan.deviceLimit) * 100))
    : 0;

  const successSession = searchParams.get('session') === 'success';

  if (loading) {
    return (
      <div className="page-header">
        <div className="spinner" />
      </div>
    );
  }

  return (
    <div style={{ padding: '32px', maxWidth: 880 }}>
      <div className="page-header" style={{ marginBottom: 32 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Billing &amp; Plan
          </h1>
          <p style={{ margin: '4px 0 0', color: 'var(--text-secondary)', fontSize: 14 }}>
            Manage your subscription and device allocation.
          </p>
        </div>
        {plan?.stripeCustomerId && (
          <button
            className="btn btn-secondary"
            onClick={handlePortal}
            disabled={portalLoading}
          >
            {portalLoading ? 'Opening...' : 'Manage Billing'}
          </button>
        )}
      </div>

      {successSession && (
        <div style={{
          background: 'rgba(0,201,167,0.1)',
          border: '1px solid var(--accent)',
          borderRadius: 8,
          padding: '12px 16px',
          color: 'var(--accent)',
          fontSize: 14,
          marginBottom: 24,
        }}>
          Subscription activated. Your plan has been updated.
        </div>
      )}

      {error && <div className="error-banner" style={{ marginBottom: 24 }}>{error}</div>}

      {/* Current Plan Card */}
      {plan && (
        <div className="card" style={{ marginBottom: 24 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 16 }}>
            <div>
              <div style={{ fontSize: 12, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-dim)', marginBottom: 4 }}>
                Current Plan
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <span style={{ fontSize: 24, fontWeight: 700, color: TIER_DETAILS[plan.tier]?.color ?? 'var(--text-primary)' }}>
                  {plan.tierLabel}
                </span>
                <span style={{
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '2px 8px',
                  borderRadius: 12,
                  background: `${STATUS_COLOR[plan.billingStatus] ?? 'var(--text-dim)'}22`,
                  color: STATUS_COLOR[plan.billingStatus] ?? 'var(--text-dim)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.05em',
                }}>
                  {plan.billingStatus}
                </span>
              </div>
              {plan.licenseEnd && (
                <div style={{ fontSize: 13, color: 'var(--text-dim)', marginTop: 4 }}>
                  Renews {new Date(plan.licenseEnd).toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' })}
                </div>
              )}
            </div>

            <div style={{ minWidth: 220 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>Devices</span>
                <span style={{ fontSize: 13, fontWeight: 600, color: plan.isAtLimit ? 'var(--status-error)' : 'var(--text-primary)' }}>
                  {plan.consumedCount} / {plan.deviceLimit === null ? '∞' : plan.deviceLimit}
                </span>
              </div>
              <div style={{ height: 6, borderRadius: 3, background: 'rgba(255,255,255,0.08)', overflow: 'hidden' }}>
                <div style={{
                  height: '100%',
                  width: plan.deviceLimit === null ? '0%' : `${usedPct}%`,
                  background: plan.isAtLimit ? 'var(--status-error)' : plan.isNearLimit ? 'var(--status-warning)' : 'var(--accent)',
                  borderRadius: 3,
                  transition: 'width 0.3s',
                }} />
              </div>
              {plan.isNearLimit && !plan.isAtLimit && (
                <div style={{ fontSize: 12, color: 'var(--status-warning)', marginTop: 4 }}>
                  Approaching device limit — consider upgrading.
                </div>
              )}
              {plan.isAtLimit && (
                <div style={{ fontSize: 12, color: 'var(--status-error)', marginTop: 4 }}>
                  Device limit reached — new agents cannot register.
                </div>
              )}
              {plan.billingStatus === 'PastDue' && (
                <div style={{ fontSize: 12, color: 'var(--status-warning)', marginTop: 4 }}>
                  Payment past due — update billing to avoid service interruption.
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Plan Upgrade Cards */}
      {plan?.tier !== 'Enterprise' && (
        <div style={{ marginBottom: 32 }}>
          <h2 style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 16 }}>
            {plan?.tier === 'Free' ? 'Upgrade Your Plan' : 'Change Plan'}
          </h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 }}>
            {(['Pro', 'Enterprise'] as const)
              .filter(t => t !== plan?.tier)
              .map(tier => {
                const d = TIER_DETAILS[tier];
                return (
                  <div key={tier} className="card" style={{ border: `1px solid ${d.color}44` }}>
                    <div style={{ fontSize: 18, fontWeight: 700, color: d.color, marginBottom: 4 }}>
                      {tier}
                    </div>
                    <div style={{ fontSize: 22, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 8 }}>
                      {d.price}
                    </div>
                    <div style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 20 }}>
                      {d.description}
                    </div>
                    <button
                      className="btn btn-primary"
                      style={{ width: '100%' }}
                      onClick={() => handleCheckout(tier === 'Enterprise' ? 'Enterprise' : 'Pro')}
                      disabled={checkoutLoading === tier}
                    >
                      {checkoutLoading === tier ? 'Redirecting...' : `Upgrade to ${tier}`}
                    </button>
                  </div>
                );
              })}
          </div>
        </div>
      )}

      {/* Invoice History */}
      {invoices.length > 0 && (
        <div>
          <h2 style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 16 }}>
            Invoice History
          </h2>
          <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
            <table className="data-table" style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th>Period</th>
                  <th>Amount</th>
                  <th>Status</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {invoices.map(inv => (
                  <tr key={inv.id}>
                    <td>
                      <span style={{ fontSize: 13, color: 'var(--text-primary)' }}>
                        {new Date(inv.periodStart).toLocaleDateString('en-US', { month: 'short', year: 'numeric' })}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: 13, color: 'var(--text-primary)', fontFamily: 'var(--font-mono)' }}>
                        {inv.currency} {inv.amount.toFixed(2)}
                      </span>
                    </td>
                    <td>
                      <span style={{
                        fontSize: 11,
                        fontWeight: 600,
                        padding: '2px 8px',
                        borderRadius: 10,
                        background: inv.status === 'paid' ? 'rgba(0,201,167,0.12)' : 'rgba(239,68,68,0.12)',
                        color: inv.status === 'paid' ? 'var(--status-success)' : 'var(--status-error)',
                        textTransform: 'capitalize',
                      }}>
                        {inv.status}
                      </span>
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                        {inv.hostedUrl && (
                          <a href={inv.hostedUrl} target="_blank" rel="noreferrer" className="btn btn-ghost" style={{ fontSize: 12, padding: '4px 10px' }}>
                            View
                          </a>
                        )}
                        {inv.pdfUrl && (
                          <a href={inv.pdfUrl} target="_blank" rel="noreferrer" className="btn btn-ghost" style={{ fontSize: 12, padding: '4px 10px' }}>
                            PDF
                          </a>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {invoices.length === 0 && plan?.stripeCustomerId && (
        <div style={{ color: 'var(--text-dim)', fontSize: 14, marginTop: 8 }}>
          No invoices yet.
        </div>
      )}
    </div>
  );
}
