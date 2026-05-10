import { FormEvent, useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { ApiError } from '../api/client';

export default function Login() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/';

  const [email, setEmail]       = useState('');
  const [password, setPassword] = useState('');
  const [error, setError]       = useState('');
  const [loading, setLoading]   = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(email, password);
      navigate(from, { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Login failed. Please try again.');
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
      <div style={{ width: '100%', maxWidth: 400 }}>
        {/* Logo */}
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
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
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Toast Notification</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>Sign in to your admin dashboard</p>
        </div>

        <div className="card">
          <form onSubmit={handleSubmit}>
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
        </div>

        <p style={{ textAlign: 'center', marginTop: 20, color: 'var(--text-dim)', fontSize: 13 }}>
          No account?{' '}
          <Link to="/register" style={{ color: 'var(--accent)', textDecoration: 'none', fontWeight: 500 }}>
            Create one
          </Link>
        </p>
      </div>
    </div>
  );
}
