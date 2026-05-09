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

const STEPS = ['Welcome', 'Choose Plan', 'Install Agent'] as const;
type Step = (typeof STEPS)[number];

export default function Onboarding() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>('Welcome');
  const [tenantId, setTenantId] = useState('');
  const [checkoutLoading, setCheckoutLoading] = useState<string | null>(null);

  useEffect(() => {
    if (user?.tenantId) setTenantId(user.tenantId);
  }, [user]);

  const currentIdx = STEPS.indexOf(step);

  const handleCheckout = async (tier: 'Pro' | 'Enterprise') => {
    setCheckoutLoading(tier);
    try {
      const { url } = await billingApi.createCheckout(tier);
      window.location.href = url;
    } catch {
      setCheckoutLoading(null);
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
      <div style={{ width: '100%', maxWidth: 580 }}>
        {/* Progress */}
        <div style={{ display: 'flex', gap: 8, marginBottom: 40, justifyContent: 'center' }}>
          {STEPS.map((s, i) => (
            <div key={s} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <div style={{
                width: 28, height: 28, borderRadius: '50%',
                background: i <= currentIdx ? 'var(--accent)' : 'rgba(255,255,255,0.08)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, fontWeight: 700,
                color: i <= currentIdx ? '#0F1117' : 'var(--text-dim)',
                flexShrink: 0,
                transition: 'background 0.2s',
              }}>
                {i < currentIdx ? '✓' : i + 1}
              </div>
              <span style={{
                fontSize: 13,
                color: i === currentIdx ? 'var(--text-primary)' : 'var(--text-dim)',
                fontWeight: i === currentIdx ? 600 : 400,
              }}>
                {s}
              </span>
              {i < STEPS.length - 1 && (
                <div style={{ width: 32, height: 1, background: 'rgba(255,255,255,0.1)', marginLeft: 4 }} />
              )}
            </div>
          ))}
        </div>

        <div className="card" style={{ padding: 40 }}>
          {step === 'Welcome' && <WelcomeStep onNext={() => setStep('Choose Plan')} />}
          {step === 'Choose Plan' && (
            <PlanStep
              onNext={() => setStep('Install Agent')}
              onCheckout={handleCheckout}
              checkoutLoading={checkoutLoading}
            />
          )}
          {step === 'Install Agent' && (
            <InstallStep tenantId={tenantId} onDone={() => navigate('/')} />
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
        width: 64, height: 64, borderRadius: 16,
        background: 'rgba(0,201,167,0.1)',
        border: '1px solid rgba(0,201,167,0.3)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        margin: '0 auto 24px',
        color: 'var(--accent)',
      }}>
        <OnboardingBell size={32} aria-hidden="true" />
      </div>
      <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 12 }}>
        Welcome to Toast Notification
      </h1>
      <p style={{ fontSize: 15, color: 'var(--text-secondary)', lineHeight: 1.6, marginBottom: 32, maxWidth: 420, margin: '0 auto 32px' }}>
        You're set up and ready to send managed Windows toast notifications to your endpoints.
        Let's get your first agent deployed in three steps.
      </p>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 16, marginBottom: 32 }}>
        {([
          { icon: <OnboardingTemplate size={24} aria-hidden="true" />, label: 'Choose a plan' },
          { icon: <OnboardingPackage size={24} aria-hidden="true" />, label: 'Install the agent' },
          { icon: <OnboardingLaunch size={24} aria-hidden="true" />, label: 'Send notifications' },
        ] as { icon: ReactNode; label: string }[]).map(({ icon, label }) => (
          <div key={label} style={{
            padding: '16px 8px',
            background: 'var(--bg-secondary)',
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
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', fontWeight: 500 }}>{label}</div>
          </div>
        ))}
      </div>
      <button className="btn btn-primary" style={{ minWidth: 180 }} onClick={onNext}>
        Get Started
      </button>
    </div>
  );
}

