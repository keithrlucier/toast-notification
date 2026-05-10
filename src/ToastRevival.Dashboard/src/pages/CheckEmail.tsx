import { Link } from 'react-router-dom';

export default function CheckEmail() {
  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
      padding: 16,
    }}>
      <div style={{ width: '100%', maxWidth: 420, textAlign: 'center' }}>
        <div style={{
          width: 56, height: 56, borderRadius: '50%',
          background: 'var(--bg-secondary)', border: '1px solid rgba(0,201,167,0.3)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          margin: '0 auto 24px',
        }}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="20 6 9 17 4 12" />
          </svg>
        </div>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 12 }}>
          Check your email
        </h1>
        <p style={{ color: 'var(--text-secondary)', lineHeight: 1.65, marginBottom: 32 }}>
          We sent a password setup link to your email address.
          Click the link to set your password and access your dashboard.
        </p>
        <p style={{ fontSize: 13, color: 'var(--text-dim)' }}>
          Didn&rsquo;t receive it? Check your spam folder or{' '}
          <Link to="/register" style={{ color: 'var(--accent)', textDecoration: 'none' }}>
            start over
          </Link>.
        </p>
      </div>
    </div>
  );
}
