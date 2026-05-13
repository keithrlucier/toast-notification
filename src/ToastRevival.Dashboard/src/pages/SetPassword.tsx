import { FormEvent, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import type { AuthResponse } from '../api/auth';

export default function SetPassword() {
  const [params]        = useSearchParams();
  const navigate        = useNavigate();
  const { setSession }  = useAuth();

  const userId = params.get('userId') ?? '';
  const token  = params.get('token')  ?? '';

  const [password, setPassword]         = useState('');
  const [confirmPassword, setConfirm]   = useState('');
  const [error, setError]               = useState('');
  const [loading, setLoading]           = useState(false);

  if (!userId || !token) {
    return (
      <div style={centeredStyle}>
        <div style={{ textAlign: 'center', color: 'var(--text-secondary)' }}>
          <p>This link is invalid or has expired.</p>
          <Link to="/register" style={{ color: 'var(--accent)' }}>Start over</Link>
        </div>
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (password !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }
    setError('');
    setLoading(true);
    try {
      const res = await api.post<AuthResponse>('/api/auth/register/set-password', { userId, token, password });
      setSession(res);
      navigate('/dashboard', { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to set password. The link may have expired.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={centeredStyle}>
      <div style={{ width: '100%', maxWidth: 420 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Set your password</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>
            Choose a password to access your dashboard.
          </p>
        </div>

        <div className="card">
          {error && <div className="error-banner">{error}</div>}
          <form onSubmit={handleSubmit}>
            <div className="field" style={{ marginBottom: 16 }}>
              <label htmlFor="password">Password</label>
              <input
                id="password"
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                autoFocus
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
                value={confirmPassword}
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
              {loading ? <span className="spinner" /> : 'Set password & sign in'}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

const centeredStyle: React.CSSProperties = {
  minHeight: '100vh',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--bg-primary)',
  padding: 16,
};
