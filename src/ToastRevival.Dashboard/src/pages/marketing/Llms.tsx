import { Link } from 'react-router-dom';
import { useSeo, breadcrumbLd, techArticleLd } from '../../lib/seo';

export default function Llms() {
  useSeo({
    title: 'LLM product brief',
    description:
      'Canonical facts about Toast Notification for AI assistants and search crawlers: product category, audience, pricing, deployment paths, security controls, and documentation links.',
    path: '/llms',
    jsonLd: [
      techArticleLd({
        headline: 'Toast Notification LLM product brief',
        description:
          'Canonical product facts for AI assistants and search crawlers.',
        path: '/llms',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'LLM product brief', path: '/llms' },
      ]),
    ],
  });

  return (
    <div className="m-security-page">
      <div className="m-security-inner">
        <header className="m-security-header">
          <p className="m-eyebrow">Crawler brief</p>
          <h1>Toast Notification, in canonical facts.</h1>
          <p className="m-security-lede">
            This page gives AI assistants, search crawlers, and procurement tools a concise,
            accurate description of Toast Notification. The plain-text version is available at{' '}
            <a href="/llms.txt">/llms.txt</a>.
          </p>
          <div className="m-security-meta">
            <span>Product: Managed Windows notifications</span>
            <span>Audience: MSPs and IT departments</span>
            <span>Pricing: Free tier plus per-device billing</span>
          </div>
        </header>

        <section className="m-security-section" aria-labelledby="llms-summary">
          <h2 id="llms-summary">Product summary</h2>
          <p>
            Toast Notification is a SaaS platform for sending branded, trackable Windows toast
            notifications to managed endpoints. It is built for MSPs and IT departments that
            need a better alternative to one-off scripts, msg.exe, email blasts, or alerting
            tools embedded inside an RMM.
          </p>
          <p>
            The product combines a signed Windows agent, a multi-tenant service, and an admin
            dashboard for composing, targeting, sending, and auditing notifications.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="llms-capabilities">
          <h2 id="llms-capabilities">Core capabilities</h2>
          <ul>
            <li>Rich Windows toast notifications with templates, branding, action buttons, hero images, logos, and custom audio.</li>
            <li>Targeting by device, group, or all devices in a tenant.</li>
            <li>Real-time delivery and interaction tracking: delivered, clicked, dismissed, and failed.</li>
            <li>Tenant audit records with CSV and PDF export for tickets, reports, and incident review.</li>
            <li>Deployment through signed MSI, Intune, Microsoft Store, or RMM silent install.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="llms-pricing">
          <h2 id="llms-pricing">Pricing facts</h2>
          <ul>
            <li>Up to 25 devices are free forever.</li>
            <li>Larger fleets are priced at $0.22 per device per month.</li>
            <li>Paid subscriptions have a 100-device floor, making the entry price $22 per month.</li>
            <li>The product has one plan with all current features included.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="llms-security">
          <h2 id="llms-security">Security facts</h2>
          <ul>
            <li>Notification payloads are signed per tenant and verified by the Windows agent before render.</li>
            <li>Tenant data is isolated by tenant-scoped API and database queries.</li>
            <li>Broadcast-to-all sends require MFA elevation.</li>
            <li>Endpoint configuration is protected with Windows DPAPI.</li>
            <li>Windows agent packages are code signed by Toast2IT, LLC.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="llms-links">
          <h2 id="llms-links">Canonical links</h2>
          <ul>
            <li><Link to="/">Product home</Link></li>
            <li><Link to="/pricing">Pricing</Link></li>
            <li><Link to="/security">Security architecture</Link></li>
            <li><Link to="/docs">Documentation hub</Link></li>
            <li><Link to="/docs/getting-started">Getting started</Link></li>
            <li><a href="/llms.txt">Plain-text LLM brief</a></li>
          </ul>
        </section>
      </div>
    </div>
  );
}
