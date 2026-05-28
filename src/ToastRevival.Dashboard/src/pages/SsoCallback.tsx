import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { AUTH_MESSAGE_STORAGE_KEY } from '../api/client';

/**
 * Landing page for the Microsoft SSO redirect. The backend callback issues our
 * JWT and redirects here with it in the URL FRAGMENT (#token=...) — a fragment,
 * never a query string, so the token never reaches a server log or the Referer
 * header. We read it, hydrate the session, and replace history into /dashboard
 * (scrubbing the token out of the address bar). On any error we bounce to /login
 * with a friendly message.
 */
export default function SsoCallback() {
  const { setSessionFromToken } = useAuth();
  const navigate = useNavigate();
  const handled = useRef(false);

  useEffect(() => {
    if (handled.current) return;
    handled.current = true;

    const params = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const token = params.get('token');

    // Clear the fragment so the token isn't left sitting in the address bar /
    // history once we navigate.
    window.history.replaceState(null, '', window.location.pathname);

    if (token && setSessionFromToken(token)) {
      navigate('/dashboard', { replace: true });
      return;
    }

    sessionStorage.setItem(AUTH_MESSAGE_STORAGE_KEY, 'Microsoft sign-in could not be completed. Please try again.');
    navigate('/login', { replace: true });
  }, [navigate, setSessionFromToken]);

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--bg-primary)',
    }}>
      <div className="spinner" />
    </div>
  );
}
