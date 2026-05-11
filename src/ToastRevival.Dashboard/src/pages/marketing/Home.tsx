import { Link } from 'react-router-dom';
import {
  FeatureBellCheck,
  FeatureBarChart,
  FeatureCloudArrow,
  FeatureLockKey,
} from '../../components/marketing/FeatureIcons';
import { useSeo, softwareApplicationLd } from '../../lib/seo';

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
      {/* Hero Section */}
      <section className="m-hero-technical" aria-labelledby="hero-heading">
        <div className="m-hero-technical-copy">
          <h1 id="hero-heading">
            Notification infrastructure for Windows fleets.
          </h1>
          <p>
            Replace ad-hoc scripts and unbranded RMM widgets with a dedicated, cryptographically signed Windows notification platform. Multi-tenant, payload-signed, and fully auditable.
          </p>
          <div className="m-hero-ctas" style={{ display: 'flex', gap: '16px', marginBottom: '24px' }}>
            <Link to="/register" className="m-btn m-btn-primary">
              Deploy free (up to 25 devices)
            </Link>
            <Link to="/docs" className="m-btn m-btn-ghost">
              Read the docs
            </Link>
          </div>
          <div style={{ color: '#64748b', fontSize: '14px' }}>
            $0.22 per device/month after free tier.
          </div>
        </div>

        <div className="m-code-block-hero">
          <div className="comment"># Deploy via RMM silent install</div>
          <div style={{ marginTop: '8px', wordBreak: 'break-all' }}>
            <span className="command">msiexec</span> <span className="param">/i</span> <span className="string">"ToastNotification.msi"</span> <span className="param">/qn</span> <span className="string">CLIENTID="tenant_xyz"</span> <span className="string">SERVERURL="https://api.toastnotification.com"</span>
          </div>
          <div className="comment" style={{ marginTop: '16px' }}># Output: Agent installed, registered, and waiting for signed payloads.</div>
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
              Deploy the code-signed MSI with an embedded scheduled task via Intune LOB, Microsoft Store, or RMM silent install. The agent uses DPAPI to protect local endpoint configuration.
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
              <div style={{ fontSize: '24px', fontWeight: 600, color: '#f8fafc', marginBottom: '8px' }}>Free Tier</div>
              <div style={{ fontSize: '36px', fontWeight: 700, color: 'var(--accent)', marginBottom: '16px' }}>$0 <span style={{ fontSize: '16px', fontWeight: 400, color: '#94a3b8' }}>/mo</span></div>
              <ul style={{ listStyle: 'none', padding: 0, margin: 0, textAlign: 'left', color: '#cbd5e1', fontSize: '15px' }}>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ Up to 25 devices</li>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ All features included</li>
                <li style={{ padding: '12px 0' }}>✓ No credit card required</li>
              </ul>
            </div>
            
            <div style={{ padding: '32px', background: 'var(--bg-secondary)', borderRadius: '16px', border: '1px solid rgba(56, 189, 248, 0.3)', minWidth: '300px', flex: '1', maxWidth: '400px', position: 'relative' }}>
              <div style={{ position: 'absolute', top: '-14px', left: '50%', transform: 'translateX(-50%)', background: '#38bdf8', color: '#0f172a', padding: '6px 16px', borderRadius: '12px', fontSize: '12px', fontWeight: 600 }}>Production Fleet</div>
              <div style={{ fontSize: '24px', fontWeight: 600, color: '#f8fafc', marginBottom: '8px' }}>$0.22</div>
              <div style={{ fontSize: '16px', fontWeight: 400, color: '#94a3b8', marginBottom: '16px' }}>per signed-in user / month</div>
              <ul style={{ listStyle: 'none', padding: 0, margin: 0, textAlign: 'left', color: '#cbd5e1', fontSize: '15px' }}>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ $22/mo minimum (100 users)</li>
                <li style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>✓ 1 device = 1 signed-in user</li>
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
            Register a tenant, deploy the signed MSI to one endpoint, and send your first notification.
          </p>
          <div className="m-final-cta-buttons">
            <Link to="/register" className="m-btn m-btn-primary">
              Get started free
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