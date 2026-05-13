import { Link } from 'react-router-dom';
import { BrandMark } from './BrandMark';

export function MarketingFooter() {
  const year = new Date().getUTCFullYear();
  return (
    <footer className="m-footer">
      <div className="m-footer-inner">
        <div className="m-footer-grid m-footer-grid--slim">
          <div className="m-footer-brand-block">
            <Link to="/" className="m-logo" aria-label="Toast Notification home">
              <BrandMark className="m-logo-mark" />
              <span className="m-logo-word">Toast Notification</span>
            </Link>
            <p className="m-footer-tagline">Managed Windows toast notifications for MSPs.</p>
          </div>

          <div className="m-footer-col">
            <h4>Product</h4>
            <Link to="/pricing">Pricing</Link>
            <Link to="/login">Sign in</Link>
            <Link to="/register">Request access</Link>
          </div>

          <div className="m-footer-col">
            <h4>Resources</h4>
            <Link to="/docs">Docs</Link>
            <Link to="/docs/getting-started">Getting started</Link>
            <Link to="/docs/api">API reference</Link>
          </div>

          <div className="m-footer-col">
            <h4>Legal</h4>
            <Link to="/legal/privacy">Privacy</Link>
            <Link to="/legal/terms">Terms</Link>
          </div>
        </div>

        <div className="m-footer-bottom">
          <span>© {year} Toast2IT, LLC. Built in the United States since 2021.</span>
        </div>
      </div>
    </footer>
  );
}
