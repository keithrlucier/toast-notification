import { FormEvent, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';

export default function ResetPassword() {
  const [params]  = useSearchParams();
  const userId    = params.get('userId') ?? '';
  const token     = params.get('token')  ?? '';

  const [password, setPassword]   = useState('');
  const [confirm, setConfirm]     = useState('');
  const [done, setDone]           = useState(false);
  const [error, setError]         = useState('');
  const [loading, setLoading]     = useState(false);

  if (!userId || !token) {
    return (
      <div style={shell}>
        <div style={{ textAlign: 'center', color: 'var(--text-secondary)' }}>
          <p>This link is invalid or has expired.</p>
          <Link to="/forgot-password" style={{ color: 'var(--accent)' }}>Request a new one</Link>
        </div>
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (password !== confirm) { setError('Passwords do not match.'); return; }
    setError('');
    setLoading(true);
    try {
      await api.post('/api/auth/reset-password', { userId, token, password });
      setDone(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Reset failed. The link may have expired.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={shell}>
      <div style={{ width: '100%', maxWidth: 400 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Choose a new password</h1>
        </div>

        <div className="card">
          {done ? (
            <div style={{ textAlign: 'center', padding: '8px 0' }}>
              <svg width="40" height="40" viewBox="0 0 40 40" fill="none" style={{ margin: '0 auto 16px', display: 'block' }}>
                <circle cx="20" cy="20" r="19" stroke="var(--accent)" strokeWidth="2" />
                <path d="M12 20.5l5.5 5.5L28 15" stroke="var(--accent)" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
              <p style={{ color: 'var(--text-primary)', fontWeight: 600, marginBottom: 8 }}>Password updated.</p>
              <Link to="/login" className="btn btn-primary" style={{ textDecoration: 'none', display: 'inline-flex', marginTop: 8 }}>
                Sign in
              </Link>
            </div>
          ) : (
            <form onSubmit={handleSubmit}>
              {error && <div className="error-banner">{error}</div>}
              <div className="field" style={{ marginBottom: 16 }}>
                <label htmlFor="password">New password</label>
                <input
                  id="password"
                  type="password"
                  required
                  minLength={8}
                  autoFocus
                  autoComplete="new-password"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="8+ characters"
                />
              </div>
              <div className="field" style={{ marginBottom: 24 }}>
                <label htmlFor="confirm">Confirm password</label>
                <input
                  id="confirm"
                  type="password"
                  required
                  minLength={8}
                  autoComplete="new-password"
                  value={confirm}
                  onChange={e => setConfirm(e.target.value)}
                  placeholder="Repeat password"
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
                disabled={loading}
              >
                {loading ? <span className="spinner" /> : 'Update password'}
              </button>
            </form>
          )}
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
