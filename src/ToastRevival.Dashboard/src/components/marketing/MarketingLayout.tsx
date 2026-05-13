import { useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { MarketingHeader } from './MarketingHeader';
import { MarketingFooter } from './MarketingFooter';
import './marketing.css';

export default function MarketingLayout() {
  const location = useLocation();

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, [location.pathname]);

  return (
    <div className="marketing-root">
      <MarketingHeader />
      <main id="main-content">
        <Outlet />
      </main>
      <MarketingFooter />
    </div>
  );
}
