import { FormEvent, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';

type UseCase =
  | 'MspClientCommunication'
  | 'InternalItOperations'
  | 'SecurityIncidentResponse'
  | 'MaintenanceWindowNotices'
  | 'ComplianceAuditEvidence'
  | 'ProductEvaluation'
  | 'Other';

interface RegistrationConfig {
  turnstileEnabled: boolean;
  turnstileSiteKey: string | null;
}

interface TrialRegistrationResponse {
  requestId: string;
  step: 'pending_review';
  message: string;
}

declare global {
  interface Window {
    turnstile?: {
      render: (el: HTMLElement, options: Record<string, unknown>) => string;
      reset: (widgetId?: string) => void;
      remove: (widgetId?: string) => void;
    };
  }
}

const USE_CASES: Array<{ value: UseCase; label: string }> = [
  { value: 'MspClientCommunication', label: 'MSP client communication' },
  { value: 'InternalItOperations', label: 'Internal IT operations' },
  { value: 'SecurityIncidentResponse', label: 'Security incident response' },
  { value: 'MaintenanceWindowNotices', label: 'Maintenance windows and outages' },
  { value: 'ComplianceAuditEvidence', label: 'Audit evidence and compliance' },
  { value: 'ProductEvaluation', label: 'Product evaluation' },
  { value: 'Other', label: 'Other' },
];

export default function Register() {
  const [companyName, setCompanyName] = useState('');
  const [website, setWebsite] = useState('');
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [jobTitle, setJobTitle] = useState('');
  const [intendedUseCase, setIntendedUseCase] = useState<UseCase>('MspClientCommunication');
  const [intendedUseCaseDetails, setIntendedUseCaseDetails] = useState('');
  const [turnstileToken, setTurnstileToken] = useState('');

  const [config, setConfig] = useState<RegistrationConfig | null>(null);
  const [submittedMessage, setSubmittedMessage] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    api.get<RegistrationConfig>('/api/auth/register/config')
      .then(res => { if (!cancelled) setConfig(res); })
      .catch(() => { if (!cancelled) setConfig({ turnstileEnabled: false, turnstileSiteKey: null }); });
    return () => { cancelled = true; };
  }, []);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    if (config?.turnstileEnabled && !turnstileToken) {
      setError('Complete the human verification challenge before submitting.');
      return;
    }

    setLoading(true);
    try {
      const res = await api.post<TrialRegistrationResponse>('/api/auth/register/init', {
        companyName,
        website,
        fullName,
        email,
        phone,
        jobTitle,
        intendedUseCase,
        intendedUseCaseDetails: intendedUseCaseDetails.trim() || null,
        turnstileToken: turnstileToken || null,
      });
      setSubmittedMessage(res.message);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Trial request failed. Please try again.');
      window.turnstile?.reset();
      setTurnstileToken('');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
      padding: 16,
    }}>
      <div style={{ width: '100%', maxWidth: 720 }}>
        <div style={{ textAlign: 'center', marginBottom: 28 }}>
          <h1 style={{ fontSize: 26, fontWeight: 700, color: 'var(--text-primary)' }}>Request trial access</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>
            Tell us who will operate the tenant and how Toast Notification will be used.
          </p>
        </div>

        <div className="card">
          {submittedMessage ? (
            <div>
              <div className="success-banner" style={{ marginBottom: 16 }}>{submittedMessage}</div>
              <p style={{ color: 'var(--text-secondary)', fontSize: 14, lineHeight: 1.6, marginBottom: 20 }}>
                We review trial requests before creating tenants. If approved, you will receive a password setup email
                with access to the dashboard, MSI download, tenant ID, and enrollment key.
              </p>
              <Link to="/login" className="btn btn-primary" style={{ textDecoration: 'none' }}>
                Return to sign in
              </Link>
            </div>
          ) : (
            <form onSubmit={handleSubmit}>
              {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 }}>
                <div className="field">
                  <label htmlFor="companyName">Company name</label>
                  <input
                    id="companyName"
                    type="text"
                    required
                    autoFocus
                    autoComplete="organization"
                    value={companyName}
                    onChange={e => setCompanyName(e.target.value)}
                    placeholder="Acme IT Services"
                  />
                </div>

                <div className="field">
                  <label htmlFor="website">Company website</label>
                  <input
                    id="website"
                    type="text"
                    required
                    autoComplete="url"
                    value={website}
                    onChange={e => setWebsite(e.target.value)}
                    placeholder="https://example.com"
                  />
                </div>

                <div className="field">
                  <label htmlFor="fullName">Your full name</label>
                  <input
                    id="fullName"
                    type="text"
                    required
                    autoComplete="name"
                    value={fullName}
                    onChange={e => setFullName(e.target.value)}
                    placeholder="Jane Smith"
                  />
                </div>

                <div className="field">
                  <label htmlFor="jobTitle">Job title</label>
                  <input
                    id="jobTitle"
                    type="text"
                    required
                    autoComplete="organization-title"
                    value={jobTitle}
                    onChange={e => setJobTitle(e.target.value)}
                    placeholder="Service Desk Manager"
                  />
                </div>

                <div className="field">
                  <label htmlFor="email">Work email</label>
                  <input
                    id="email"
                    type="email"
                    required
                    autoComplete="email"
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                    placeholder="admin@company.com"
                  />
                </div>

                <div className="field">
                  <label htmlFor="phone">Contact telephone</label>
                  <input
                    id="phone"
                    type="tel"
                    required
                    autoComplete="tel"
                    value={phone}
                    onChange={e => setPhone(e.target.value)}
                    placeholder="+1 555 000 0000"
                  />
                </div>
              </div>

              <div className="field" style={{ marginTop: 16 }}>
                <label htmlFor="intendedUseCase">Intended use case</label>
                <select
                  id="intendedUseCase"
                  required
                  value={intendedUseCase}
                  onChange={e => setIntendedUseCase(e.target.value as UseCase)}
                >
                  {USE_CASES.map(option => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </div>

              <div className="field" style={{ marginTop: 16 }}>
                <label htmlFor="intendedUseCaseDetails">Use case notes</label>
                <textarea
                  id="intendedUseCaseDetails"
                  value={intendedUseCaseDetails}
                  onChange={e => setIntendedUseCaseDetails(e.target.value)}
                  rows={4}
                  maxLength={2000}
                  placeholder="Example: We need tenant-branded maintenance notifications with acknowledgement tracking for roughly 150 managed Windows endpoints."
                />
              </div>

              {config?.turnstileEnabled && config.turnstileSiteKey && (
                <div style={{ marginTop: 18 }}>
                  <TurnstileWidget
                    siteKey={config.turnstileSiteKey}
                    onVerify={setTurnstileToken}
                    onExpired={() => setTurnstileToken('')}
                    onError={() => {
                      setTurnstileToken('');
                      setError('Human verification failed. Reload the challenge and try again.');
                    }}
                  />
                </div>
              )}

              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%', justifyContent: 'center', padding: '12px 16px', marginTop: 24 }}
                disabled={loading || config === null}
              >
                {loading ? <span className="spinner" /> : 'Submit for review'}
              </button>
            </form>
          )}
        </div>

        <p style={{ textAlign: 'center', marginTop: 20, color: 'var(--text-dim)', fontSize: 13 }}>
          Already approved?{' '}
          <Link to="/login" style={{ color: 'var(--accent)', textDecoration: 'none', fontWeight: 500 }}>
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
}

