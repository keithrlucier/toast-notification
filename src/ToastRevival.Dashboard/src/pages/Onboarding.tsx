import { useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { billingApi } from '../api/billing';
import {
  OnboardingBell,
  OnboardingTemplate,
  OnboardingPackage,
  OnboardingLaunch,
} from '../icons/onboarding';

const STEPS = ['Welcome', 'Billing', 'Install Agent'] as const;
type Step = (typeof STEPS)[number];

export default function Onboarding() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>('Welcome');
  const [tenantId, setTenantId] = useState('');
  const [checkoutLoading, setCheckoutLoading] = useState(false);
  const [checkoutError, setCheckoutError] = useState('');

  useEffect(() => {
    if (user?.tenantId) setTenantId(user.tenantId);
  }, [user]);

  const currentIdx = STEPS.indexOf(step);

  const handleCheckout = async () => {
    setCheckoutLoading(true);
    setCheckoutError('');
    try {
      const { url } = await billingApi.createCheckout();
      window.location.href = url;
    } catch {
      setCheckoutError('Billing is not configured yet. You can continue setup and activate it later from Billing.');
      setCheckoutLoading(false);
    }
  };

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
      padding: 24,
    }}>
      <div style={{ width: '100%', maxWidth: 660 }}>
        <div style={{ display: 'flex', gap: 8, marginBottom: 40, justifyContent: 'center', flexWrap: 'wrap' }}>
          {STEPS.map((s, i) => (
            <div key={s} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <div style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                background: i <= currentIdx ? 'var(--accent)' : 'rgba(15,23,42,0.12)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: i < currentIdx ? 10 : 12,
                fontWeight: 700,
                color: i <= currentIdx ? '#FFFFFF' : 'var(--text-dim)',
                flexShrink: 0,
                transition: 'background 0.2s',
              }}>
                {i < currentIdx ? 'OK' : i + 1}
              </div>
              <span style={{
                fontSize: 13,
                color: i === currentIdx ? 'var(--text-primary)' : 'var(--text-dim)',
                fontWeight: i === currentIdx ? 600 : 400,
              }}>
                {s}
              </span>
              {i < STEPS.length - 1 && (
                <div style={{ width: 32, height: 1, background: 'rgba(15,23,42,0.14)', marginLeft: 4 }} />
              )}
            </div>
          ))}
        </div>

        <div className="card" style={{ padding: 40 }}>
          {step === 'Welcome' && <WelcomeStep onNext={() => setStep('Billing')} />}
          {step === 'Billing' && (
            <BillingStep
              onNext={() => setStep('Install Agent')}
              onCheckout={handleCheckout}
              checkoutLoading={checkoutLoading}
              checkoutError={checkoutError}
            />
          )}
          {step === 'Install Agent' && (
            <InstallStep tenantId={tenantId} onDone={() => navigate('/dashboard')} />
          )}
        </div>
      </div>
    </div>
  );
}

function WelcomeStep({ onNext }: { onNext: () => void }) {
  return (
    <div style={{ textAlign: 'center' }}>
      <div style={{
        width: 64,
        height: 64,
        borderRadius: 16,
        background: 'rgba(31,111,189,0.1)',
        border: '1px solid rgba(31,111,189,0.28)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        margin: '0 auto 24px',
        color: 'var(--accent)',
      }}>
        <OnboardingBell size={32} aria-hidden="true" />
      </div>
      <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 12 }}>
        Toast Notification Console
      </h1>
      <p style={{ fontSize: 15, color: 'var(--text-secondary)', lineHeight: 1.6, marginBottom: 32, maxWidth: 460, margin: '0 auto 32px' }}>
        Configure billing, deploy the Windows agent, and start managing endpoint notifications from one operations console.
      </p>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: 16, marginBottom: 32 }}>
        {([
          { icon: <OnboardingTemplate size={24} aria-hidden="true" />, label: 'Activate billing' },
          { icon: <OnboardingPackage size={24} aria-hidden="true" />, label: 'Install the agent' },
          { icon: <OnboardingLaunch size={24} aria-hidden="true" />, label: 'Send notifications' },
        ] as { icon: ReactNode; label: string }[]).map(({ icon, label }) => (
          <div key={label} style={{
            padding: '16px 8px',
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.08)',
            borderRadius: 8,
            textAlign: 'center',
          }}>
            <div style={{
              marginBottom: 6,
              color: 'var(--accent)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}>
              {icon}
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', fontWeight: 600 }}>{label}</div>
          </div>
        ))}
      </div>
      <button className="btn btn-primary" style={{ minWidth: 180 }} onClick={onNext}>
        Begin Setup
      </button>
    </div>
  );
}

