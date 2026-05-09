import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import {
  FeatureBellCheck,
  FeatureBarChart,
  FeatureCloudArrow,
  FeatureLockKey,
} from '../../components/marketing/FeatureIcons';

const CAPABILITIES = [
  {
    Icon: FeatureBellCheck,
    title: 'Branded notifications across the fleet.',
    body:
      'Send rich Windows 11 toast notifications with hero images, logos, action buttons, scenario routing (Reminder, Alarm, Urgent), and custom audio. Six pre-built templates ship with every tenant.',
  },
  {
    Icon: FeatureLockKey,
    title: 'Tenant isolation, payload signing, audit trail.',
    body:
      'JWT authentication. Per-tenant HMAC-SHA256 payload signing verified by every endpoint before render. Azure Content Safety scans every notification before publish. Append-only audit log with CSV and PDF export.',
  },
  {
    Icon: FeatureCloudArrow,
    title: 'Deploy with the tools you already run.',
    body:
      'Code-signed MSI. Code-signed MSIX through the Microsoft Store. Intune LOB compatible. RMM silent install with CLIENTID and SERVERURL properties. Velopack auto-update built in.',
  },
  {
    Icon: FeatureBarChart,
    title: 'Delivery and interaction tracked end-to-end.',
    body:
      'Each delivery reports back: delivered, clicked, dismissed, failed. Aggregate dashboards, per-notification reports, and CSV / PDF export for ticket attachment and incident review.',
  },
];

const COMPLIANCE = [
  { label: 'Transport', value: 'TLS 1.3, HSTS, Let’s Encrypt' },
  { label: 'Auth', value: 'JWT (60-min user, 365-day device)' },
  { label: 'Payload integrity', value: 'HMAC-SHA256 per tenant' },
  { label: 'Content scan', value: 'Azure Content Safety pre-publish' },
  { label: 'At-rest', value: 'AES-256, DPAPI on agent config' },
  { label: 'Tenancy', value: 'EF Core query filters, every read' },
  { label: 'Code signing', value: 'Sectigo OV, Thales hardware token' },
  { label: 'MFA', value: 'TOTP enforced on broadcast sends' },
];

const STEPS = [
  {
    n: '01',
    title: 'Deploy the agent.',
    body:
      'Drop the signed MSI into Intune, your RMM, or run msiexec on a single endpoint with CLIENTID and SERVERURL.',
  },
  {
    n: '02',
    title: 'Compose a notification.',
    body:
      'Pick a template. Add title, body, hero image, action buttons, scenario. Live preview matches the Action Center render in Segoe UI.',
  },
  {
    n: '03',
    title: 'Send and track.',
    body:
      'Target one device, a group, or every endpoint in your tenant. Hit send. Every delivery and interaction is reported in real time.',
  },
];

export default function Home() {
  useEffect(() => {
    document.title = 'Toast Notification - Managed Windows notifications for MSPs';

    const description =
      'Toast Notification is the platform MSPs use to send branded Windows toast notifications across thousands of endpoints. Multi-tenant, signed, audited.';
    let meta = document.querySelector('meta[name="description"]');
    if (!meta) {
      meta = document.createElement('meta');
      meta.setAttribute('name', 'description');
      document.head.appendChild(meta);
    }
    meta.setAttribute('content', description);
  }, []);

  return (
    <>
      {/* Hero */}
      <section className="m-hero" aria-labelledby="hero-heading">
        <div className="m-hero-grid">
          <div className="m-hero-copy">
            <h1 id="hero-heading" className="m-hero-headline">
              Managed Windows notifications.
              <br />
              Sent from your dashboard.
            </h1>
            <p className="m-hero-subhead">
              Toast Notification is the platform MSPs use to deliver branded, audited Windows toast notifications across
              thousands of endpoints. Multi-tenant, payload-signed, code-signed, and tracked end-to-end.
            </p>
            <div className="m-hero-ctas">
              <Link to="/register" className="m-btn m-btn-primary">
                Start 14-day trial
              </Link>
              <Link to="/pricing" className="m-btn m-btn-ghost">
                View pricing
              </Link>
            </div>
            <p className="m-hero-fineprint">
              14-day trial starts during checkout. 100-device subscription minimum applies after trial.
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
                The composer. Live preview matches the Action Center render in Segoe UI.
              </figcaption>
            </figure>
          </div>
        </div>
      </section>

      {/* Problem / Solution */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="problem-heading">
        <div className="m-two-col">
          <div className="m-two-col-copy">
            <h2 id="problem-heading">PowerShell isn&rsquo;t communication.</h2>
            <p>
              Most MSPs reach end users with msg.exe, custom scripts, or whatever notification API their RMM ships with.
              None of it scales past a few hundred endpoints. None of it is brandable. None of it is signed. None of it
              tracks delivery or interaction. And it produces nothing an auditor will accept.
            </p>
            <p>
              Toast Notification is one platform: a code-signed Windows agent, a multi-tenant API, and a dashboard any
              technician can drive. Built for MSPs whose end users expect more than a console window.
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

      {/* Capabilities */}
      <section className="m-section" aria-labelledby="capabilities-heading">
        <div className="m-container">
          <h2 id="capabilities-heading" className="m-section-heading is-centered">
            Capabilities.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            Production infrastructure. No MSP-specific bolt-ons. No premium tier paywalls.
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

      {/* Security & compliance */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="compliance-heading"
      >
        <div className="m-container m-compliance">
          <div className="m-compliance-copy">
            <h2 id="compliance-heading" className="m-section-heading">
              Security architecture.
            </h2>
            <p className="m-section-subhead" style={{ marginTop: 16, maxWidth: 520 }}>
              We treat notification infrastructure the way you treat the rest of your stack. Eight controls, every
              tenant, every send.
            </p>
            <p className="m-compliance-note">
              SOC 2 Type II preparation in progress. HIPAA-friendly architecture for compliance-driven shops; ask for
              the BAA path during procurement.
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

      {/* How it works */}
      <section
        id="how-it-works"
        className="m-section"
        aria-labelledby="how-heading"
      >
        <div className="m-container">
          <h2 id="how-heading" className="m-section-heading is-centered">
            How it works.
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
            One plan. Per-device pricing.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 560 }}>
            $0.22 per managed device per month. 100-device subscription minimum. 14-day free trial. No tiers, no
            upsells, no feature paywalls.
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
            <Link to="/pricing">See full pricing details &amp; FAQ &rsaquo;</Link>
          </p>
        </div>
      </section>

      {/* Final CTA */}
      <section className="m-section" aria-labelledby="final-cta-heading">
        <div className="m-final-cta">
          <h2 id="final-cta-heading">Start the 14-day trial.</h2>
          <p>
            Register a tenant, deploy the signed MSI, and send your first notification in under ten minutes.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Start trial
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
