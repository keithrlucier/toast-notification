import { Link } from 'react-router-dom';
import {
  FeatureBellCheck,
  FeatureBarChart,
  FeatureCloudArrow,
  FeatureLockKey,
} from '../../components/marketing/FeatureIcons';
import { useSeo, softwareApplicationLd } from '../../lib/seo';

const CAPABILITIES = [
  {
    Icon: FeatureBellCheck,
    title: 'Rich notifications. Every template pre-built.',
    body:
      'Hero images, logos, action buttons, scenario routing (Reminder, Alarm, Urgent), custom audio. Six templates ship with every tenant. Compose in the dashboard—live preview matches the Action Center render in Segoe UI.',
  },
  {
    Icon: FeatureLockKey,
    title: 'Every send is signed, scanned, and logged.',
    body:
      'Per-tenant HMAC-SHA256 payload signing verified before every render. Azure Content Safety on every notification before publish. Append-only audit log with CSV and PDF export. Nothing ships unsigned.',
  },
  {
    Icon: FeatureCloudArrow,
    title: 'Deploy with the tools you already run.',
    body:
      'Code-signed MSI with embedded scheduled task. Code-signed MSIX via the Microsoft Store. Intune LOB compatible. RMM silent install via CLIENTID and SERVERURL properties. Velopack auto-update with enterprise opt-out registry toggle.',
  },
  {
    Icon: FeatureBarChart,
    title: 'Delivery data you can put in a ticket.',
    body:
      'Per-notification reports: delivered, clicked, dismissed, failed. Aggregate dashboards. Fleet-wide interaction rates. CSV and PDF export formatted for incident review and client-facing reporting.',
  },
];

const COMPLIANCE = [
  { label: 'Transport', value: 'TLS 1.3, HSTS, Let’s Encrypt' },
  { label: 'Payload integrity', value: 'HMAC-SHA256 per tenant, verified on every endpoint' },
  { label: 'Auth', value: 'JWT — 60-min user, 365-day device' },
  { label: 'Content scan', value: 'Azure Content Safety on every send' },
  { label: 'At-rest encryption', value: 'AES-256 database, DPAPI on agent config' },
  { label: 'Tenancy', value: 'EF Core global query filters enforced on every read' },
  { label: 'Code signing', value: 'Sectigo OV, Thales HSM' },
  { label: 'MFA', value: 'TOTP enforced on broadcast sends' },
];

const STEPS = [
  {
    n: '01',
    title: 'Deploy the agent.',
    body:
      'Drop the signed MSI into Intune or your RMM, or run msiexec with CLIENTID and SERVERURL. Agent registers, connects, and starts receiving notifications.',
  },
  {
    n: '02',
    title: 'Compose and target.',
    body:
      'Pick a template. Set title, body, hero image, action buttons, scenario. Live preview matches the Action Center render. Target one device, a group, or your entire tenant.',
  },
  {
    n: '03',
    title: 'Track every delivery.',
    body:
      'Delivered, clicked, dismissed, failed — reported in real time. Aggregate dashboards and per-notification reports available. Export to CSV or PDF for tickets and incident review.',
  },
];

