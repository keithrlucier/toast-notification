import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';

type Step = 'details' | 'sms';

export default function Register() {
  const navigate = useNavigate();

  const [step, setStep]         = useState<Step>('details');
  const [userId, setUserId]     = useState('');

  // Step 1 fields
  const [fullName, setFullName]       = useState('');
  const [tenantName, setTenantName]   = useState('');
  const [email, setEmail]             = useState('');
  const [mobile, setMobile]           = useState('');

  // Step 2 field
  const [code, setCode] = useState('');

  const [error, setError]   = useState('');
  const [loading, setLoading] = useState(false);

  const handleDetails = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const res = await api.post<{ userId: string; step: string }>(
        '/api/auth/register/init',
        { fullName, tenantName, email, mobile }
      );
      setUserId(res.userId);
      setStep('sms');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Registration failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleSms = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await api.post('/api/auth/register/verify-sms', { userId, code });
      navigate('/check-email', { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const shell = (title: string, subtitle: string, child: React.ReactNode) => (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
      padding: 16,
    }}>
      <div style={{ width: '100%', maxWidth: 420 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>{title}</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>{subtitle}</p>
        </div>
        <div className="card">
          {error && <div className="error-banner">{error}</div>}
          {child}
        </div>
        <p style={{ textAlign: 'center', marginTop: 20, color: 'var(--text-dim)', fontSize: 13 }}>
          Already have an account?{' '}
          <Link to="/login" style={{ color: 'var(--accent)', textDecoration: 'none', fontWeight: 500 }}>
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );

  if (step === 'sms') {
    return shell(
      'Verify your mobile',
      `We sent a 6-digit code to ${mobile}`,
      <form onSubmit={handleSms}>
        <div className="field" style={{ marginBottom: 24 }}>
          <label htmlFor="code">Verification code</label>
          <input
            id="code"
            type="text"
            inputMode="numeric"
            pattern="[0-9]{6}"
            maxLength={6}
            required
            autoFocus
            autoComplete="one-time-code"
            value={code}
            onChange={e => setCode(e.target.value.replace(/\D/g, ''))}
            placeholder="000000"
            style={{ letterSpacing: '0.2em', fontSize: 20, textAlign: 'center' }}
          />
        </div>
        <button
          type="submit"
          className="btn btn-primary"
          style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
          disabled={loading || code.length !== 6}
        >
          {loading ? <span className="spinner" /> : 'Verify'}
        </button>
        <button
          type="button"
          onClick={() => { setStep('details'); setCode(''); setError(''); }}
          style={{
            display: 'block', width: '100%', marginTop: 12, padding: '10px 16px',
            background: 'transparent', border: 'none', color: 'var(--text-secondary)',
            fontSize: 13, cursor: 'pointer', textAlign: 'center',
          }}
        >
          Back
        </button>
      </form>
    );
  }

  return shell(
    'Create your account',
    'Set up your tenant and verify your mobile',
    <form onSubmit={handleDetails}>
      <div className="field" style={{ marginBottom: 16 }}>
        <label htmlFor="fullName">Full name</label>
        <input
          id="fullName"
          type="text"
          required
          autoFocus
          autoComplete="name"
          value={fullName}
          onChange={e => setFullName(e.target.value)}
          placeholder="Jane Smith"
        />
      </div>

      <div className="field" style={{ marginBottom: 16 }}>
        <label htmlFor="tenantName">Organization name</label>
        <input
          id="tenantName"
          type="text"
          required
          value={tenantName}
          onChange={e => setTenantName(e.target.value)}
          placeholder="Acme IT Services"
        />
      </div>

      <div className="field" style={{ marginBottom: 16 }}>
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

      <div className="field" style={{ marginBottom: 24 }}>
        <label htmlFor="mobile">Mobile number</label>
        <input
          id="mobile"
          type="tel"
          required
          autoComplete="tel"
          value={mobile}
          onChange={e => setMobile(e.target.value)}
          placeholder="+1 555 000 0000"
        />
        <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 6 }}>
          We&rsquo;ll send a verification code via SMS.
        </p>
      </div>

      <button
        type="submit"
        className="btn btn-primary"
        style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
        disabled={loading}
      >
        {loading ? <span className="spinner" /> : 'Continue'}
      </button>
    </form>
  );
}
