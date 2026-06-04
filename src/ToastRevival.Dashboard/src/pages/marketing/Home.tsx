import { Link } from 'react-router-dom';
import {
  FeatureBellCheck,
  FeatureBarChart,
  FeatureCloudArrow,
  FeatureLockKey,
} from '../../components/marketing/FeatureIcons';
import { useSeo, softwareApplicationLd } from '../../lib/seo';

const USE_CASES = [
  {
    title: 'Maintenance windows',
    body: 'Send tenant-branded notices before patching, backup pauses, planned outages, or forced reboot windows. Track which users acknowledged the message.',
  },
  {
    title: 'Security response',
    body: 'Reach Windows users during phishing campaigns, password resets, VPN outages, endpoint isolation, or emergency instructions without relying on email.',
  },
  {
    title: 'MSP client operations',
    body: 'Separate tenants, templates, enrollment keys, device groups, and audit history so each client receives the right notification from the right brand.',
  },
  {
    title: 'Help desk follow-through',
    body: 'Send action-required prompts, policy reminders, and service updates with delivery evidence that can be attached to tickets or client reports.',
  },
];

export default function Home() {
  useSeo({
    title: 'Windows notification platform for MSPs',
    description:
      'Toast Notification sends branded, signed, trackable Windows toast notifications for MSPs and IT teams, plus dashboard-managed desktop info overlays and device lock screen branding — no login scripts or GPO.',
    path: '/',
    jsonLd: softwareApplicationLd(),
  });

  return (
    <>
      {/* Hero Section */}
      <section className="m-hero-technical" aria-labelledby="hero-heading">
        <div className="m-hero-technical-copy">
          <h1 id="hero-heading">
            Branded Windows notifications your users can act on.
          </h1>
          <p>
            Toast Notification gives MSPs and IT teams a dedicated console for sending native Windows toast notifications, proving delivery, and recording user actions across managed endpoints.
          </p>
          <div className="m-hero-ctas" style={{ display: 'flex', gap: '16px', marginBottom: '24px' }}>
            <Link to="/register" className="m-btn m-btn-primary">
              Request trial access
            </Link>
            <Link to="/docs" className="m-btn m-btn-ghost">
              Read the docs
            </Link>
          </div>
          <div style={{ color: '#64748b', fontSize: '14px' }}>
            Reviewed trials, tenant-scoped deployment values.
          </div>
        </div>

        <div className="m-hero-image-container" style={{ position: 'relative', width: '100%', maxWidth: '700px', borderRadius: '12px', overflow: 'hidden', boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(255,255,255,0.1)' }}>
          <img src="/hero-desktop.png" alt="Windows 11 desktop showing a Toast notification on an executive desk" style={{ width: '100%', height: 'auto', display: 'block', objectFit: 'cover' }} />
        </div>
      </section>

      <section className="m-section" aria-labelledby="use-cases-heading" style={{ background: 'var(--bg-secondary)', paddingTop: 72, paddingBottom: 72 }}>
        <div className="m-container" style={{ marginBottom: 40, textAlign: 'center' }}>
          <h2 id="use-cases-heading" className="m-section-heading">Where teams use it.</h2>
          <p className="m-section-subhead" style={{ marginTop: 16, maxWidth: 700, marginInline: 'auto' }}>
            Toast Notification is for operational messages that need endpoint presence, visible branding, and proof of delivery.
          </p>
        </div>
        <div className="m-bento-grid">
          {USE_CASES.map(item => (
            <div key={item.title} className="m-bento-item m-bento-small">
              <h3>{item.title}</h3>
              <p>{item.body}</p>
            </div>
          ))}
        </div>
      </section>

      {/* The msg.exe Reality Check */}
      <section className="m-section" aria-labelledby="reality-check-heading">
        <div className="m-container" style={{ textAlign: 'center', marginBottom: '48px' }}>
          <h2 id="reality-check-heading" className="m-section-heading">msg.exe delivers. It doesn't confirm.</h2>
          <p className="m-section-subhead" style={{ maxWidth: '650px', margin: '16px auto 0' }}>
            Legacy methods lack branding, interaction tracking, and audit trails. Toast Notification provides guaranteed, verifiable delivery across thousands of endpoints.
          </p>
        </div>
        
        <div className="m-terminal-comparison">
          <div className="m-terminal-box">
            <div className="m-terminal-header">
              <span>Legacy (msg.exe / PowerShell)</span>
              <span style={{ color: '#ef4444' }}>Unverified</span>
            </div>
            <div className="m-terminal-body" style={{ color: '#94a3b8' }}>
{`C:\\> msg.exe * /SERVER:WS-014 "Rebooting tonight"

WS-014: message delivered (1 user)
WS-014: 0 acknowledgements
WS-014: no audit trail available
WS-014: no visual branding
WS-014: payload unsigned`}
            </div>
          </div>

          <div className="m-terminal-box" style={{ borderColor: 'rgba(56, 189, 248, 0.3)' }}>
            <div className="m-terminal-header">
              <span style={{ color: '#f8fafc' }}>Toast Notification Infrastructure</span>
              <span style={{ color: '#10b981' }}>Verified</span>
            </div>
            <div className="m-terminal-body" style={{ color: '#e2e8f0' }}>
{`> POST /api/v1/notifications
> Payload signed: HMAC-SHA256
> Target: DeviceGroup: "Servers"

[✓] Signature verified by Windows agent
[✓] Rendered Native Action Center UI
[✓] User Action: Clicked "Acknowledge"
[✓] Audit Log: Appended (Delivery & Interaction)`}
            </div>
          </div>
        </div>
      </section>

      {/* Bento Box Grid */}
      <section className="m-section" aria-labelledby="capabilities-heading" style={{ background: 'var(--bg-secondary)', padding: '80px 0' }}>
        <div className="m-container" style={{ marginBottom: '48px', textAlign: 'center' }}>
          <h2 id="capabilities-heading" className="m-section-heading">Platform Architecture</h2>
          <p className="m-section-subhead" style={{ marginTop: '16px' }}>Built for scale. Secure by default.</p>
        </div>

        <div className="m-bento-grid">
          <div className="m-bento-item m-bento-large">
            <div className="m-bento-icon">
              <FeatureLockKey width="24" height="24" />
            </div>
            <h3>HMAC-SHA256 Payload Signing</h3>
            <p>
              Every notification payload is signed per tenant with HMAC-SHA256 before leaving the server. The Windows agent verifies this signature locally before rendering anything. If it's not signed by us, it doesn't render.
            </p>
          </div>

          <div className="m-bento-item m-bento-small">
            <div className="m-bento-icon">
              <FeatureBarChart width="24" height="24" />
            </div>
            <h3>Audit Evidence</h3>
            <p>
              Track delivered, clicked, dismissed, and failed outcomes. Aggregate dashboards and CSV/PDF exports for compliance evidence.
            </p>
          </div>

          <div className="m-bento-item m-bento-small">
            <div className="m-bento-icon">
              <FeatureBellCheck width="24" height="24" />
            </div>
            <h3>Rich Windows Templates</h3>
            <p>
              Hero images, logos, action buttons, and scenario routing (Reminder, Alarm, Urgent). Full support for native Windows Action Center rendering.
            </p>
          </div>

          <div className="m-bento-item m-bento-large">
            <div className="m-bento-icon">
              <FeatureCloudArrow width="24" height="24" />
            </div>
            <h3>Flexible Deployment Paths</h3>
            <p>
              Deploy the signed MSI via Intune Win32 app or RMM silent install, or install from the Microsoft Store. The agent uses DPAPI to protect local endpoint configuration.
            </p>
          </div>

          <div className="m-bento-item m-bento-small">
            <div className="m-bento-icon">
              <FeatureLockKey width="24" height="24" />
            </div>
            <h3>Device Appearance</h3>
            <p>
              Branded device info and lock screens, deployed from your dashboard — no login scripts, no GPO, no registry edits. Toggle a read-only desktop info overlay (hostname, user, OS, IP, tenant, custom text) and a per-device lock screen image per tenant; the agent applies both at startup without touching the user's wallpaper.
            </p>
          </div>
        </div>
      </section>

      {/* Three ways to run it */}
      <section className="m-section" aria-labelledby="pricing-heading">
        <div className="m-container" style={{ textAlign: 'center' }}>
          <h2 id="pricing-heading" className="m-section-heading">Three ways to run it.</h2>
          <p className="m-section-subhead" style={{ maxWidth: '640px', margin: '16px auto 48px' }}>
            Reviewed trial, managed SaaS, or roll your own with the Docker Compose source.
            Every tier ships every feature.
          </p>

          <div className="m-tier-grid" style={{ marginTop: 0 }}>
            <div className="m-tier-card">
              <div className="m-tier-name">Free Trial</div>
              <p className="m-tier-tagline">Hands-on evaluation for two endpoints.</p>
              <div className="m-tier-price">$0</div>
              <div className="m-tier-price-sub">2 devices · 14 days · reviewed</div>
              <ul className="m-tier-bullets">
                <li>Full product, every feature unlocked</li>
                <li>Trial requests reviewed before activation</li>
                <li>Managed agent delivered from your tenant portal</li>
              </ul>
              <Link to="/register" className="m-btn m-btn-primary m-tier-cta" style={{ width: '100%', textAlign: 'center' }}>
                Request trial access
              </Link>
            </div>

            <div className="m-tier-card">
              <div className="m-tier-name">Managed SaaS</div>
              <p className="m-tier-tagline">We run it. You send notifications.</p>
              <div className="m-tier-price">$0.22</div>
              <div className="m-tier-price-sub">/ device / mo · first 25 free</div>
              <ul className="m-tier-bullets">
                <li>First 25 devices free, then $0.22/device per month</li>
                <li>No device cap; hosted in our US region</li>
                <li>Updates, backups, TLS, and billing handled by us</li>
              </ul>
              <Link to="/pricing" className="m-btn m-btn-primary m-tier-cta" style={{ width: '100%', textAlign: 'center' }}>
                See pricing detail
              </Link>
            </div>

            <div className="m-tier-card">
              <div className="m-tier-name">Roll Your Own</div>
              <p className="m-tier-tagline">Docker Compose source. Your servers. Your rules.</p>
              <div className="m-tier-price">$0</div>
              <div className="m-tier-price-sub">self-hosted · no device cap</div>
              <ul className="m-tier-bullets">
                <li>Full Docker Compose source on GitHub</li>
                <li>No device cap, no billing service required</li>
                <li>You handle hosting, updates, and backups</li>
              </ul>
              <a
                href="https://github.com/keithrlucier/toast-notification"
                target="_blank"
                rel="noreferrer"
                className="m-btn m-btn-ghost m-tier-cta"
                style={{ width: '100%', textAlign: 'center' }}
              >
                View on GitHub
              </a>
            </div>
          </div>
        </div>
      </section>
      
      {/* Final CTA */}
      <section className="m-section" aria-labelledby="final-cta-heading">
        <div className="m-final-cta" style={{ background: 'transparent', border: 'none', boxShadow: 'none' }}>
          <h2 id="final-cta-heading">Start in under ten minutes.</h2>
          <p>
            Request access, deploy the signed MSI to one endpoint after approval, and send your first notification.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Request trial access
            </Link>
            <Link to="/security" className="m-btn m-btn-ghost">
              Security architecture
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
