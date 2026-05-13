import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ChevronDown } from '../../components/marketing/FeatureIcons';
import { useSeo, breadcrumbLd } from '../../lib/seo';

const TIERS = [
  {
    name: 'Free Trial',
    tagline: 'Hands-on evaluation for two endpoints.',
    price: '$0',
    priceSub: '2 devices · 14 days · reviewed',
    bullets: [
      'Full product, every feature unlocked',
      'Two enrolled devices for the trial window',
      'Trial requests reviewed before activation',
      'Pre-signed MSI download after approval',
    ],
    cta: { label: 'Request trial access', href: '/register', style: 'primary' as const },
    foot: 'Convert to Managed SaaS or self-host before the 14-day window ends.',
  },
  {
    name: 'Managed SaaS',
    tagline: 'We run it. You send notifications.',
    price: '$22',
    priceSub: '/ month · up to 100 devices',
    bullets: [
      'Hosted on our infrastructure, US region',
      'Updates, backups, and TLS handled by us',
      'Microsoft Store MSIX listing available',
      'Cancel from the billing portal anytime',
    ],
    cta: { label: 'Request trial access', href: '/register', style: 'primary' as const },
    foot: 'Trial first, then activate billing. No annual contract.',
  },
  {
    name: 'Roll Your Own',
    tagline: 'Docker Compose source. Your servers. Your rules.',
    price: '$0',
    priceSub: 'self-hosted · no device cap',
    bullets: [
      'Full Docker Compose source on GitHub',
      'No device cap, no billing service required',
      'Run on your own hardware or cloud',
      'You handle hosting, updates, and backups',
    ],
    cta: { label: 'View on GitHub', href: 'https://github.com/keithrlucier/toast-notification', style: 'ghost' as const, external: true },
    foot: 'Bring your own OV cert to sign the agent, or run the pre-signed MSI from our GitHub release.',
  },
];

const INCLUSIONS = [
  {
    heading: 'Notifications',
    items: [
      'Six pre-built templates — Announcement, Alert, Action Required, Reminder, Celebration, Maintenance',
      'Title, body, hero image, logo, action buttons, audio',
      'Scenario routing — Default, Reminder, Alarm, IncomingCall, Urgent',
      'Tenant asset library with image moderation',
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
      'Signed MSI with embedded scheduled task',
      'MSIX through the Microsoft Store',
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
      'Encrypted agent config via DPAPI, tenant-scoped query isolation',
    ],
  },
  {
    heading: 'Administration',
    items: [
      'Multi-user admin portal with role-based access',
      'Device inventory with last-seen and version tracking',
      'Template gallery with live preview',
      'Moderation queue and content-safety scanning',
      'Self-service Stripe billing portal (Managed SaaS only)',
    ],
  },
];

const FAQ = [
  {
    q: 'Why three tiers?',
    a: 'Different operators want different things. Some want to evaluate the product on two endpoints before they commit. Some want us to host and run it so they never see a server. Some have an MSP background and would rather run their own Docker stack on infrastructure they already manage. The price is a side effect of that choice, not a feature gate. Every tier ships every feature.',
  },
  {
    q: 'What happens after the 14-day trial?',
    a: 'Two devices and fourteen days are enforced in the backend. To keep using the product, either activate Managed SaaS billing through the admin dashboard or pull the Docker Compose source and run it yourself. There is no automatic conversion to a paid plan.',
  },
  {
    q: 'How is a "device" counted on Managed SaaS?',
    a: 'The agent is licensed per signed-in user session. A single machine with two active users consumes two device slots. Decommissioning a device frees the slot immediately. Devices that have not pinged in 30 days are not counted as active.',
  },
  {
    q: 'How does device counting work on Terminal Server / RDS?',
    a: 'Each logged-on user session counts as one device. Ten users active on a single Terminal Server is ten device slots. Each session receives notifications independently, so each session consumes one slot. There is no special TS mode.',
  },
  {
    q: 'What does "Roll Your Own" actually include?',
    a: 'The full Docker Compose stack — ASP.NET Core 8 API, React dashboard, PostgreSQL 16, nginx — plus a self-host README with the three-step deploy and an environment file documenting every config key. Billing is disabled by default. Turnstile and content safety degrade gracefully if you do not supply keys. Named volumes for the database and uploaded assets.',
  },
  {
    q: 'Can I sign the agent myself for self-host?',
    a: 'Yes, two paths. Path A — use our pre-signed MSI from the GitHub release. That is the path most self-hosters take. Path B — buy your own OV code-signing certificate (roughly $300-400 a year, one-to-three day validation) and sign the MSI yourself. Path B is documented but high friction; most operators evaluating between SaaS and self-host pick Path A or convert to Managed SaaS.',
  },
  {
    q: 'Is there a contract on Managed SaaS?',
    a: 'No. Billing is monthly through Stripe. Cancel from the billing portal at any time — service continues through the end of the current billing period.',
  },
  {
    q: 'Where is the Managed SaaS data stored?',
    a: 'Production data is stored in the United States, single region. Notification payloads are HMAC-SHA256 signed per tenant. Eligible inputs are scored by content safety before delivery.',
  },
  {
    q: 'Why is this priced this way?',
    a: 'Toast Notification was built in 2020 for MSPs during the work-from-home explosion and delivered 986,000 legitimate notifications across 17 production tenants in its first life. Teams and Slack absorbed the generic use case, so it is now a passion project for the shops where OS-level fleet notification without a third-party dependency still matters. The Managed SaaS rate covers infrastructure. The self-hosted path is free, full stop, no strings.',
  },
];

