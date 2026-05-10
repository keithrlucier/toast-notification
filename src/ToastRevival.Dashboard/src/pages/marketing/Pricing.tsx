import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ChevronDown } from '../../components/marketing/FeatureIcons';
import { useSeo, pricingProductLd, breadcrumbLd } from '../../lib/seo';

const INCLUSIONS = [
  {
    heading: 'Notifications',
    items: [
      'Six pre-built templates (Announcement, Alert, Action Required, Reminder, Celebration, Maintenance)',
      'Title, body, hero image, logo, action buttons, audio',
      'Scenario routing - Default, Reminder, Alarm, IncomingCall, Urgent',
      'Tenant-uploadable asset library with image moderation',
      'Notification scheduling and recurring sends',
    ],
  },
  {
    heading: 'Targeting',
    items: [
      'Per-device, per-group, or whole-tenant broadcast',
      'TOTP MFA enforcement on broadcast (TargetType=All) sends',
      'Device groups with explicit membership management',
      'Tenant blocklist for sender content',
    ],
  },
  {
    heading: 'Deployment',
    items: [
      'Code-signed MSI with embedded scheduled task',
      'Code-signed MSIX through the Microsoft Store (listing 9PFD6004DVTN)',
      'Intune LOB compatible',
      'RMM silent install with CLIENTID and SERVERURL properties',
      'Velopack auto-update with enterprise opt-out registry toggle',
    ],
  },
  {
    heading: 'Tracking & audit',
    items: [
      'Per-notification delivery and interaction reports',
      'Aggregate delivery rate, interaction rate, fleet activity dashboards',
      'Append-only tenant audit log',
      'CSV and PDF export for incident review and ticket attachment',
    ],
  },
  {
    heading: 'Security',
    items: [
      "TLS 1.3, HSTS, Let's Encrypt",
      'JWT auth - 60-min user, 365-day device tokens',
      'Per-tenant HMAC-SHA256 payload signing, verified by every endpoint',
      'Azure Content Safety on every send',
      'AES-256 at rest. DPAPI on agent config. Multi-tenant query-filter isolation.',
      'Device enrollment keys for restricted registration',
    ],
  },
  {
    heading: 'Branding & ops',
    items: [
      'Tenant logo, primary color, and default audio/scenario',
      'API keys with revocation',
      'Stripe billing portal for self-serve plan and payment management',
      'Email support, business-hours response',
    ],
  },
];

const FAQ = [
  {
    q: 'How is a "device" counted?',
    a: 'A device is any Windows endpoint where the Toast Notification agent is registered and currently active. Decommissioning a device frees the slot immediately. Inactive devices that have not pinged in 30 days are not billed.',
  },
  {
    q: 'What happens if my device count changes mid-month?',
    a: 'Device count is synced to Stripe on registration and decommission. Billing uses the higher of active devices or the 100-device monthly minimum. Canceled subscriptions block new registrations until billing is restored.',
  },
  {
    q: 'Is there a contract or annual commitment?',
    a: 'No contract. Billing is monthly. Cancel from the Stripe billing portal at any time — service continues through the end of the current billing period.',
  },
  {
    q: 'Do you offer volume pricing?',
    a: 'Per-device pricing is uniform up to 5,000 devices. Above 5,000 devices, contact us to discuss options.',
  },
  {
    q: 'How does the 14-day trial work?',
    a: 'Every new tenant gets 14 days of full-feature access, started during Stripe checkout. Billing begins after the trial unless canceled. Trial tenants have access to all features — no locked capabilities.',
  },
  {
    q: 'What payment methods do you accept?',
    a: 'Credit card and ACH via Stripe. Self-serve billing portal included with every subscription.',
  },
  {
    q: 'Where is data stored?',
    a: 'PostgreSQL 16 on AWS US-East-1. Notification payloads are HMAC-SHA256 signed per tenant. Asset uploads are scanned by Azure Content Safety before persistence.',
  },
  {
    q: 'Do you support SSO or SAML?',
    a: 'SAML / OIDC single sign-on is on the roadmap. Not available yet. Email support@toastnotification.com if this is a hard requirement for your deployment.',
  },
];

const COSTS = [
  { devices: '100', monthly: '$22', annual: '$264', note: 'Subscription minimum.' },
  { devices: '250', monthly: '$55', annual: '$660' },
  { devices: '500', monthly: '$110', annual: '$1,320' },
  { devices: '1,000', monthly: '$220', annual: '$2,640' },
  { devices: '2,500', monthly: '$550', annual: '$6,600' },
  { devices: '5,000', monthly: '$1,100', annual: '$13,200' },
  { devices: '5,000+', monthly: 'Contact us', annual: 'Custom', note: 'Volume pricing.' },
];

