import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { billingApi, type BillingPlan, type Invoice } from '../api/billing';

const STATUS_COLOR: Record<string, string> = {
  Active: 'var(--status-success)',
  Trialing: '#1F6FBD',
  PastDue: 'var(--status-warning)',
  Canceled: 'var(--status-error)',
};

function money(value: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
  }).format(value);
}

function formatDate(value: string | null): string {
  if (!value) return 'Not set';
  return new Date(value).toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

export default function Billing() {
  const [plan, setPlan] = useState<BillingPlan | null>(null);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [checkoutLoading, setCheckoutLoading] = useState(false);
  const [portalLoading, setPortalLoading] = useState(false);
  const [searchParams] = useSearchParams();

  const load = useCallback(async () => {
    setError('');
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

  const handleCheckout = async () => {
    setCheckoutLoading(true);
    setError('');
    try {
      const { url } = await billingApi.createCheckout();
      window.location.href = url;
    } catch {
      setError('Could not start checkout. Confirm Stripe pricing is configured and try again.');
      setCheckoutLoading(false);
    }
  };

  const handlePortal = async () => {
    setPortalLoading(true);
    setError('');
    try {
      const { url } = await billingApi.createPortal();
      window.location.href = url;
    } catch {
      setError('Could not open the billing portal. Please try again.');
      setPortalLoading(false);
    }
  };

  const successSession = searchParams.get('session') === 'success';

  if (loading) {
    return (
      <div className="page-header">
        <div className="spinner" />
      </div>
    );
  }

  return (
    <div style={{ padding: '32px', maxWidth: 1040 }}>
      <div className="page-header" style={{ marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Billing
          </h1>
          <p style={{ margin: '4px 0 0', color: 'var(--text-secondary)', fontSize: 14 }}>
            Single enterprise plan with a device-based monthly subscription.
          </p>
        </div>
        {plan?.stripeCustomerId ? (
          <button className="btn btn-secondary" onClick={handlePortal} disabled={portalLoading}>
            {portalLoading ? 'Opening...' : 'Manage Billing'}
          </button>
        ) : (
          <button className="btn btn-primary" onClick={handleCheckout} disabled={checkoutLoading}>
            {checkoutLoading ? 'Redirecting...' : 'Activate Billing'}
          </button>
        )}
      </div>

      {successSession && (
        <div className="success-banner" style={{ marginBottom: 24 }}>
          Subscription activated. Billing will update after Stripe confirms the subscription.
        </div>
      )}

      {error && <div className="error-banner" style={{ marginBottom: 24 }}>{error}</div>}

      {plan && (
        <>
          <div className="card" style={{ marginBottom: 24 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 24, flexWrap: 'wrap' }}>
              <div>
                <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-dim)', marginBottom: 6 }}>
                  Current Plan
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                  <span style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>
                    {plan.planName}
                  </span>
                  <span style={{
                    fontSize: 11,
                    fontWeight: 700,
                    padding: '3px 9px',
                    borderRadius: 12,
                    background: `${STATUS_COLOR[plan.billingStatus] ?? 'var(--text-dim)'}1f`,
                    color: STATUS_COLOR[plan.billingStatus] ?? 'var(--text-dim)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.05em',
                  }}>
                    {plan.billingStatus}
                  </span>
                </div>
                <p style={{ margin: '8px 0 0', maxWidth: 560, color: 'var(--text-secondary)', fontSize: 14, lineHeight: 1.55 }}>
                  {money(plan.pricePerDevice)} per active device each month with a {plan.minimumDevices}-device minimum.
                  The 14-day trial starts during checkout.
                </p>
              </div>
              <div style={{ textAlign: 'right' }}>
                <div style={{ color: 'var(--text-dim)', fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
                  Estimated Monthly
                </div>
                <div style={{ color: 'var(--text-primary)', fontSize: 30, fontWeight: 800, marginTop: 4 }}>
                  {money(plan.currentBill)}
                </div>
                <div style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
                  {plan.billableDevices.toLocaleString()} billable devices
                </div>
              </div>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 16, marginBottom: 24 }}>
            <Metric label="Active Devices" value={plan.deviceCount.toLocaleString()} />
            <Metric label="Billing Floor" value={money(plan.monthlyFloor)} />
            <Metric label="Billable Devices" value={plan.billableDevices.toLocaleString()} />
            <Metric label={plan.trialEnd ? 'Trial Ends' : 'Renews'} value={formatDate(plan.trialEnd ?? plan.licenseEnd)} />
          </div>

          {!plan.stripeCustomerId && (
            <div className="card" style={{ marginBottom: 24 }}>
              <h2 style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-primary)', margin: '0 0 8px' }}>
                Activate Billing
              </h2>
              <p style={{ fontSize: 14, color: 'var(--text-secondary)', lineHeight: 1.55, margin: '0 0 18px' }}>
                Start the standard plan with a 14-day trial. Stripe bills the higher of your active device count or the
                100-device minimum.
              </p>
              <button className="btn btn-primary" onClick={handleCheckout} disabled={checkoutLoading}>
                {checkoutLoading ? 'Redirecting...' : 'Start 14-Day Trial'}
              </button>
            </div>
          )}
        </>
      )}

      <section>
        <h2 style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 16 }}>
          Invoice History
        </h2>
        {invoices.length > 0 ? (
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
                    <td>{new Date(inv.periodStart).toLocaleDateString('en-US', { month: 'short', year: 'numeric' })}</td>
                    <td style={{ fontFamily: 'var(--font-mono)' }}>
                      {inv.currency} {inv.amount.toFixed(2)}
                    </td>
                    <td>
                      <span style={{
                        fontSize: 11,
                        fontWeight: 700,
                        padding: '3px 8px',
                        borderRadius: 10,
                        background: inv.status === 'paid' ? 'rgba(34,197,94,0.12)' : 'rgba(203,104,18,0.12)',
                        color: inv.status === 'paid' ? 'var(--status-success)' : 'var(--status-warning)',
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
        ) : (
          <div className="card" style={{ color: 'var(--text-secondary)', fontSize: 14 }}>
            No invoices yet.
          </div>
        )}
      </section>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="card">
      <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-dim)', marginBottom: 8 }}>
        {label}
      </div>
      <div style={{ fontSize: 24, fontWeight: 800, color: 'var(--text-primary)' }}>
        {value}
      </div>
    </div>
  );
}
