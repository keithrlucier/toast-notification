import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';

const NAV_GROUPS: { heading: string; links: { to: string; label: string; end?: boolean }[] }[] = [
  {
    heading: 'Start here',
    links: [
      { to: '/docs', label: 'Overview', end: true },
      { to: '/docs/getting-started', label: 'Getting started' },
    ],
  },
  {
    heading: 'Deployment',
    links: [
      { to: '/docs/deploy/store', label: 'Microsoft Store' },
      { to: '/docs/deploy/intune', label: 'Intune (LOB)' },
      { to: '/docs/deploy/rmm', label: 'RMM silent install' },
    ],
  },
  {
    heading: 'API reference',
    links: [{ to: '/docs/api', label: 'REST API' }],
  },
];

export default function DocsLayout() {
  const [navOpen, setNavOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    setNavOpen(false);
  }, [location.pathname]);

  return (
    <div className="m-docs">
      <button
        type="button"
        className="m-docs-mobile-toggle"
        aria-label={navOpen ? 'Close documentation navigation' : 'Open documentation navigation'}
        aria-expanded={navOpen}
        aria-controls="m-docs-nav"
        onClick={() => setNavOpen((v) => !v)}
      >
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <line x1="4" y1="7" x2="20" y2="7" />
          <line x1="4" y1="12" x2="20" y2="12" />
          <line x1="4" y1="17" x2="20" y2="17" />
        </svg>
        <span>Documentation menu</span>
      </button>

      <div className="m-docs-inner">
        <aside
          id="m-docs-nav"
          className={`m-docs-nav${navOpen ? ' is-open' : ''}`}
          aria-label="Documentation navigation"
        >
          <nav>
            {NAV_GROUPS.map((group) => (
              <div key={group.heading} className="m-docs-nav-group">
                <h2 className="m-docs-nav-heading">{group.heading}</h2>
                <ul>
                  {group.links.map((link) => (
                    <li key={link.to}>
                      <NavLink
                        to={link.to}
                        end={link.end}
                        className={({ isActive }) => `m-docs-nav-link${isActive ? ' is-active' : ''}`}
                      >
                        {link.label}
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>
        </aside>

        <div className="m-docs-content">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
