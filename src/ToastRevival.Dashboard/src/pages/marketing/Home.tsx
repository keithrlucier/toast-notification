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
    title: 'Rich notifications. Six templates out of the box.',
    body: 'Hero images, logos, action buttons, scenario routing (Reminder, Alarm, Urgent), custom audio. Every template customizable per tenant. Live preview in the composer matches the Action Center render exactly.',
  },
  {
    Icon: FeatureLockKey,
    title: 'Signed, scanned, and logged on every send.',
    body: 'Per-tenant HMAC-SHA256 payload signing verified before every render. Azure Content Safety on every notification before publish. Append-only audit log. CSV and PDF export for incident review and client reporting.',
  },
  {
    Icon: FeatureCloudArrow,
    title: 'Deploys with tools you already run.',
    body: 'Code-signed MSI with embedded scheduled task. Intune LOB compatible. RMM silent install via CLIENTID and SERVERURL. Code-signed MSIX via the Microsoft Store. Velopack auto-update with enterprise opt-out registry toggle.',
  },
  {
    Icon: FeatureBarChart,
    title: 'Delivery data you can show a client.',
    body: 'Delivered, clicked, dismissed, failed — reported in real time per notification. Aggregate dashboards and fleet-wide interaction rates. CSV and PDF export formatted for ticket attachment and client-facing reporting.',
  },
];

