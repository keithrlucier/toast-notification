import { Link } from 'react-router-dom';
import {
  FeatureBellCheck,
  FeatureBarChart,
  FeatureCloudArrow,
  FeatureLockKey,
} from '../../components/marketing/FeatureIcons';
import { useSeo, softwareApplicationLd } from '../../lib/seo';

const STACK = [
  { label: '.NET 8 / ASP.NET Core', value: 'Backend API' },
  { label: 'PostgreSQL 16', value: 'AWS us-east-1' },
  { label: 'Windows App SDK 1.7', value: 'Native agent' },
  { label: 'Zero third-party tracking', value: 'No analytics' },
];

const CAPABILITIES = [
  {
    Icon: FeatureBellCheck,
    title: 'Rich notifications. Every template included.',
    body:
      'Hero images, logos, action buttons, scenario routing (Reminder, Alarm, Urgent), custom audio. Six templates ship with every tenant. Live preview in the composer matches the Action Center render exactly in Segoe UI.',
  },
  {
    Icon: FeatureLockKey,
    title: 'Signed, scanned, and logged on every send.',
    body:
      'Per-tenant HMAC-SHA256 payload signing verified before every render. Azure Content Safety on every notification before publish. Append-only audit log. CSV and PDF export — the kind of evidence a compliance team will actually accept.',
  },
  {
    Icon: FeatureCloudArrow,
    title: 'Deploys with the tools you already have.',
    body:
      'Code-signed MSI with embedded scheduled task. Intune LOB compatible. RMM silent install via CLIENTID and SERVERURL properties. Code-signed MSIX via the Microsoft Store. Velopack auto-update with enterprise opt-out registry toggle.',
  },
  {
    Icon: FeatureBarChart,
    title: 'Delivery data you can show a client.',
    body:
      'Delivered, clicked, dismissed, failed — per notification, in real time. Aggregate dashboards and fleet-wide interaction rates. CSV and PDF export formatted for ticket attachment and client-facing reporting.',
  },
];

const COMPLIANCE = [
  { label: 'Transport', value: "TLS 1.3, HSTS, Let's Encrypt" },
  { label: 'Payload integrity', value: 'HMAC-SHA256 per tenant, verified on every endpoint' },
  { label: 'Auth', value: 'JWT — 60-min user, 365-day device' },
  { label: 'Content scan', value: 'Azure Content Safety on every send' },
  { label: 'At-rest encryption', value: 'AES-256 database, DPAPI on agent config' },
  { label: 'Tenancy', value: 'EF Core global query filters enforced on every read' },
  { label: 'Code signing', value: 'Sectigo OV, Thales HSM' },
  { label: 'MFA', value: 'TOTP enforced on broadcast sends' },
];