function BillingStep({
  onNext,
  onCheckout,
  checkoutLoading,
  checkoutError,
}: {
  onNext: () => void;
  onCheckout: () => void;
  checkoutLoading: boolean;
  checkoutError: string;
}) {
  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 6 }}>
        Billing & Subscription
      </h2>
      <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 24, lineHeight: 1.55 }}>
        Your first 25 devices are always free — no credit card required. Billing activates automatically
        when you exceed 25 active devices.
      </p>

      <div style={{
        border: '1px solid rgba(15,23,42,0.12)',
        borderRadius: 8,
        background: 'var(--bg-secondary)',
        padding: 24,
        marginBottom: 16,
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 20, flexWrap: 'wrap', marginBottom: 18 }}>
          <div>
            <div style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
              Standard
            </div>
            <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
              Enterprise endpoint notification management
            </div>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div style={{ fontSize: 24, fontWeight: 800, color: 'var(--text-primary)' }}>$0.22</div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>per active device / month</div>
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 12 }}>
          <PlanFact label="Free tier" value="1–25 devices" />
          <PlanFact label="Standard rate" value="$0.22 / device" />
          <PlanFact label="Billing starts" value="At device 26+" />
        </div>
      </div>

      <p style={{ fontSize: 13, color: 'var(--text-dim)', marginBottom: 20, lineHeight: 1.5 }}>
        Stripe billing is only required when you exceed 25 devices. You can configure it now or any time
        from the Billing page.
      </p>

      {checkoutError && <div className="error-banner" style={{ marginBottom: 16 }}>{checkoutError}</div>}

      <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
        <button className="btn btn-ghost" onClick={onCheckout} disabled={checkoutLoading}>
          {checkoutLoading ? 'Redirecting...' : 'Set Up Billing'}
        </button>
        <button className="btn btn-primary" onClick={onNext}>
          Continue
        </button>
      </div>
    </div>
  );
}

function PlanFact({ label, value }: { label: string; value: string }) {
  return (
    <div style={{
      border: '1px solid rgba(15,23,42,0.08)',
      borderRadius: 8,
      padding: '12px 14px',
      background: '#FFFFFF',
    }}>
      <div style={{ fontSize: 11, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 700 }}>
        {label}
      </div>
      <div style={{ fontSize: 16, color: 'var(--text-primary)', fontWeight: 700, marginTop: 4 }}>
        {value}
      </div>
    </div>
  );
}

function InstallStep({ tenantId, onDone }: { tenantId: string; onDone: () => void }) {
  const serverUrl = window.location.origin.includes('localhost')
    ? 'https://api.toastnotification.com'
    : window.location.origin;

  const msiCommand =
    `msiexec /i ToastNotification.msi /qn CLIENTID=${tenantId || '<your-tenant-id>'} SERVERURL=${serverUrl}`;

  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(msiCommand);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 6 }}>
        Install Your First Agent
      </h2>
      <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 24, lineHeight: 1.55 }}>
        Deploy the MSI to Windows endpoints with your RMM tool or Microsoft Intune. The agent registers itself on first launch.
      </p>

      <div style={{ marginBottom: 24 }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 8 }}>
          Silent MSI Install Command
        </div>
        <div style={{
          position: 'relative',
          background: 'var(--bg-secondary)',
          border: '1px solid rgba(15,23,42,0.12)',
          borderRadius: 8,
          padding: '14px 16px',
        }}>
          <code style={{
            display: 'block',
            fontFamily: 'var(--font-mono)',
            fontSize: 12,
            color: 'var(--accent)',
            wordBreak: 'break-all',
            paddingRight: 80,
          }}>
            {msiCommand}
          </code>
          <button
            className="btn btn-ghost"
            style={{ position: 'absolute', top: 10, right: 10, fontSize: 12, padding: '4px 10px' }}
            onClick={handleCopy}
          >
            {copied ? 'Copied' : 'Copy'}
          </button>
        </div>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 32 }}>
        {[
          { label: 'Tenant ID (CLIENTID)', value: tenantId || 'Loading...' },
          { label: 'API Server (SERVERURL)', value: serverUrl },
        ].map(({ label, value }) => (
          <div key={label} style={{
            display: 'flex',
            justifyContent: 'space-between',
            padding: '10px 14px',
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.08)',
            borderRadius: 6,
            gap: 16,
          }}>
            <span style={{ fontSize: 13, color: 'var(--text-secondary)', flexShrink: 0 }}>{label}</span>
            <code style={{ fontSize: 12, color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', wordBreak: 'break-all', textAlign: 'right' }}>
              {value}
            </code>
          </div>
        ))}
      </div>

      <div style={{
        background: 'rgba(31,111,189,0.08)',
        border: '1px solid rgba(31,111,189,0.2)',
        borderRadius: 8,
        padding: '12px 16px',
        fontSize: 13,
        color: '#14508C',
        marginBottom: 32,
      }}>
        Download the MSI installer from account settings. Agents appear in Devices within seconds of first launch.
      </div>

      <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
        <button className="btn btn-secondary" onClick={() => window.open('/devices', '_blank')}>
          View Devices
        </button>
        <button className="btn btn-primary" onClick={onDone}>
          Go to Dashboard
        </button>
      </div>
    </div>
  );
}
