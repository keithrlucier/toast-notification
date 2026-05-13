import { useState, useEffect, useCallback, FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { billingApi, type BillingPlan, type Invoice } from '../api/billing';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

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

interface StripeSnapshot {
  hasSecretKey: boolean;
  hasWebhookSecret: boolean;
  maskedSecretKey: string | null;
  maskedWebhookSecret: string | null;
  perDevicePriceId: string;
  isConfigured: boolean;
}

interface MessagingSnapshot {
  hasClickSendUsername: boolean;
  hasClickSendApiKey: boolean;
  hasMailjetApiKey: boolean;
  hasMailjetApiSecret: boolean;
  hasMailjetSenderEmail: boolean;
  maskedClickSendUsername: string | null;
  maskedClickSendApiKey: string | null;
  maskedMailjetApiKey: string | null;
  maskedMailjetApiSecret: string | null;
  mailjetSenderEmail: string | null;
}

export default function Billing() {
  const { user } = useAuth();
  const isPlatformAdmin = user?.isPlatformAdmin ?? false;

  const [plan, setPlan] = useState<BillingPlan | null>(null);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [checkoutLoading, setCheckoutLoading] = useState(false);
  const [portalLoading, setPortalLoading] = useState(false);
  const [searchParams] = useSearchParams();

  // Stripe config state
  const [stripeSnap, setStripeSnap]   = useState<StripeSnapshot | null>(null);
  const [secretKey, setSecretKey]     = useState('');
  const [webhookSec, setWebhookSec]   = useState('');
  const [priceId, setPriceId]         = useState('');
  const [saveLoading, setSaveLoading] = useState(false);
  const [saveError, setSaveError]     = useState('');
  const [saveOk, setSaveOk]           = useState(false);

  // Messaging config state
  const [msgSnap, setMsgSnap]               = useState<MessagingSnapshot | null>(null);
  const [csUsername, setCsUsername]         = useState('');
  const [csApiKey, setCsApiKey]             = useState('');
  const [mjApiKey, setMjApiKey]             = useState('');
  const [mjApiSecret, setMjApiSecret]       = useState('');
  const [mjSenderEmail, setMjSenderEmail]   = useState('');
  const [msgSaveLoading, setMsgSaveLoading] = useState(false);
  const [msgSaveError, setMsgSaveError]     = useState('');
  const [msgSaveOk, setMsgSaveOk]           = useState(false);

  const load = useCallback(async () => {
    setError('');
    try {
      const calls: Promise<unknown>[] = [billingApi.getPlan(), billingApi.getInvoices()];
      if (isPlatformAdmin) {
        calls.push(api.get<StripeSnapshot>('/api/billing/admin/stripe-config'));
        calls.push(api.get<MessagingSnapshot>('/api/system/messaging/config'));
      }
      const [p, inv, snap, msg] = await Promise.all(calls);
      setPlan(p as BillingPlan);
      setInvoices((inv as { invoices: Invoice[] }).invoices);
      if (snap) setStripeSnap(snap as StripeSnapshot);
      if (msg) setMsgSnap(msg as MessagingSnapshot);
    } catch {
      setError('Failed to load billing information.');
    } finally {
      setLoading(false);
    }
  }, [isPlatformAdmin]);

  useEffect(() => { load(); }, [load]);

  const handleCheckout = async () => {
    setCheckoutLoading(true);
    setError('');
    try {
      const { url } = await billingApi.createCheckout();
      window.location.href = url;
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not start checkout. Confirm Stripe pricing is configured and try again.');
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

  const handleStripeConfigSave = async (e: FormEvent) => {
    e.preventDefault();
    setSaveError('');
    setSaveOk(false);
    setSaveLoading(true);
    try {
      const snap = await api.post<StripeSnapshot>('/api/billing/admin/stripe-config', {
        secretKey:       secretKey       || null,
        webhookSecret:   webhookSec      || null,
        perDevicePriceId: priceId        || null,
      });
      setStripeSnap(snap);
      setSecretKey('');
      setWebhookSec('');
      setPriceId('');
      setSaveOk(true);
      setTimeout(() => setSaveOk(false), 3000);
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : 'Save failed.');
    } finally {
      setSaveLoading(false);
    }
  };

  const handleMessagingConfigSave = async (e: FormEvent) => {
    e.preventDefault();
    setMsgSaveError('');
    setMsgSaveOk(false);
    setMsgSaveLoading(true);
    try {
      const snap = await api.post<MessagingSnapshot>('/api/system/messaging/config', {
        clickSendUsername:  csUsername   || null,
        clickSendApiKey:    csApiKey     || null,
        mailjetApiKey:      mjApiKey     || null,
        mailjetApiSecret:   mjApiSecret  || null,
        mailjetSenderEmail: mjSenderEmail || null,
      });
      setMsgSnap(snap);
      setCsUsername(''); setCsApiKey(''); setMjApiKey(''); setMjApiSecret(''); setMjSenderEmail('');
      setMsgSaveOk(true);
      setTimeout(() => setMsgSaveOk(false), 3000);
    } catch (err) {
      setMsgSaveError(err instanceof ApiError ? err.message : 'Save failed.');
    } finally {
      setMsgSaveLoading(false);
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
                  {money(plan.pricePerDevice)} per active device per month.
                  Reviewed trial and billing status are shown here. Paid service uses Stripe billing.
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
            <Metric label="Free Tier" value={`${plan.freeTierLimit} devices`} />
            <Metric label="Billable Devices" value={plan.billableDevices.toLocaleString()} />
            <Metric label={plan.trialEnd ? 'Trial Ends' : 'Renews'} value={formatDate(plan.trialEnd ?? plan.licenseEnd)} />
          </div>

          {!plan.stripeCustomerId && (
            <div className="card" style={{ marginBottom: 24 }}>
              <h2 style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-primary)', margin: '0 0 8px' }}>
                Activate Billing
              </h2>
              <p style={{ fontSize: 14, color: 'var(--text-secondary)', lineHeight: 1.55, margin: '0 0 18px' }}>
                Activate the standard plan in Stripe when the tenant is ready for paid service.
              </p>
              <button className="btn btn-primary" onClick={handleCheckout} disabled={checkoutLoading}>
                {checkoutLoading ? 'Redirecting...' : 'Activate Billing'}
              </button>
            </div>
          )}
        </>
      )}

      {/* Stripe Configuration — platform admin only */}
      {isPlatformAdmin && (
        <section style={{ marginBottom: 32 }}>
          <h2 style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 16 }}>
            Stripe Configuration
          </h2>
          <div className="card">
            {/* Status row */}
            <div style={{ display: 'flex', gap: 24, marginBottom: 24, flexWrap: 'wrap' }}>
              {[
                { label: 'Secret key',      ok: stripeSnap?.hasSecretKey,      masked: stripeSnap?.maskedSecretKey },
                { label: 'Webhook secret',  ok: stripeSnap?.hasWebhookSecret,  masked: stripeSnap?.maskedWebhookSecret },
                { label: 'Price ID',        ok: stripeSnap?.isConfigured,      masked: stripeSnap?.perDevicePriceId || null },
              ].map(({ label, ok, masked }) => (
                <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    width: 8, height: 8, borderRadius: '50%',
                    background: ok ? 'var(--status-success)' : 'var(--status-error)',
                    flexShrink: 0,
                  }} />
                  <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{label}</span>
                  {masked && (
                    <code style={{ fontSize: 11, fontFamily: 'var(--font-mono)', color: 'var(--text-dim)' }}>
                      {masked}
                    </code>
                  )}
                </div>
              ))}
            </div>

            <form onSubmit={handleStripeConfigSave}>
              {saveError && <div className="error-banner" style={{ marginBottom: 16 }}>{saveError}</div>}
              {saveOk    && <div className="success-banner" style={{ marginBottom: 16 }}>Saved. API reloaded.</div>}
              <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16 }}>
                Leave a field blank to keep the current value. Changes take effect immediately without a server restart.
              </p>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
                <div className="field">
                  <label>Secret key <span style={{ color: 'var(--text-dim)', fontWeight: 400 }}>(sk_live_...)</span></label>
                  <input
                    type="password"
                    autoComplete="off"
                    value={secretKey}
                    onChange={e => setSecretKey(e.target.value)}
                    placeholder={stripeSnap?.hasSecretKey ? stripeSnap.maskedSecretKey ?? 'Set' : 'Not configured'}
                  />
                </div>
                <div className="field">
                  <label>Webhook secret <span style={{ color: 'var(--text-dim)', fontWeight: 400 }}>(whsec_...)</span></label>
                  <input
                    type="password"
                    autoComplete="off"
                    value={webhookSec}
                    onChange={e => setWebhookSec(e.target.value)}
                    placeholder={stripeSnap?.hasWebhookSecret ? stripeSnap.maskedWebhookSecret ?? 'Set' : 'Not configured'}
                  />
                </div>
              </div>
              <div className="field" style={{ marginBottom: 16, maxWidth: 420 }}>
                <label>Per-device price ID <span style={{ color: 'var(--text-dim)', fontWeight: 400 }}>(price_...)</span></label>
                <input
                  type="text"
                  value={priceId}
                  onChange={e => setPriceId(e.target.value)}
                  placeholder={stripeSnap?.isConfigured ? stripeSnap.perDevicePriceId : 'Not configured'}
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={saveLoading || (!secretKey && !webhookSec && !priceId)}
                style={{ minWidth: 120 }}
              >
                {saveLoading ? 'Saving...' : 'Save configuration'}
              </button>
            </form>
          </div>
        </section>
      )}

      {/* Messaging Configuration — platform admin only */}
      {isPlatformAdmin && (
        <section style={{ marginBottom: 32 }}>
          <h2 style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 16 }}>
            Messaging Configuration
          </h2>
          <div className="card">
            {/* Status row */}
            <div style={{ display: 'flex', gap: 24, marginBottom: 24, flexWrap: 'wrap' }}>
              {[
                { label: 'ClickSend username', ok: msgSnap?.hasClickSendUsername, masked: msgSnap?.maskedClickSendUsername },
                { label: 'ClickSend API key',  ok: msgSnap?.hasClickSendApiKey,   masked: msgSnap?.maskedClickSendApiKey },
                { label: 'Mailjet API key',    ok: msgSnap?.hasMailjetApiKey,     masked: msgSnap?.maskedMailjetApiKey },
                { label: 'Mailjet API secret', ok: msgSnap?.hasMailjetApiSecret,  masked: msgSnap?.maskedMailjetApiSecret },
                { label: 'Sender email',       ok: msgSnap?.hasMailjetSenderEmail, masked: msgSnap?.mailjetSenderEmail },
              ].map(({ label, ok, masked }) => (
                <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    width: 8, height: 8, borderRadius: '50%',
                    background: ok ? 'var(--status-success)' : 'var(--status-error)',
                    flexShrink: 0,
                  }} />
                  <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{label}</span>
                  {masked && (
                    <code style={{ fontSize: 11, fontFamily: 'var(--font-mono)', color: 'var(--text-dim)' }}>
                      {masked}
                    </code>
                  )}
                </div>
              ))}
            </div>

            <form onSubmit={handleMessagingConfigSave}>
              {msgSaveError && <div className="error-banner" style={{ marginBottom: 16 }}>{msgSaveError}</div>}
              {msgSaveOk    && <div className="success-banner" style={{ marginBottom: 16 }}>Saved. Config reloaded.</div>}
              <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16 }}>
                Leave a field blank to keep the current value. Changes take effect immediately without a server restart.
              </p>

              <p style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-dim)', marginBottom: 10 }}>
                ClickSend (SMS)
              </p>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 20 }}>
                <div className="field">
                  <label>Username</label>
                  <input
                    type="text"
                    autoComplete="off"
                    value={csUsername}
                    onChange={e => setCsUsername(e.target.value)}
                    placeholder={msgSnap?.hasClickSendUsername ? msgSnap.maskedClickSendUsername ?? 'Set' : 'Not configured'}
                  />
                </div>
                <div className="field">
                  <label>API key</label>
                  <input
                    type="password"
                    autoComplete="off"
                    value={csApiKey}
                    onChange={e => setCsApiKey(e.target.value)}
                    placeholder={msgSnap?.hasClickSendApiKey ? msgSnap.maskedClickSendApiKey ?? 'Set' : 'Not configured'}
                  />
                </div>
              </div>

              <p style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-dim)', marginBottom: 10 }}>
                Mailjet (Email)
              </p>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
                <div className="field">
                  <label>API key</label>
                  <input
                    type="password"
                    autoComplete="off"
                    value={mjApiKey}
                    onChange={e => setMjApiKey(e.target.value)}
                    placeholder={msgSnap?.hasMailjetApiKey ? msgSnap.maskedMailjetApiKey ?? 'Set' : 'Not configured'}
                  />
                </div>
                <div className="field">
                  <label>API secret</label>
                  <input
                    type="password"
                    autoComplete="off"
                    value={mjApiSecret}
                    onChange={e => setMjApiSecret(e.target.value)}
                    placeholder={msgSnap?.hasMailjetApiSecret ? msgSnap.maskedMailjetApiSecret ?? 'Set' : 'Not configured'}
                  />
                </div>
              </div>
              <div className="field" style={{ marginBottom: 16, maxWidth: 360 }}>
                <label>Sender email</label>
                <input
                  type="email"
                  autoComplete="off"
                  value={mjSenderEmail}
                  onChange={e => setMjSenderEmail(e.target.value)}
                  placeholder={msgSnap?.mailjetSenderEmail ?? 'Not configured'}
                />
              </div>

              <button
                type="submit"
                className="btn btn-primary"
                disabled={msgSaveLoading || (!csUsername && !csApiKey && !mjApiKey && !mjApiSecret && !mjSenderEmail)}
                style={{ minWidth: 120 }}
              >
                {msgSaveLoading ? 'Saving...' : 'Save configuration'}
              </button>
            </form>
          </div>
        </section>
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
