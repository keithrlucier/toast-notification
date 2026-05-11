import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ChevronDown } from '../../components/marketing/FeatureIcons';
import { useSeo, breadcrumbLd } from '../../lib/seo';

const PLANS = [
  {
    name: 'Free',
    range: '1 – 25 devices',
    price: '$0',
    unit: 'forever',
    tagline: 'Every feature. No credit card. No time limit.',
    cta: 'Get started free',
    ctaHref: '/register',
    ctaStyle: 'primary' as const,
    highlight: false,
  },
  {
    name: 'Standard',
    range: '26 – 100 devices',
    price: '$22',
    unit: '/ month',
    tagline: 'One flat rate. Up to 100 devices. No per-device math.',
    cta: 'Get started',
    ctaHref: '/register',
    ctaStyle: 'accent' as const,
    highlight: true,
  },
  {
    name: 'Growth',
    range: '101 – 200 devices',
    price: '$44',
    unit: '/ month',
    tagline: 'One flat rate. Up to 200 devices.',
    cta: 'Get started',
    ctaHref: '/register',
    ctaStyle: 'primary' as const,
    highlight: false,
  },
  {
    name: 'Enterprise',
    range: '200+ devices',
    price: 'Contact us',
    unit: '',
    tagline: 'Volume pricing for large deployments.',
    cta: 'Get in touch',
    ctaHref: 'mailto:support@toastnotification.com?subject=Enterprise%20Pricing',
    ctaStyle: 'ghost' as const,
    highlight: false,
  },
];

const BLOCKS = [
  { block: 'Free',       devices: '1 – 25',    monthly: '$0',          note: 'No credit card required.' },
  { block: 'Standard',  devices: '26 – 100',   monthly: '$22 / mo',    note: 'Flat rate. All features.' },
  { block: 'Growth',    devices: '101 – 200',  monthly: '$44 / mo',    note: 'Flat rate. All features.' },
  { block: 'Enterprise',devices: '200+',       monthly: 'Contact us',  note: 'Volume pricing.' },
];

const INCLUSIONS = [
  {
    heading: 'Notifications',
    items: [
      'Six pre-built templates — Announcement, Alert, Action Required, Reminder, Celebration, Maintenance',
      'Title, body, hero image, logo, action buttons, audio',
      'Scenario routing — Default, Reminder, Alarm, IncomingCall, Urgent',
      'Tenant-uploadable asset library with image moderation',
      'Notification scheduling and recurring sends',
    ],
  },
  {
    heading: 'Targeting',
    items: [
      'Per-device, per-group, or whole-tenant broadcast',
      'TOTP MFA enforcement on broadcast sends',
      'Device groups with explicit membership management',
      'Tenant blocklist for sender content',
    ],
  },
  {
    heading: 'Deployment',
    items: [
      'Code-signed MSI with embedded scheduled task',
      'Code-signed MSIX through the Microsoft Store',
      'Intune LOB compatible',
      'RMM silent install with CLIENTID, SERVERURL, and ENROLLMENTKEY properties',
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
      'TLS 1.2/1.3, HSTS, HTTPS redirect',
      'JWT auth — 60-min user tokens, 365-day device tokens',
      'Per-tenant HMAC-SHA256 payload signing, verified by the Windows agent',
      'Device enrollment keys for restricted registration',
      'Encrypted agent config via DPAPI. Tenant-scoped query isolation.',
    ],
  },
  {
    heading: 'Administration',
    items: [
      'Multi-user admin portal with role-based access',
      'Self-service Stripe billing portal',
      'Device inventory with last-seen and version tracking',
      'Template gallery with live preview',
      'Moderation queue and content-safety scanning',
    ],
  },
];

const FAQ = [
  {
    q: 'How does block pricing work?',
    a: 'You pay a flat monthly rate based on which block your active device count falls into — not per device. 1–25 devices is always free. 26–100 devices is $22/month regardless of whether you have 30 or 100 devices. 101–200 is $44/month. Blocks give you a cost ceiling, not a per-seat invoice.',
  },
  {
    q: 'How is a "device" counted?',
    a: 'The app is licensed per signed-in user session. A single machine with two active users consumes two device slots. Decommissioning a device frees the slot immediately. Devices that have not pinged in 30 days are not counted as active.',
  },
  {
    q: 'How does device counting work on Terminal Server / RDS?',
    a: 'Each logged-on user session counts as one device. Ten users active on a single Terminal Server is ten device slots. Each session receives notifications independently, so each session consumes one slot. There is no special TS mode or per-server licensing.',
  },
  {
    q: 'What happens if my device count crosses a block boundary mid-month?',
    a: 'Billing moves to the next block at the start of the following billing cycle. You are never charged retroactively for crossing a threshold mid-month. Stripe billing is managed through your self-service portal.',
  },
  {
    q: 'Is there a contract or annual commitment?',
    a: 'No contract. Billing is monthly. Cancel from the Stripe billing portal at any time — service continues through the end of the current billing period.',
  },
  {
    q: 'What payment methods do you accept?',
    a: 'Credit card and ACH via Stripe. Self-serve billing portal included with every paid subscription.',
  },
  {
    q: 'Do you offer volume pricing above 200 devices?',
    a: 'Yes. Email support@toastnotification.com with your estimated device count and we will put together a quote.',
  },
  {
    q: 'Where is data stored?',
    a: 'Production data is stored in the United States. Notification payloads are HMAC-SHA256 signed per tenant. Content-safety checks score eligible inputs before delivery.',
  },
  {
    q: 'Do you support SSO or SAML?',
    a: 'SAML / OIDC single sign-on is on the roadmap. Not available yet. Email support@toastnotification.com if this is a hard requirement.',
  },
];