export default function Home() {
  useSeo({
    title: 'Managed Windows notifications for MSPs',
    description:
      'Toast Notification is open-infrastructure notification tooling for Windows fleets. Free up to 25 devices. Multi-tenant, payload-signed, audit-logged.',
    path: '/',
    jsonLd: softwareApplicationLd(),
  });

  return (
    <>
      {/* Hero */}
      <section className="m-hero m-hero--cinematic" aria-labelledby="hero-heading">
        <img
          src="/marketing/hero-msp-operator.jpg"
          className="m-hero-bg-img"
          alt=""
          aria-hidden="true"
          loading="eager"
          decoding="async"
        />
        <div className="m-hero-overlay" aria-hidden="true" />
        <div className="m-hero-inner">
          <p className="m-eyebrow">Windows fleet management</p>
          <h1 id="hero-heading" className="m-hero-headline">
            Your clients get<br />the notification.<br />You get the proof.
          </h1>
          <p className="m-hero-subhead">
            Notification infrastructure for Windows fleets.
            Multi-tenant, payload-signed, append-only audit log.
            Deploy via MSI, Intune, or your RMM.
            <strong style={{ color: 'var(--accent)', display: 'block', marginTop: 12 }}>
              Free for up to 25 devices. No credit card.
            </strong>
          </p>
          <div className="m-hero-ctas">
            <Link to="/register" className="m-btn m-btn-primary">
              Get started — it&rsquo;s free
            </Link>
            <Link to="/security" className="m-btn m-btn-ghost">
              Security posture
            </Link>
          </div>
          <p className="m-hero-fineprint">
            25 devices free forever. Larger fleets: $0.22/device/month, cancel anytime.
          </p>
        </div>
      </section>

      {/* Honest stack strip */}
      <div className="m-stats-strip" aria-label="Technical stack">
        <div className="m-stats-inner">
          {STACK.map(({ label, value }) => (
            <div key={label} className="m-stat">
              <div className="m-stat-value" style={{ fontSize: 15, fontFamily: 'var(--font-mono)', letterSpacing: '0.01em' }}>
                {label}
              </div>
              <div className="m-stat-label">{value}</div>
            </div>
          ))}
        </div>
      </div>

      {/* Problem / Solution */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="problem-heading">
        <div className="m-two-col">
          <div className="m-two-col-copy">
            <h2 id="problem-heading">msg.exe delivers.<br />It doesn&rsquo;t confirm.</h2>
            <p>
              Most Windows fleets get their notifications from msg.exe, custom PowerShell,
              or a notification widget bolted onto an RMM. None of it is branded. None of it
              tracks delivery or interaction. And none of it produces anything an auditor
              will recognize as evidence.
            </p>
            <p>
              Toast Notification fills that gap. It&rsquo;s purpose-built notification
              infrastructure&mdash;not a feature inside something else. One code-signed agent,
              one multi-tenant API, one dashboard. Built on an open-source .NET 8 stack running
              on AWS with no hidden dependencies.
            </p>
            <p>
              <Link to="/security" style={{ color: 'var(--accent)', textDecoration: 'none', fontWeight: 500 }}>
                Read the full security architecture &rsaquo;
              </Link>
            </p>
          </div>

          <div className="m-comparison-card">
            <div className="m-compare">
              <span className="m-compare-label">Before</span>
              <div className="m-compare-frame" aria-label="msg.exe console output">
                {`C:\\> msg.exe * /SERVER:WS-014 ^\n   "Maintenance window 22:00 ET tonight."\n\nWS-014: message delivered (1 user)\nWS-014: 0 acknowledgements\nWS-014: no audit trail\nWS-014: no branding\nWS-014: no retry`}
              </div>
            </div>

            <div className="m-compare">
              <span className="m-compare-label">After</span>
              <div className="m-compare-frame is-toast" aria-label="Branded toast notification">
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
            What it does.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            Every feature on every plan. Free tier included.
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

      {/* Security */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="compliance-heading"
      >
        <div className="m-container m-compliance">
          <div className="m-compliance-copy">
            <h2 id="compliance-heading" className="m-section-heading">
              Security architecture.<br />No marketing.
            </h2>
            <p className="m-section-subhead" style={{ marginTop: 16, maxWidth: 480 }}>
              Eight controls enforced on every notification, for every tenant.
              This infrastructure is used in production. The security is real
              because it has to be.
            </p>
            <p className="m-compliance-note">
              Pen-tested May 2026. Full architecture details, logging policy,
              and AWS infrastructure docs on the{' '}
              <Link to="/security">security page</Link>.
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

      {/* Deployment */}
      <section className="m-section" aria-labelledby="deploy-heading">
        <div className="m-container">
          <h2 id="deploy-heading" className="m-section-heading is-centered">
            Deploy with what you already have.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            Three paths to the first endpoint. No proprietary tooling required.
          </p>
          <div className="m-deploy-grid">
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">MSI / msiexec</p>
              <h3>Direct install</h3>
              <p>
                Code-signed installer with embedded scheduled task. Works on any
                Windows 10/11 endpoint. Five minutes from download to first connected device.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i ToastNotification.msi CLIENTID=&lt;id&gt; SERVERURL=&lt;url&gt;
              </code>
            </div>
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">Intune LOB</p>
              <h3>Intune deployment</h3>
              <p>
                Upload as a Line-of-Business app. CLIENTID and SERVERURL set as
                install command parameters. Scopes to any device or user group.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i "ToastNotification.msi" CLIENTID="..." SERVERURL="..." /qn
              </code>
            </div>
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">RMM silent install</p>
              <h3>RMM deployment</h3>
              <p>
                NinjaOne, Datto, ConnectWise, Kaseya&mdash;any RMM that executes
                msiexec with parameters. Silent install, Velopack auto-update
                keeps agents current.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i ToastNotification.msi /qn CLIENTID=... SERVERURL=...
              </code>
            </div>
          </div>
        </div>
      </section>

      {/* Pricing */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="pricing-heading"
      >
        <div className="m-container m-pricing-summary">
          <h2 id="pricing-heading" className="m-section-heading is-centered">
            Free for small shops. Fair for everyone else.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 560 }}>
            Up to 25 devices free, forever. No credit card. No trial timer.
            Larger fleets: $0.22 per device per month.
          </p>

          <table className="m-price-grid" aria-label="Pricing by fleet size">
            <thead>
              <tr>
                <th scope="col">Devices</th>
                <th scope="col">Monthly cost</th>
              </tr>
            </thead>
            <tbody>
              {[
                ['1 – 25', 'Free'],
                ['100', '$22'],
                ['300', '$66'],
                ['500', '$110'],
                ['1,000', '$220'],
              ].map(([size, cost]) => (
                <tr key={size}>
                  <td>{size}</td>
                  <td>
                    <span className="m-mono" style={cost === 'Free' ? { color: 'var(--accent)', fontWeight: 700 } : undefined}>
                      {cost}
                    </span>
                  </td>
                </tr>
              ))}
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
          <h2 id="final-cta-heading">Start in under ten minutes.</h2>
          <p>
            Register a tenant, deploy the signed MSI to one endpoint,
            send your first notification. No payment required for small deployments.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Get started free
            </Link>
            <Link to="/security" className="m-btn m-btn-ghost">
              Security posture
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
