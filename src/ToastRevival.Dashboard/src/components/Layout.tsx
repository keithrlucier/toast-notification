import { Outlet } from 'react-router-dom';
import Sidebar from './Sidebar';
import { useAuth } from '../contexts/AuthContext';

function roleLabel(role?: string, isPlatformAdmin?: boolean): string {
  if (isPlatformAdmin) return 'Platform Admin';
  if (role === 'SuperAdmin') return 'Tenant Owner';
  if (role === 'Admin') return 'Tenant Admin';
  if (role === 'Technician') return 'Technician';
  return 'User';
}

function initials(email?: string): string {
  if (!email) return 'U';
  return email.slice(0, 1).toUpperCase();
}

export default function Layout() {
  const { user } = useAuth();

  return (
    <div className="app-shell">
      <Sidebar />
      <section className="app-workspace">
        <header className="app-topbar">
          <div>
            <div className="app-kicker">{user?.isPlatformAdmin ? 'Platform Console' : 'Enterprise Console'}</div>
            <div className="app-title">Operations</div>
          </div>
          <div className="identity-chip">
            <span className="identity-avatar">{initials(user?.email)}</span>
            <span className="identity-copy">
              <strong>{user?.email ?? 'Signed in'}</strong>
              <small>{roleLabel(user?.role, user?.isPlatformAdmin)}</small>
            </span>
          </div>
        </header>
        <main className="app-main">
          <Outlet />
        </main>
      </section>
    </div>
  );
}