export default function Home() {
  useSeo({
    title: 'Managed Windows notifications for MSPs',
    description:
      'Toast Notification is notification infrastructure for MSP fleets. Multi-tenant, payload-signed, audit-logged. Deploy via MSI, Intune, or your RMM.',
    path: '/',
    jsonLd: softwareApplicationLd(),
  });

  return (
    <>
      {/* Hero */}
      <section className="m-hero" aria-labelledby="hero-heading">
        <div className="m-hero-grid">
          <div className="m-hero-copy">
            <h1 id="hero-heading" className="m-hero-headline">
              Notification infrastructure
              <br />
              built for MSP operations.
            </h1>
            <p className="m-hero-subhead">
              Multi-tenant. Payload-signed. Append-only audit log. Code-signed agent deployable
              via MSI, Intune, or your RMM. Every delivery and interaction tracked end-to-end.
            </p>
            <div className="m-hero-ctas">
              <Link to="/register" className="m-btn m-btn-primary">
                Start 14-day trial
              </Link>
              <Link to="/docs" className="m-btn m-btn-ghost">
                View docs
              </Link>
            </div>
            <p className="m-hero-fineprint">
              14-day trial included with every account. 100-device subscription minimum after trial.
            </p>
          </div>

          <div>
            <div className="m-hero-rule" aria-hidden="true" />
            <figure className="m-hero-figure">
              <img
                src="/screenshots/composer-hero.png"
                alt="Toast Notification composer with live preview of a branded Windows toast"
                className="m-hero-screenshot-img"
                loading="eager"
                decoding="async"
                width={1600}
                height={1000}
              />
              <figcaption className="m-hero-caption">
                Composer with live Action Center preview. Segoe UI rendering, exact match.
              </figcaption>
            </figure>
          </div>
        </div>
      </section>

      {/* Problem / Solution */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="problem-heading">
        <div className="m-two-col">
          <div className="m-two-col-copy">
            <h2 id="problem-heading">msg.exe delivers. It doesn&rsquo;t confirm.</h2>
            <p>
              Most MSPs reach endpoints with msg.exe, custom PowerShell, or whatever notification
              API their RMM ships. It works&mdash;sometimes. It doesn&rsquo;t scale. It isn&rsquo;t
              brandable. It doesn&rsquo;t track delivery or interaction. And it produces nothing an
              auditor will accept as evidence of communication.
            </p>
            <p>
              Toast Notification is purpose-built infrastructure for that gap. One code-signed
              agent, one multi-tenant API, one dashboard any technician can operate.
            </p>
          </div>

          <div className="m-comparison-card">
            <div className="m-compare">
              <span className="m-compare-label">Before</span>
              <div className="m-compare-frame" aria-label="msg.exe console output">
                {`C:\\> msg.exe * /SERVER:WS-014 ^
   "Maintenance window 22:00 ET tonight."

WS-014: message delivered (1 user)
WS-014: 0 acknowledgements
WS-014: no audit trail
WS-014: no branding
WS-014: no retry`}
              </div>
            </div>

            <div className="m-compare">
              <span className="m-compare-label">After</span>
              <div className="m-compare-frame is-toast" aria-label="Branded Toast Notification render">
                <div className="m-compare-toast-hero" aria-hidden="true">
                  <span>Maintenance window</span>
                </div>
                <div className="m-compare-toast-body">
                  <div className="m-compare-toast-row">
                    <span className="m-compare-toast-logo" aria-hidden="true">
                      <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M8 2.5 C 5.5 2.5, 4 4.5, 4 7 L 4 9.5 L 3 11.5 L 13 11.5 L 12 9.5 L 12 7 C 12 4.5, 10.5 2.5, 8 2.5 Z" />
                      </svg>
                    </span>
                    <span className="m-compare-toast-title">Maintenance window tonight</span>
                  </div>
                  <p className="m-compare-toast-text">
                    Your workstation will reboot at 22:00 ET. Save your work. Snooze available.
                  </p>
                  <div className="m-compare-toast-actions">
                    <span className="m-compare-toast-btn is-primary">Acknowledge</span>
                    <span className="m-compare-toast-btn">Snooze 30m</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Security & compliance */}
      <section
        className="m-section"
        aria-labelledby="compliance-heading"
      >
        <div className="m-container m-compliance">
          <div className="m-compliance-copy">
            <h2 id="compliance-heading" className="m-section-heading">
              Eight controls. Every tenant. Every send.
            </h2>
            <p className="m-section-subhead" style={{ marginTop: 16, maxWidth: 520 }}>
              Security is not a tier or an add-on. These controls are enforced on every
              notification, for every tenant, on every subscription.
            </p>
            <p className="m-compliance-note">
              Full technical documentation in the <Link to="/docs">docs</Link>.
            </p>
          </div>

          <dl className="m-compliance-grid" aria-label="Security controls">
            {COMPLIANCE.map(({ label, value }) => (
              <div key={label} className="m-compliance-item">
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>

      {/* Capabilities */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="capabilities-heading">
        <div className="m-container">
          <h2 id="capabilities-heading" className="m-section-heading is-centered">
            What&rsquo;s included.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            No premium tier. No feature paywalls. Every capability available on every subscription.
          </p>

          <div className="m-features-grid">
            {CAPABILITIES.map(({ Icon, title, body }) => (
              <article key={title} className="m-feature-card">
                <Icon className="m-feature-icon" />
                <h3>{title}</h3>
                <p>{body}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* How it works */}
      <section
        id="how-it-works"
        className="m-section"
        aria-labelledby="how-heading"
      >
        <div className="m-container">
          <h2 id="how-heading" className="m-section-heading is-centered">
            Deploy in under ten minutes.
          </h2>

          <div className="m-steps">
            {STEPS.map((step) => (
              <div key={step.n} className="m-step">
                <div className="m-step-num" aria-hidden="true">
                  {step.n}
                </div>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Pricing summary */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="pricing-heading"
      >
        <div className="m-container m-pricing-summary">
          <h2 id="pricing-heading" className="m-section-heading is-centered">
            Straightforward pricing.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 560 }}>
            $0.22 per managed device per month. 100-device minimum. 14-day free trial.
            No tiers, no upsells, no feature paywalls.
          </p>

          <table className="m-price-grid" aria-label="Indicative monthly cost by fleet size">
            <thead>
              <tr>
                <th scope="col">Fleet size</th>
                <th scope="col">Monthly</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>100 devices</td>
                <td><span className="m-mono">$22</span></td>
              </tr>
              <tr>
                <td>300 devices</td>
                <td><span className="m-mono">$66</span></td>
              </tr>
              <tr>
                <td>500 devices</td>
                <td><span className="m-mono">$110</span></td>
              </tr>
              <tr>
                <td>1,000 devices</td>
                <td><span className="m-mono">$220</span></td>
              </tr>
              <tr>
                <td>5,000 devices</td>
                <td><span className="m-mono">$1,100</span></td>
              </tr>
            </tbody>
          </table>

          <p className="m-tier-footnote">
            <Link to="/pricing">Full pricing details &amp; FAQ &rsaquo;</Link>
          </p>
        </div>
      </section>

      {/* Final CTA */}
      <section className="m-section" aria-labelledby="final-cta-heading">
        <div className="m-final-cta">
          <h2 id="final-cta-heading">Deploy in under ten minutes.</h2>
          <p>
            Register a tenant, drop the signed MSI into Intune or your RMM, and send your
            first notification to your first endpoint.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Start 14-day trial
            </Link>
            <Link to="/pricing" className="m-btn m-btn-ghost">
              Pricing details
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
