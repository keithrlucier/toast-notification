import { useEffect, useState } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { useAuth } from '../../contexts/AuthContext';
import { BrandMark } from './BrandMark';

const NAV_LINKS = [
  { to: '/pricing', label: 'Pricing' },
];

export function MarketingHeader() {
  const { user } = useAuth();
  const [scrolled, setScrolled] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 80);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  useEffect(() => {
    setMenuOpen(false);
  }, [location.pathname]);

  const signedIn = !!user;
  const primaryHref = signedIn ? '/dashboard' : '/register';
  const primaryLabel = signedIn ? 'Open dashboard' : 'Get started';
  const secondaryHref = signedIn ? '/dashboard' : '/login';
  const secondaryLabel = signedIn ? 'Dashboard' : 'Sign in';

  return (
    <>
      <header className={`m-header ${scrolled ? 'is-scrolled' : ''}`}>
        <div className="m-header-inner">
          <Link to="/" className="m-logo" aria-label="Toast Notification home">
            <BrandMark className="m-logo-mark" />
            <span className="m-logo-word">Toast Notification</span>
          </Link>

          <nav className="m-nav" aria-label="Primary">
            {NAV_LINKS.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                className={({ isActive }) => `m-nav-link${isActive ? ' is-active' : ''}`}
                end={link.to === '/'}
              >
                {link.label}
              </NavLink>
            ))}
          </nav>

          <div className="m-cta-group">
            <Link to={secondaryHref} className="m-cta-link">
              {secondaryLabel}
            </Link>
            <Link to={primaryHref} className="m-btn m-btn-primary" style={{ padding: '10px 18px', minHeight: 0 }}>
              {primaryLabel}
            </Link>
          </div>

          <button
            type="button"
            className="m-hamburger"
            aria-label={menuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={menuOpen}
            aria-controls="m-mobile-menu"
            onClick={() => setMenuOpen((v) => !v)}
          >
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              {menuOpen ? (
                <>
                  <line x1="6" y1="6" x2="18" y2="18" />
                  <line x1="6" y1="18" x2="18" y2="6" />
                </>
              ) : (
                <>
                  <line x1="4" y1="7" x2="20" y2="7" />
                  <line x1="4" y1="12" x2="20" y2="12" />
                  <line x1="4" y1="17" x2="20" y2="17" />
                </>
              )}
            </svg>
          </button>
        </div>
      </header>

      {menuOpen && (
        <div id="m-mobile-menu" className="m-mobile-menu" role="menu">
          {NAV_LINKS.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              className={({ isActive }) => `m-mobile-link${isActive ? ' is-active' : ''}`}
              role="menuitem"
            >
              {link.label}
            </NavLink>
          ))}
          <Link to={secondaryHref} className="m-mobile-link" role="menuitem">
            {secondaryLabel}
          </Link>
          <Link to={primaryHref} className="m-btn m-btn-primary m-mobile-cta" role="menuitem">
            {primaryLabel}
          </Link>
        </div>
      )}
    </>
  );
}