function TurnstileWidget({
  siteKey,
  onVerify,
  onExpired,
  onError,
}: {
  siteKey: string;
  onVerify: (token: string) => void;
  onExpired: () => void;
  onError: () => void;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const widgetIdRef = useRef<string | null>(null);
  const verifyRef = useRef(onVerify);
  const expiredRef = useRef(onExpired);
  const errorRef = useRef(onError);

  useEffect(() => {
    verifyRef.current = onVerify;
    expiredRef.current = onExpired;
    errorRef.current = onError;
  }, [onVerify, onExpired, onError]);

  useEffect(() => {
    let cancelled = false;

    const renderWidget = () => {
      if (cancelled || !containerRef.current || !window.turnstile || widgetIdRef.current) return;
      widgetIdRef.current = window.turnstile.render(containerRef.current, {
        sitekey: siteKey,
        action: 'trial_register',
        callback: (token: string) => verifyRef.current(token),
        'expired-callback': () => expiredRef.current(),
        'error-callback': () => errorRef.current(),
      });
    };

    const existing = document.querySelector<HTMLScriptElement>('script[data-turnstile-script="true"]');
    if (existing) {
      if (window.turnstile) renderWidget();
      else existing.addEventListener('load', renderWidget, { once: true });
    } else {
      const script = document.createElement('script');
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
      script.async = true;
      script.defer = true;
      script.dataset.turnstileScript = 'true';
      script.addEventListener('load', renderWidget, { once: true });
      document.head.appendChild(script);
    }

    return () => {
      cancelled = true;
      if (widgetIdRef.current) {
        window.turnstile?.remove(widgetIdRef.current);
        widgetIdRef.current = null;
      }
    };
  }, [siteKey]);

  return <div ref={containerRef} />;
}