export default function Pricing() {
  const [openIdx, setOpenIdx] = useState<number | null>(0);

  useSeo({
    title: 'Pricing',
    description:
      '$0.22 per managed device per month. 100-device subscription minimum. 14-day free trial. One plan, no tiers, no upsells.',
    path: '/pricing',
    jsonLd: [
      pricingProductLd(),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Pricing', path: '/pricing' },
      ]),
    ],
  });

  return (
    <>
      {/* Plan card */}
      <section className="m-section" aria-labelledby="pricing-cards-heading">
        <div className="m-container">
          <h1 id="pricing-cards-heading" className="m-section-heading is-centered">
            Pricing.
          </h1>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 600, marginInline: 'auto' }}>
            One plan. Per-device pricing. Every feature included. No premium tiers, no feature paywalls,
            no annual contract.
          </p>

          <div className="m-plan-card" aria-label="Standard plan">
            <div className="m-plan-card-head">
              <div>
                <span className="m-plan-name">Standard</span>
                <p className="m-plan-tagline">Every feature, every tenant.</p>
              </div>
              <div className="m-plan-price-block">
                <span className="m-plan-price">$0.22</span>
                <span className="m-plan-price-unit">per device / month</span>
                <span className="m-plan-price-floor">100-device subscription minimum - $22 / month entry</span>
              </div>
            </div>

            <div className="m-plan-card-cta">
              <Link to="/register" className="m-btn m-btn-primary">
                Start 14-day trial
              </Link>
              <a href="mailto:sales@toastnotification.com?subject=Volume%20pricing" className="m-btn m-btn-ghost">
                Contact for &gt;5,000 devices
              </a>
            </div>

            <p className="m-plan-fineprint">
              Trial starts in Stripe checkout. Billing begins after 14 days unless canceled; manage payment from the
              Stripe billing portal.
            </p>
          </div>
        </div>
      </section>

      {/* Cost reference */}
      <section className="m-section" style={{ paddingTop: 32 }} aria-labelledby="cost-heading">
        <div className="m-container">
          <h2 id="cost-heading" className="m-section-heading is-centered">
            Indicative cost by fleet size.
          </h2>

          <table className="m-cost-table" aria-label="Monthly and annual cost by device count">
            <thead>
              <tr>
                <th scope="col">Devices</th>
                <th scope="col">Monthly</th>
                <th scope="col">Annual</th>
                <th scope="col" className="m-th-note">Notes</th>
              </tr>
            </thead>
            <tbody>
              {COSTS.map((row) => (
                <tr key={row.devices}>
                  <td><span className="m-mono">{row.devices}</span></td>
                  <td><span className="m-mono">{row.monthly}</span></td>
                  <td><span className="m-mono">{row.annual}</span></td>
                  <td className="m-cost-note">{row.note ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* What's included */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="included-heading">
        <div className="m-container">
          <h2 id="included-heading" className="m-section-heading is-centered">
            What&rsquo;s included.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 580, marginInline: 'auto' }}>
            Every feature is included in every subscription. There is no Pro upsell, no Enterprise gate, no add-on SKU.
          </p>

          <div className="m-inclusion-grid">
            {INCLUSIONS.map((group) => (
              <div key={group.heading} className="m-inclusion-card">
                <h3>{group.heading}</h3>
                <ul>
                  {group.items.map((it) => (
                    <li key={it}>{it}</li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section className="m-section" aria-labelledby="faq-heading">
        <div className="m-container">
          <h2 id="faq-heading" className="m-section-heading is-centered" style={{ marginBottom: 48 }}>
            Frequently asked.
          </h2>

          <div className="m-faq">
            {FAQ.map((item, idx) => {
              const open = idx === openIdx;
              return (
                <div key={item.q} className={`m-faq-item${open ? ' is-open' : ''}`}>
                  <button
                    type="button"
                    className="m-faq-trigger"
                    aria-expanded={open}
                    aria-controls={`faq-panel-${idx}`}
                    onClick={() => setOpenIdx(open ? null : idx)}
                  >
                    <span>{item.q}</span>
                    <ChevronDown className="m-faq-chevron" />
                  </button>
                  {open && (
                    <div id={`faq-panel-${idx}`} className="m-faq-answer" role="region">
                      {item.a}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </section>

      {/* Final CTA */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="pricing-cta-heading">
        <div className="m-final-cta">
          <h2 id="pricing-cta-heading">Start the 14-day trial.</h2>
          <p>
            Register a tenant, deploy the signed MSI, and send your first notification in under ten minutes.
            Questions? Email <a href="mailto:support@toastnotification.com">support@toastnotification.com</a>.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Start trial
            </Link>
            <a href="mailto:support@toastnotification.com?subject=Toast%20Notification" className="m-btn m-btn-ghost">
              Contact us
            </a>
          </div>
        </div>
      </section>
    </>
  );
}
