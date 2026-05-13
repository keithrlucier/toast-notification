import { FormEvent, useState, useRef, useEffect } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { ApiError, AUTH_MESSAGE_STORAGE_KEY } from '../api/client';
import { authApi } from '../api/auth';

function takeAuthMessage(): string {
  const message = sessionStorage.getItem(AUTH_MESSAGE_STORAGE_KEY) ?? '';
  if (message) sessionStorage.removeItem(AUTH_MESSAGE_STORAGE_KEY);
  return message;
}

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
  const [error,    setError]    = useState(takeAuthMessage);
  const [loading,  setLoading]  = useState(false);

  // Step 2 state (SMS challenge)
  const [step,        setStep]        = useState<'password' | 'sms'>('password');
  const [pendingId,   setPendingId]   = useState('');
  const [maskedPhone, setMaskedPhone] = useState('');
  const [code,        setCode]        = useState('');
  const [resendCooldown, setResendCooldown] = useState(0);
  const cooldownRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => () => { if (cooldownRef.current) clearInterval(cooldownRef.current); }, []);

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
      } else {
        // No phone on file — direct login
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
            </form>
          ) : (
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

        {step === 'sms' && (
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
