import { NavLink } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

const NAV_ITEMS = [
  { to: '/',            label: 'Dashboard', icon: HomeIcon },
  { to: '/analytics',   label: 'Analytics', icon: AnalyticsIcon },
  { to: '/compose',     label: 'Compose',   icon: ComposeIcon },
  { to: '/templates',   label: 'Templates', icon: TemplatesIcon },
  { to: '/assets',      label: 'Assets',    icon: AssetsIcon },
  { to: '/history',     label: 'History',   icon: HistoryIcon },
  { to: '/devices',     label: 'Devices',   icon: DevicesIcon },
];

const ADMIN_ITEMS = [
  { to: '/moderation',        label: 'Moderation', icon: ModerationIcon },
  { to: '/users',             label: 'Users',      icon: UsersIcon },
  { to: '/settings/api-keys', label: 'API Keys',   icon: ApiKeysIcon },
  { to: '/settings/tenant',   label: 'Settings',   icon: SettingsIcon },
];

export default function Sidebar() {
  const { user, logout } = useAuth();
  const isAdmin = user?.role === 'Admin' || user?.role === 'SuperAdmin';

  return (
    <aside style={{
      width: 224,
      flexShrink: 0,
      background: 'var(--bg-secondary)',
      borderRight: '1px solid rgba(255,255,255,0.06)',
      display: 'flex',
      flexDirection: 'column',
      height: '100vh',
      position: 'sticky',
      top: 0,
      overflow: 'hidden',
    }}>
      {/* Brand */}
      <div style={{ padding: '24px 20px 16px', borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{
            width: 32, height: 32, borderRadius: 6,
            background: 'var(--accent)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            flexShrink: 0,
          }}>
            <ToastIcon />
          </div>
          <div>
            <div style={{ fontWeight: 700, fontSize: 13, color: 'var(--text-primary)', lineHeight: 1.2 }}>
              Toast Notification
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-dim)', marginTop: 2, maxWidth: 140, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {user?.email ?? ''}
            </div>
          </div>
        </div>
      </div>

      {/* Nav */}
      <nav style={{ flex: 1, overflowY: 'auto', padding: '12px 8px' }}>
        {NAV_ITEMS.map(item => (
          <NavItem key={item.to} {...item} />
        ))}

        {isAdmin && (
          <>
            <div style={{
              fontSize: 10,
              fontWeight: 700,
              color: 'var(--text-dim)',
              textTransform: 'uppercase',
              letterSpacing: '0.08em',
              padding: '16px 12px 6px',
            }}>
              Admin
            </div>
            {ADMIN_ITEMS.map(item => (
              <NavItem key={item.to} {...item} />
            ))}
          </>
        )}
      </nav>

      {/* Footer */}
      <div style={{ padding: '12px 8px', borderTop: '1px solid rgba(255,255,255,0.06)' }}>
        <button
          className="btn btn-ghost"
          onClick={logout}
          style={{ width: '100%', justifyContent: 'flex-start', padding: '10px 12px' }}
        >
          <LogoutIcon />
          Sign out
        </button>
      </div>
    </aside>
  );
}

interface NavItemProps {
  to: string;
  label: string;
  icon: React.ComponentType;
}

function NavItem({ to, label, icon: Icon }: NavItemProps) {
  return (
    <NavLink
      to={to}
      end={to === '/'}
      style={({ isActive }) => ({
        display: 'flex',
        alignItems: 'center',
        gap: 10,
        padding: '10px 12px',
        borderRadius: 6,
        textDecoration: 'none',
        fontSize: 14,
        fontWeight: 500,
        color: isActive ? 'var(--accent)' : 'var(--text-secondary)',
        background: isActive ? 'rgba(0,201,167,0.08)' : 'transparent',
        marginBottom: 2,
        transition: 'background 0.15s, color 0.15s',
      })}
    >
      <Icon />
      {label}
    </NavLink>
  );
}

/* Icons — minimal SVG, no external library */

function ToastIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
      <rect x="1" y="4" width="16" height="11" rx="2" fill="#0F1117" stroke="#0F1117" strokeWidth="0" />
      <rect x="2" y="5" width="14" height="9" rx="1.5" fill="white" fillOpacity="0.15" />
      <rect x="3" y="7" width="8" height="1.5" rx="0.75" fill="white" fillOpacity="0.9" />
      <rect x="3" y="10" width="6" height="1" rx="0.5" fill="white" fillOpacity="0.5" />
    </svg>
  );
}

function HomeIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <path d="M2 6.5L8 2l6 4.5V13a1 1 0 01-1 1H3a1 1 0 01-1-1V6.5z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
      <path d="M6 14v-4h4v4" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
    </svg>
  );
}

function ComposeIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <rect x="1.5" y="3.5" width="13" height="9" rx="1.5" stroke="currentColor" strokeWidth="1.5" />
      <path d="M4.5 7h7M4.5 9.5h4.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function TemplatesIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <rect x="1.5" y="1.5" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="8.5" y="1.5" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="1.5" y="8.5" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="8.5" y="8.5" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
    </svg>
  );
}

function HistoryIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <circle cx="8" cy="8" r="6.25" stroke="currentColor" strokeWidth="1.5" />
      <path d="M8 4.5V8l2.5 2" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function DevicesIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <rect x="1" y="3" width="10" height="7" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <path d="M4 13h4M6 10v3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <rect x="11" y="7" width="4" height="5" rx="1" stroke="currentColor" strokeWidth="1.5" />
    </svg>
  );
}

function ModerationIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <path d="M8 1.5L14 4v4c0 3.5-2.5 5.5-6 6.5C2.5 13.5 2 11.5 2 8V4l6-2.5z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
      <path d="M5.5 8l2 2L10.5 6" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function LogoutIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <path d="M6 2H3a1 1 0 00-1 1v10a1 1 0 001 1h3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M11 5l3 3-3 3M14 8H6" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function UsersIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <circle cx="6" cy="5" r="2.5" stroke="currentColor" strokeWidth="1.5" />
      <path d="M1 13c0-2.21 2.239-4 5-4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <circle cx="12" cy="7" r="2" stroke="currentColor" strokeWidth="1.5" />
      <path d="M10 13c0-1.657 1.343-3 3-3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function ApiKeysIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <circle cx="6" cy="9" r="3.5" stroke="currentColor" strokeWidth="1.5" />
      <path d="M8.5 7.5L14 2" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M12 4l1.5 1.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function AssetsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <rect x="1" y="4" width="7" height="7" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="9" y="1.5" width="5.5" height="5.5" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <path d="M9 10.5h5.5M11.75 7.5v6" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function AnalyticsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <path d="M2 12l3.5-4.5L8.5 10l3-4L14 4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M2 14h12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function SettingsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <circle cx="8" cy="8" r="2.5" stroke="currentColor" strokeWidth="1.5" />
      <path d="M8 1v2M8 13v2M1 8h2M13 8h2M3.05 3.05l1.42 1.42M11.53 11.53l1.42 1.42M3.05 12.95l1.42-1.42M11.53 4.47l1.42-1.42"
        stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}
