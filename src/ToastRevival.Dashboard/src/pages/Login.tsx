import { FormEvent, useState, useRef, useEffect } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { api, ApiError, AUTH_MESSAGE_STORAGE_KEY } from '../api/client';
import { authApi } from '../api/auth';

function takeAuthMessage(): string {
  const message = sessionStorage.getItem(AUTH_MESSAGE_STORAGE_KEY) ?? '';
  if (message) sessionStorage.removeItem(AUTH_MESSAGE_STORAGE_KEY);
  return message;
}

// Maps the opaque ?sso_error= codes the SSO callback redirects with to friendly,
// non-enumerating messages. Anything unmapped falls back to the generic line.
const SSO_ERRORS: Record<string, string> = {
  not_enabled:  'Microsoft sign-in isn’t enabled for your organization. Contact your administrator.',
  no_account:   'No account here matches that Microsoft user. Ask your administrator to invite you first.',
  mfa_required: 'Your organization requires multi-factor authentication to sign in.',
  suspended:    'Your organization’s access is suspended. Contact support.',
  incomplete:   'Your account registration isn’t finished yet.',
  link_conflict: 'That Microsoft identity is already linked to another account.',
  denied:       'Microsoft sign-in was cancelled.',
  unavailable:  'Microsoft sign-in isn’t available right now.',
};

function ssoErrorMessage(code: string | null): string {
  if (!code) return '';
  return SSO_ERRORS[code] ?? 'Microsoft sign-in could not be completed. Please try again.';
}

// Official Microsoft sign-in mark — the four-color square. Per Microsoft brand
// guidelines: their logo + "Sign in with Microsoft", never a recolored knockoff.
const MicrosoftLogo = (
  <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true" style={{ flexShrink: 0 }}>
    <rect x="1" y="1" width="9" height="9" fill="#F25022" />
    <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
    <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
    <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
  </svg>
);

const LOGO = (
  <div style={{
    width: 48, height: 48, borderRadius: 10,
    background: 'var(--accent)',
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    marginBottom: 16,
  }}>
    <svg width="28" height="28" viewBox="0 0 28 28" fill="none">
      <rect x="2" y="7" width="24" height="15" rx="2.5" fill="#0F1117" />
      <rect x="3.5" y="8.5" width="21" height="12" rx="1.5" fill="white" fillOpacity="0.12" />
      <rect x="5" y="11" width="12" height="2" rx="1" fill="white" fillOpacity="0.9" />
      <rect x="5" y="14.5" width="9" height="1.5" rx="0.75" fill="white" fillOpacity="0.5" />
      <rect x="5" y="17.5" width="6" height="1" rx="0.5" fill="white" fillOpacity="0.35" />
    </svg>
  </div>
);