export default function Pricing() {
  const [openIdx, setOpenIdx] = useState<number | null>(0);

  useSeo({
    title: 'Pricing',
    description:
      'Simple block pricing. 1–25 devices free. $22/month up to 100 devices. $44/month up to 200 devices. Every feature included on every plan.',
    path: '/pricing',
    jsonLd: [
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Pricing', path: '/pricing' },
      ]),
    ],
  });

  return (
    <>
      {/* Header */}
      <section className="m-section" aria-labelledby="pricing-heading">
        <div className="m-container">
          <h1 id="pricing-heading" className="m-section-heading is-centered">
            Pricing.
          </h1>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 560, marginInline: 'auto' }}>
            Block pricing. Pay a flat monthly rate for your device range — not per seat.
            Every feature included on every plan. No upsells.
          </p>
        </div>
      </section>

      {/* Pricing cards */}
      <section className="m-section" style={{ paddingTop: 0 }} aria-labelledby="pricing-cards-heading">
        <div className="m-container">
          <h2 id="pricing-cards-heading" className="sr-only">Plans</h2>
          <div className="m-pricing-grid">
            {PLANS.map((plan) => (
              <div
                key={plan.name}
                className={`m-pricing-card${plan.highlight ? ' is-featured' : ''}`}
              >
                <div className="m-pricing-card-header">
                  <span className="m-pricing-card-name">{plan.name}</span>
                  <span className="m-pricing-card-range">{plan.range}</span>
                </div>
                <div className="m-pricing-card-price">
                  <span className={`m-pricing-price${plan.highlight ? ' is-accent' : ''}`}>
                    {plan.price}
                  </span>
                  {plan.unit && (
                    <span className="m-pricing-unit">{plan.unit}</span>
                  )}
                </div>
                <p className="m-pricing-tagline">{plan.tagline}</p>
                {plan.ctaHref.startsWith('mailto') ? (
                  <a
                    href={plan.ctaHref}
                    className="m-btn m-btn-ghost"
                    style={{ marginTop: 'auto', width: '100%', textAlign: 'center' }}
                  >
                    {plan.cta}
                  </a>
                ) : (
                  <Link
                    to={plan.ctaHref}
                    className={`m-btn ${plan.ctaStyle === 'ghost' ? 'm-btn-ghost' : 'm-btn-primary'}`}
                    style={{ marginTop: 'auto', width: '100%', textAlign: 'center' }}
                  >
                    {plan.cta}
                  </Link>
                )}
              </div>
            ))}
          </div>

          <p className="m-plan-fineprint" style={{ textAlign: 'center', marginTop: 24 }}>
            All plans include every feature. No feature is gated behind a higher tier.
            Cancel anytime from the billing portal.
          </p>
        </div>
      </section>

      {/* Block reference table */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)', paddingTop: 48, paddingBottom: 48 }} aria-labelledby="blocks-heading">
        <div className="m-container">
          <h2 id="blocks-heading" className="m-section-heading is-centered">
            At a glance.
          </h2>
          <table className="m-cost-table" aria-label="Pricing blocks by device count" style={{ marginTop: 32 }}>
            <thead>
              <tr>
                <th scope="col">Plan</th>
                <th scope="col">Devices covered</th>
                <th scope="col">Monthly</th>
                <th scope="col" className="m-th-note">Notes</th>
              </tr>
            </thead>
            <tbody>
              {BLOCKS.map((row) => (
                <tr key={row.block}>
                  <td><strong>{row.block}</strong></td>
                  <td><span className="m-mono">{row.devices}</span></td>
                  <td><span className="m-mono">{row.monthly}</span></td>
                  <td className="m-cost-note">{row.note}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* What's included */}
      <section className="m-section" aria-labelledby="included-heading">
        <div className="m-container">
          <h2 id="included-heading" className="m-section-heading is-centered">
            What&rsquo;s included.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 580, marginInline: 'auto' }}>
            Every feature ships on every plan. No Pro gate. No Enterprise tier. No add-on SKU.
          </p>
          <div className="m-inclusion-grid" style={{ marginTop: 48 }}>
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
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="faq-heading">
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
      <section className="m-section" aria-labelledby="pricing-cta-heading">
        <div className="m-final-cta">
          <h2 id="pricing-cta-heading">Start free. Grow when you&rsquo;re ready.</h2>
          <p>
            Register a tenant, deploy the signed MSI, and send your first notification in under ten minutes.
            Free up to 25 devices, no credit card required.
            Questions? <a href="mailto:support@toastnotification.com">support@toastnotification.com</a>.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Get started free
            </Link>
            <a href="mailto:support@toastnotification.com?subject=Toast%20Notification%20Pricing" className="m-btn m-btn-ghost">
              Contact us
            </a>
          </div>
        </div>
      </section>
    </>
  );
}