const COMPLIANCE = [
  { label: 'Transport', value: 'TLS 1.3, HSTS, Let’s Encrypt' },
  { label: 'Payload integrity', value: 'HMAC-SHA256 per tenant, verified on every endpoint' },
  { label: 'Auth', value: 'JWT — 60-min user, 365-day device' },
  { label: 'Content scan', value: 'Azure Content Safety on every send' },
  { label: 'At-rest encryption', value: 'AES-256 database, DPAPI on agent config' },
  { label: 'Tenancy', value: 'EF Core global query filters, every read' },
  { label: 'Code signing', value: 'Sectigo OV, Thales HSM' },
  { label: 'MFA', value: 'TOTP enforced on broadcast sends' },
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
            Multi-tenant notification infrastructure for MSPs. Code-signed agent,
            HMAC-signed payloads, append-only audit log. Every delivery confirmed.
            Every interaction on record.
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
            14-day trial included. 100-device minimum after trial.
          </p>
        </div>
      </section>

      {/* Stats strip */}
      <div className="m-stats-strip" aria-label="Platform scale">
        <div className="m-stats-inner">
          <div className="m-stat">
            <div className="m-stat-value">180k+</div>
            <div className="m-stat-label">Managed endpoints</div>
          </div>
          <div className="m-stat">
            <div className="m-stat-value">4.2M</div>
            <div className="m-stat-label">Notifications delivered</div>
          </div>
          <div className="m-stat">
            <div className="m-stat-value">&lt;8 min</div>
            <div className="m-stat-label">Account to first notification</div>
          </div>
          <div className="m-stat">
            <div className="m-stat-value">$22</div>
            <div className="m-stat-label">Starting price per month</div>
          </div>
        </div>
      </div>

      {/* Problem / Solution */}
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="problem-heading">
        <div className="m-two-col">
          <div className="m-two-col-copy">
            <h2 id="problem-heading">msg.exe delivers.<br />It doesn&rsquo;t confirm.</h2>
            <p>
              Most MSPs reach endpoints with msg.exe, custom PowerShell, or whatever notification
              API their RMM ships. It works&mdash;sometimes. It doesn&rsquo;t scale. It
              isn&rsquo;t brandable. It doesn&rsquo;t track delivery or interaction.
            </p>
            <p>
              And it produces nothing an auditor, a client, or a compliance team will accept
              as evidence of communication. Toast Notification is purpose-built infrastructure
              for that gap.
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

      {/* Case study */}
      <section className="m-section" aria-labelledby="case-study-heading">
        <div className="m-case-study">
          <img
            src="/marketing/case-study-msp-team.jpg"
            className="m-case-study-img"
            alt="MSP operations center"
            loading="lazy"
            decoding="async"
          />
          <div className="m-case-study-content">
            <p className="m-case-study-eyebrow">Case study &mdash; Cascade IT Partners</p>
            <h2 id="case-study-heading">2,400 endpoints.<br />47 clients.<br />Zero Monday callbacks.</h2>
            <p className="m-case-study-pull">
              &ldquo;Before Toast Notification, maintenance windows meant a PowerShell script and 20 minutes of
              Monday morning calls from people who &lsquo;never got the notice.&rsquo; We deployed through
              NinjaOne on a Tuesday afternoon. By Thursday we&rsquo;d pushed a branded notification to all
              2,400 endpoints in 51 seconds. Every device confirmed. Zero callbacks.
              The audit log alone paid for the subscription.&rdquo;
            </p>
            <p className="m-case-study-attribution">
              &mdash; Director of Operations, regional MSP, Pacific Northwest
            </p>
            <div className="m-case-study-stats">
              <div>
                <div className="m-case-stat-value">51s</div>
                <div className="m-case-stat-label">Fleet-wide delivery</div>
              </div>
              <div>
                <div className="m-case-stat-value">2,400</div>
                <div className="m-case-stat-label">Endpoints covered</div>
              </div>
              <div>
                <div className="m-case-stat-value">0</div>
                <div className="m-case-stat-label">Help desk callbacks</div>
              </div>
            </div>
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
              Eight controls.<br />Every tenant.<br />Every send.
            </h2>
            <p className="m-section-subhead" style={{ marginTop: 16, maxWidth: 480 }}>
              Security is not a tier or an add-on. These controls are enforced
              on every notification, for every tenant, on every subscription.
            </p>
            <p className="m-compliance-note">
              Full technical documentation in the <Link to="/docs">docs</Link>.
              Pen-test results and architecture details available on request.
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
      <section className="m-section" aria-labelledby="capabilities-heading">
        <div className="m-container">
          <h2 id="capabilities-heading" className="m-section-heading is-centered">
            Everything included. No tiers.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            Every capability on every subscription. No premium tier. No feature paywalls.
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

      {/* Deployment */}
      <section
        className="m-section"
        style={{ background: 'var(--bg-secondary)' }}
        aria-labelledby="deploy-heading"
      >
        <div className="m-container">
          <h2 id="deploy-heading" className="m-section-heading is-centered">
            Deploy with the tools you already run.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16 }}>
            Three paths to the first endpoint. Pick the one that fits your stack.
          </p>
          <div className="m-deploy-grid">
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">Option A</p>
              <h3>MSI / msiexec</h3>
              <p>
                Code-signed installer with embedded scheduled task. Works on any Windows 10/11
                endpoint without additional tooling. Five-minute deployment from download to
                first connected device.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i ToastNotification.msi CLIENTID=&lt;id&gt; SERVERURL=&lt;url&gt;
              </code>
            </div>
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">Option B</p>
              <h3>Intune LOB</h3>
              <p>
                Upload the MSI as a Line-of-Business app. CLIENTID and SERVERURL set as install
                command parameters. Scope to any device group or user group. Automatic enrollment
                on first run.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i "ToastNotification.msi" CLIENTID="..." SERVERURL="..." /qn
              </code>
            </div>
            <div className="m-deploy-card">
              <p className="m-deploy-card-eyebrow">Option C</p>
              <h3>RMM silent install</h3>
              <p>
                NinjaOne, Datto, ConnectWise, Kaseya&mdash;any RMM that can execute msiexec
                with parameters. Silent install, no user interaction required, Velopack
                auto-update keeps agents current.
              </p>
              <code className="m-deploy-cmd">
                msiexec /i ToastNotification.msi /qn CLIENTID=... SERVERURL=...
              </code>
            </div>
          </div>
        </div>
      </section>

      {/* Pricing summary */}
      <section
        className="m-section"
        aria-labelledby="pricing-heading"
      >
        <div className="m-container m-pricing-summary">
          <h2 id="pricing-heading" className="m-section-heading is-centered">
            Straightforward pricing.
          </h2>
          <p className="m-section-subhead is-centered" style={{ marginTop: 16, maxWidth: 520 }}>
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
              {[
                ['100 devices', '$22'],
                ['300 devices', '$66'],
                ['500 devices', '$110'],
                ['1,000 devices', '$220'],
                ['5,000 devices', '$1,100'],
              ].map(([size, cost]) => (
                <tr key={size}>
                  <td>{size}</td>
                  <td><span className="m-mono">{cost}</span></td>
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
      <section className="m-section" style={{ background: 'var(--bg-secondary)' }} aria-labelledby="final-cta-heading">
        <div className="m-final-cta">
          <h2 id="final-cta-heading">
            Deploy in under ten minutes.
          </h2>
          <p>
            Register a tenant, drop the signed MSI into Intune or your RMM,
            and send your first branded notification to your first endpoint.
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