export default function Pricing() {
  const [openIdx, setOpenIdx] = useState<number | null>(0);

  useSeo({
    title: 'Pricing',
    description:
      'Toast Notification pricing — Free Trial (2 devices, 14 days, reviewed), Managed SaaS ($22/month for up to 100 devices), or Roll Your Own (Docker Compose self-host, free, no device cap).',
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
      {/* Origin lead */}
      <section className="m-section" aria-labelledby="pricing-heading">
        <div className="m-container">
          <h1 id="pricing-heading" className="m-section-heading is-centered">
            Pricing.
          </h1>
          <p
            className="m-section-subhead is-centered"
            style={{ marginTop: 24, maxWidth: 720, marginInline: 'auto' }}
          >
            Toast Notification was built in 2020 for MSPs during the work-from-home explosion
            and delivered 986,000 legitimate notifications across 17 production tenants in its
            first life. Teams and Slack absorbed the generic use case. This is now a passion
            project for the shops where OS-level fleet notification without a third-party
            dependency still matters.
          </p>
          <p
            className="m-section-subhead is-centered"
            style={{ marginTop: 16, maxWidth: 720, marginInline: 'auto' }}
          >
            Three ways to run it. Every feature ships on every tier. The self-hosted path is
            genuinely free, no strings.
          </p>
        </div>
      </section>

      {/* Tier cards */}
      <section className="m-section" style={{ paddingTop: 0 }} aria-labelledby="tiers-heading">
        <div className="m-container">
          <h2 id="tiers-heading" className="sr-only">Plans</h2>
          <div className="m-tier-grid">
            {TIERS.map((tier) => (
              <div key={tier.name} className="m-tier-card">
                <div className="m-tier-name">{tier.name}</div>
                <p className="m-tier-tagline">{tier.tagline}</p>
                <div className="m-tier-price">{tier.price}</div>
                <div className="m-tier-price-sub">{tier.priceSub}</div>
                <ul className="m-tier-bullets">
                  {tier.bullets.map((b) => (
                    <li key={b}>{b}</li>
                  ))}
                </ul>
                {tier.cta.external ? (
                  <a
                    href={tier.cta.href}
                    target="_blank"
                    rel="noreferrer"
                    className={`m-btn ${tier.cta.style === 'ghost' ? 'm-btn-ghost' : 'm-btn-primary'} m-tier-cta`}
                    style={{ width: '100%', textAlign: 'center' }}
                  >
                    {tier.cta.label}
                  </a>
                ) : (
                  <Link
                    to={tier.cta.href}
                    className={`m-btn ${tier.cta.style === 'ghost' ? 'm-btn-ghost' : 'm-btn-primary'} m-tier-cta`}
                    style={{ width: '100%', textAlign: 'center' }}
                  >
                    {tier.cta.label}
                  </Link>
                )}
                <p
                  className="m-tier-footnote"
                  style={{ marginTop: 16, textAlign: 'left' }}
                >
                  {tier.foot}
                </p>
              </div>
            ))}
          </div>

          <p className="m-tier-footnote">
            Every tier ships every feature. No Pro gate, no Enterprise SKU, no add-on.
          </p>
        </div>
      </section>

      {/* What's included */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="included-heading"
      >
        <div className="m-container">
          <h2 id="included-heading" className="m-section-heading is-centered">
            What ships on every tier.
          </h2>
          <p
            className="m-section-subhead is-centered"
            style={{ marginTop: 16, maxWidth: 620, marginInline: 'auto' }}
          >
            The product is the product. The tier is the operational model — who runs the
            infrastructure, who pays for hosting, who holds the database.
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
      <section className="m-section" aria-labelledby="faq-heading">
        <div className="m-container">
          <h2
            id="faq-heading"
            className="m-section-heading is-centered"
            style={{ marginBottom: 48 }}
          >
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
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="pricing-cta-heading"
      >
        <div className="m-final-cta">
          <h2 id="pricing-cta-heading">Pick the path that fits the shop.</h2>
          <p>
            Request a reviewed trial to evaluate against two endpoints, or pull the Docker
            Compose source and run it on your own infrastructure today. Questions —{' '}
            <a href="mailto:support@toastnotification.com">support@toastnotification.com</a>.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Request trial access
            </Link>
            <a
              href="https://github.com/keithrlucier/toast-notification"
              target="_blank"
              rel="noreferrer"
              className="m-btn m-btn-ghost"
            >
              Self-host on GitHub
            </a>
          </div>
        </div>
      </section>
    </>
  );
}
