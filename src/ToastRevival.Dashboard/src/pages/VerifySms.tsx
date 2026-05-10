import { FormEvent, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';

export default function VerifySms() {
  const [params]   = useSearchParams();
  const navigate   = useNavigate();
  const userId     = params.get('userId') ?? '';

  const [code, setCode]     = useState('');
  const [error, setError]   = useState('');
  const [loading, setLoading] = useState(false);
  const [mobile]            = useState(params.get('mobile') ?? 'your mobile');

  if (!userId) {
    return (
      <div style={shell}>
        <p style={{ color: 'var(--text-secondary)', textAlign: 'center' }}>
          Invalid verification link. <a href="/register" style={{ color: 'var(--accent)' }}>Start over</a>.
        </p>
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await api.post('/api/auth/register/verify-sms', { userId, code });
      navigate('/check-email', { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed. Try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={shell}>
      <div style={{ width: '100%', maxWidth: 420 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Verify your mobile</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>
            Enter the 6-digit code sent to {mobile}
          </p>
        </div>
        <div className="card">
          {error && <div className="error-banner">{error}</div>}
          <form onSubmit={handleSubmit}>
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
          </form>
        </div>
      </div>
    </div>
  );
}

const shell: React.CSSProperties = {
  minHeight: '100vh',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--bg-primary)',
  padding: 16,
};
