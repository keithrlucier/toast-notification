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
      'Toast Notification sends branded, signed, trackable Windows toast notifications for MSPs and IT teams handling maintenance, security, help desk, and outage communication.',
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
              Deploy the MSI with an embedded scheduled task via Intune LOB, Microsoft Store, or RMM silent install. The agent uses DPAPI to protect local endpoint configuration.
            </p>
          </div>
        </div>
      </section>

      {/* Transparent Pricing */}
      <section className="m-section" aria-labelledby="pricing-heading">
        <div className="m-container" style={{ textAlign: 'center' }}>
          <h2 id="pricing-heading" className="m-section-heading">Transparent, predictable pricing.</h2>
          <p className="m-section-subhead" style={{ maxWidth: '600px', margin: '16px auto 48px' }}>
            No feature gating. Every tenant gets full API access, all templates, and full audit logging.
          </p>

          <div style={{ display: 'flex', gap: '24px', justifyContent: 'center', flexWrap: 'wrap' }}>
            <div style={{ padding: '32px', background: 'var(--bg-secondary)', borderRadius: '16px', border: '1px solid rgba(255,255,255,0.05)', minWidth: '300px', flex: '1', maxWidth: '400px' }}>
              <div style={{ fontSize: '24px', fontWeight: 600, color: '#f8fafc', marginBottom: '8px' }}>Reviewed Trial</div>
              <div style={{ fontSize: '36px', fontWeight: 700, color: 'var(--accent)', marginBottom: '16px' }}>$0 <span style={{ fontSize: '16px', fontWeight: 400, color: '#94a3b8' }}>/mo</span></div>
              <ul style={{ listStyle: 'none', padding: 0, margin: 0, textAlign: 'left', color: '#cbd5e1', fontSize: '15px' }}>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>Full product evaluation</li>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>Tenant reviewed before activation</li>
                <li style={{ padding: '12px 0' }}>MSI download after approval</li>
              </ul>
            </div>
            
            <div style={{ padding: '32px', background: 'var(--bg-secondary)', borderRadius: '16px', border: '1px solid rgba(56, 189, 248, 0.3)', minWidth: '300px', flex: '1', maxWidth: '400px', position: 'relative' }}>
              <div style={{ position: 'absolute', top: '-14px', left: '50%', transform: 'translateX(-50%)', background: '#38bdf8', color: '#0f172a', padding: '6px 16px', borderRadius: '12px', fontSize: '12px', fontWeight: 600 }}>Production Fleet</div>
              <div style={{ fontSize: '24px', fontWeight: 600, color: '#f8fafc', marginBottom: '8px' }}>$22</div>
              <div style={{ fontSize: '16px', fontWeight: 400, color: '#94a3b8', marginBottom: '16px' }}>flat / month — up to 100 devices</div>
              <ul style={{ listStyle: 'none', padding: 0, margin: 0, textAlign: 'left', color: '#cbd5e1', fontSize: '15px' }}>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ 26–100 devices, one flat rate</li>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ All features included</li>
                <li style={{ padding: '12px 0' }}>✓ Cancel anytime</li>
              </ul>
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