function PlanStep({
  onNext,
  onCheckout,
  checkoutLoading,
}: {
  onNext: () => void;
  onCheckout: (tier: 'Pro' | 'Enterprise') => void;
  checkoutLoading: string | null;
}) {
  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 6 }}>
        Choose Your Plan
      </h2>
      <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 24 }}>
        You can change plans at any time from your Billing settings.
      </p>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginBottom: 24 }}>
        <PlanCard
          name="Free"
          price="Free"
          limit="10 devices"
          features={['All notification templates', 'Content moderation', 'Delivery analytics', 'Community support']}
          color="var(--text-secondary)"
          ctaLabel="Continue with Free"
          onSelect={onNext}
        />
        <PlanCard
          name="Pro"
          price="$3/device/mo"
          limit="250 devices"
          features={['Everything in Free', 'Priority support', 'Advanced analytics', 'API key access']}
          color="var(--accent)"
          ctaLabel={checkoutLoading === 'Pro' ? 'Redirecting...' : 'Upgrade to Pro'}
          onSelect={() => onCheckout('Pro')}
          disabled={checkoutLoading !== null}
          highlighted
        />
        <PlanCard
          name="Enterprise"
          price="Custom pricing"
          limit="Unlimited devices"
          features={['Everything in Pro', 'Dedicated support SLA', 'Custom deployment', 'Volume pricing']}
          color="#60A5FA"
          ctaLabel={checkoutLoading === 'Enterprise' ? 'Redirecting...' : 'Contact Sales'}
          onSelect={() => onCheckout('Enterprise')}
          disabled={checkoutLoading !== null}
        />
      </div>
    </div>
  );
}

function PlanCard({
  name, price, limit, features, color, ctaLabel, onSelect, disabled, highlighted,
}: {
  name: string; price: string; limit: string; features: string[];
  color: string; ctaLabel: string; onSelect: () => void;
  disabled?: boolean; highlighted?: boolean;
}) {
  return (
    <div style={{
      padding: '20px 24px',
      borderRadius: 8,
      border: highlighted ? `1.5px solid ${color}` : '1px solid rgba(255,255,255,0.08)',
      background: highlighted ? `${color}0a` : 'var(--bg-secondary)',
      display: 'flex',
      alignItems: 'center',
      gap: 24,
    }}>
      <div style={{ flex: 1 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 4 }}>
          <span style={{ fontSize: 16, fontWeight: 700, color }}>{name}</span>
          <span style={{ fontSize: 14, color: 'var(--text-secondary)' }}>{price}</span>
          <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>· {limit}</span>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 16px' }}>
          {features.map(f => (
            <span key={f} style={{ fontSize: 12, color: 'var(--text-dim)' }}>✓ {f}</span>
          ))}
        </div>
      </div>
      <button
        className={highlighted ? 'btn btn-primary' : 'btn btn-secondary'}
        style={{ flexShrink: 0, minWidth: 140 }}
        onClick={onSelect}
        disabled={disabled}
      >
        {ctaLabel}
      </button>
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
      <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 24 }}>
        Deploy the MSI to your Windows endpoints via your RMM tool (NinjaOne, Datto, ConnectWise, Intune).
        The agent registers itself automatically on first launch.
      </p>

      <div style={{ marginBottom: 24 }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 8 }}>
          Silent MSI Install Command
        </div>
        <div style={{
          position: 'relative',
          background: 'var(--bg-secondary)',
          border: '1px solid rgba(255,255,255,0.08)',
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
            {copied ? 'Copied!' : 'Copy'}
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
        background: 'rgba(96,165,250,0.08)',
        border: '1px solid rgba(96,165,250,0.2)',
        borderRadius: 8,
        padding: '12px 16px',
        fontSize: 13,
        color: '#93C5FD',
        marginBottom: 32,
      }}>
        Download the MSI installer from your account settings. Agents appear in the Devices page within
        seconds of first launch.
      </div>

      <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end' }}>
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