export default function Login() {
  const { setSession } = useAuth();
  const navigate  = useNavigate();
  const location  = useLocation();
  const from      = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/';

  // Step 1 state
  const [email,    setEmail]    = useState('');
  const [password, setPassword] = useState('');
  const [error,    setError]    = useState(() => ssoErrorMessage(new URLSearchParams(window.location.search).get('sso_error')) || takeAuthMessage());
  const [loading,  setLoading]  = useState(false);
  const [ssoEnabled, setSsoEnabled] = useState(false);

  // Step 2 state (SMS or authenticator challenge)
  const [step,        setStep]        = useState<'password' | 'sms' | 'totp'>('password');
  const [pendingId,   setPendingId]   = useState('');
  const [maskedPhone, setMaskedPhone] = useState('');
  const [code,        setCode]        = useState('');
  const [resendCooldown, setResendCooldown] = useState(0);
  const cooldownRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => () => { if (cooldownRef.current) clearInterval(cooldownRef.current); }, []);

  // Only offer the Microsoft button if the server actually has SSO configured —
  // no dead button. Anonymous endpoint, exposes nothing but a boolean.
  useEffect(() => {
    api.get<{ enabled: boolean }>('/api/auth/sso/microsoft/config')
      .then(r => setSsoEnabled(Boolean(r.enabled)))
      .catch(() => setSsoEnabled(false));
  }, []);

  const startCooldown = () => {
    setResendCooldown(60);
    cooldownRef.current = setInterval(() => {
      setResendCooldown(prev => {
        if (prev <= 1) { clearInterval(cooldownRef.current!); return 0; }
        return prev - 1;
      });
    }, 1000);
  };

  const handlePassword = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const result = await authApi.login({ email, password });
      if ('step' in result && result.step === 'sms_required') {
        setPendingId(result.userId);
        setMaskedPhone(result.maskedPhone);
        setStep('sms');
        startCooldown();
      } else if ('step' in result && result.step === 'totp_required') {
        setPendingId(result.userId);
        setStep('totp');
      } else {
        // No second factor on file — direct login
        setSession(result as import('../api/auth').AuthResponse);
        navigate(from, { replace: true });
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Login failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleSmsVerify = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const auth = await authApi.loginVerifySms({ userId: pendingId, code: code.trim() });
      setSession(auth);
      navigate(from, { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleTotpVerify = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const auth = await authApi.loginVerifyTotp({ userId: pendingId, code: code.trim() });
      setSession(auth);
      navigate(from, { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleResend = async () => {
    if (resendCooldown > 0) return;
    setError('');
    setLoading(true);
    try {
      const result = await authApi.login({ email, password });
      if ('step' in result && result.step === 'sms_required') {
        setPendingId(result.userId);
        startCooldown();
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not resend code.');
    } finally {
      setLoading(false);
    }
  };

  const wrapper = (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
      padding: 16,
    }}>
      <div style={{ width: '100%', maxWidth: 400 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          {LOGO}
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Toast Notification</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>
            {step === 'password' ? 'Sign in to your admin dashboard' : 'Verify your identity'}
          </p>
        </div>

        <div className="card">
          {step === 'password' ? (
            <form onSubmit={handlePassword}>
              {error && <div className="error-banner">{error}</div>}
              <div className="field" style={{ marginBottom: 16 }}>
                <label htmlFor="email">Email</label>
                <input
                  id="email"
                  type="email"
                  autoComplete="email"
                  required
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  placeholder="admin@company.com"
                  autoFocus
                />
              </div>
              <div className="field" style={{ marginBottom: 24 }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
                  <label htmlFor="password" style={{ margin: 0 }}>Password</label>
                  <Link to="/forgot-password" style={{ fontSize: 12, color: 'var(--accent)', textDecoration: 'none' }}>
                    Forgot password?
                  </Link>
                </div>
                <input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="••••••••"
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
                disabled={loading}
              >
                {loading ? <span className="spinner" /> : 'Sign in'}
              </button>

              {ssoEnabled && (
                <>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '20px 0' }}>
                    <div style={{ flex: 1, height: 1, background: 'rgba(148,148,160,0.25)' }} />
                    <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>or</span>
                    <div style={{ flex: 1, height: 1, background: 'rgba(148,148,160,0.25)' }} />
                  </div>
                  <button
                    type="button"
                    onClick={() => { window.location.href = '/api/auth/sso/microsoft/start'; }}
                    className="btn btn-secondary"
                    style={{ width: '100%', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 10, padding: '12px 16px' }}
                  >
                    {MicrosoftLogo}
                    <span>Sign in with Microsoft</span>
                  </button>
                </>
              )}
            </form>
          ) : step === 'sms' ? (
            <form onSubmit={handleSmsVerify}>
              {error && <div className="error-banner">{error}</div>}
              <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 20, lineHeight: 1.55 }}>
                A 6-digit code was sent to <strong style={{ color: 'var(--text-primary)' }}>{maskedPhone}</strong>.
                Enter it below to complete sign-in.
              </p>
              <div className="field" style={{ marginBottom: 24 }}>
                <label htmlFor="code">Verification code</label>
                <input
                  id="code"
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  required
                  maxLength={6}
                  pattern="\d{6}"
                  value={code}
                  onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  placeholder="000000"
                  autoFocus
                  style={{ fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20 }}
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%', justifyContent: 'center', padding: '12px 16px', marginBottom: 12 }}
                disabled={loading || code.length < 6}
              >
                {loading ? <span className="spinner" /> : 'Verify'}
              </button>
              <div style={{ textAlign: 'center' }}>
                <button
                  type="button"
                  onClick={handleResend}
                  disabled={resendCooldown > 0 || loading}
                  style={{
                    background: 'none', border: 'none', cursor: resendCooldown > 0 ? 'default' : 'pointer',
                    fontSize: 13,
                    color: resendCooldown > 0 ? 'var(--text-dim)' : 'var(--accent)',
                    padding: 0,
                  }}
                >
                  {resendCooldown > 0 ? `Resend in ${resendCooldown}s` : 'Resend code'}
                </button>
              </div>
            </form>
          ) : (
            <form onSubmit={handleTotpVerify}>
              {error && <div className="error-banner">{error}</div>}
              <p style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 20, lineHeight: 1.55 }}>
                Enter the 6-digit code from your authenticator app to complete sign-in.
              </p>
              <div className="field" style={{ marginBottom: 24 }}>
                <label htmlFor="totp-code">Authenticator code</label>
                <input
                  id="totp-code"
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  required
                  maxLength={6}
                  pattern="\d{6}"
                  value={code}
                  onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  placeholder="000000"
                  autoFocus
                  style={{ fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20 }}
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
                disabled={loading || code.length < 6}
              >
                {loading ? <span className="spinner" /> : 'Verify'}
              </button>
            </form>
          )}
        </div>

        {step === 'password' && (
          <p style={{ textAlign: 'center', marginTop: 20, color: 'var(--text-dim)', fontSize: 13 }}>
            No account?{' '}
            <Link to="/register" style={{ color: 'var(--accent)', textDecoration: 'none', fontWeight: 500 }}>
              Create one
            </Link>
          </p>
        )}

        {step !== 'password' && (
          <p style={{ textAlign: 'center', marginTop: 20, color: 'var(--text-dim)', fontSize: 13 }}>
            <button
              type="button"
              onClick={() => { setStep('password'); setCode(''); setError(''); }}
              style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--text-dim)', fontSize: 13, padding: 0 }}
            >
              ← Back to sign in
            </button>
          </p>
        )}
      </div>
    </div>
  );

  return wrapper;
}
