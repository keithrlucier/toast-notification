import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { ApiError } from '../api/client';

export default function Register() {
  const { register } = useAuth();
  const navigate = useNavigate();

  const [tenantName, setTenantName] = useState('');
  const [email, setEmail]           = useState('');
  const [password, setPassword]     = useState('');
  const [error, setError]           = useState('');
  const [loading, setLoading]       = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await register(tenantName, email, password);
      navigate('/', { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Registration failed. Please try again.');
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
      <div style={{ width: '100%', maxWidth: 420 }}>
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)' }}>Create your account</h1>
          <p style={{ color: 'var(--text-secondary)', marginTop: 6 }}>Start sending managed Windows notifications</p>
        </div>

        <div className="card">
          <form onSubmit={handleSubmit}>
            {error && <div className="error-banner">{error}</div>}

            <div className="field" style={{ marginBottom: 16 }}>
              <label htmlFor="tenantName">Organization name</label>
              <input
                id="tenantName"
                type="text"
                required
                value={tenantName}
                onChange={e => setTenantName(e.target.value)}
                placeholder="Acme IT Services"
                autoFocus
              />
            </div>

            <div className="field" style={{ marginBottom: 16 }}>
              <label htmlFor="email">Admin email</label>
              <input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="admin@company.com"
              />
            </div>

            <div className="field" style={{ marginBottom: 8 }}>
              <label htmlFor="password">Password</label>
              <input
                id="password"
                type="password"
                autoComplete="new-password"
                required
                minLength={8}
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="8+ characters"
              />
            </div>

            <p style={{ fontSize: 12, color: 'var(--text-dim)', marginBottom: 24 }}>
              Must contain at least one number.
            </p>

            <button
              type="submit"
              className="btn btn-primary"
              style={{ width: '100%', justifyContent: 'center', padding: '12px 16px' }}
              disabled={loading}
            >
              {loading ? <span className="spinner" /> : 'Create account'}
            </button>
          </form>
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
}
