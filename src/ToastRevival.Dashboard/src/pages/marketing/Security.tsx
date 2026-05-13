import { Link } from 'react-router-dom';
import { useSeo, breadcrumbLd } from '../../lib/seo';

export default function Security() {
  useSeo({
    title: 'Security architecture',
    description:
      'Toast Notification security architecture: HTTPS transport, HMAC-SHA256 payload signing, tenant isolation, MFA controls, audit logging, and responsible disclosure.',
    path: '/security',
    jsonLd: breadcrumbLd([
      { name: 'Home', path: '/' },
      { name: 'Security', path: '/security' },
    ]),
  });

  return (
    <div className="m-security-page">
      <div className="m-security-inner">

        <header className="m-security-header">
          <p className="m-eyebrow">Security architecture</p>
          <h1>Security controls for managed Windows notifications.</h1>
          <p className="m-security-lede">
            Toast Notification is designed for MSPs that need tenant isolation, signed
            endpoint delivery, audit evidence, and clear operational boundaries. For security
            questions or coordinated disclosure, contact{' '}
            <a href="mailto:security@toastnotification.com">security@toastnotification.com</a>.
          </p>
          <div className="m-security-meta">
            <span>Controls: HMAC payload signing, MFA elevation, tenant isolation</span>
            <span>Transport: TLS 1.2 / 1.3, HSTS enforced</span>
            <span>Production data region: United States</span>
          </div>
        </header>

        <section className="m-security-section" aria-labelledby="platform-heading">
          <h2 id="platform-heading">Platform architecture</h2>
          <p>
            Toast Notification separates the public application tier from the database tier.
            The database is reachable only from the application tier and is not exposed
            directly to the public internet. The current production service is hosted in the
            United States and is single-region.
          </p>
          <ul>
            <li><strong>Application tier</strong> &mdash; TLS termination, static dashboard delivery, API routing, authentication, targeting, and audit workflows.</li>
            <li><strong>Data tier</strong> &mdash; private application-only access for tenant records, users, devices, notification records, assets, and audit logs.</li>
            <li><strong>Windows agent</strong> &mdash; installed per endpoint and distributed through standard Windows installer channels.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="transport-heading">
          <h2 id="transport-heading">Transport security</h2>
          <ul>
            <li>HTTPS is enforced for production traffic. TLS 1.2 and TLS 1.3 are supported; TLS 1.0 and TLS 1.1 are rejected.</li>
            <li>HTTP requests redirect to HTTPS. HSTS is set with <code>max-age=31536000; includeSubDomains</code>.</li>
            <li>Public certificates are managed with automated renewal.</li>
            <li>SignalR WebSocket connections authenticate with JWTs during the connection handshake. Tokens are validated before hub code handles device events.</li>
            <li>Static site and API responses include defensive browser headers: <code>X-Content-Type-Options</code>, <code>X-Frame-Options</code>, <code>Referrer-Policy</code>, and <code>Permissions-Policy</code>.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="auth-heading">
          <h2 id="auth-heading">Authentication and authorization</h2>
          <ul>
            <li><strong>User tokens</strong> &mdash; HMAC-SHA256 JWTs with 60-minute expiry and zero clock-skew tolerance.</li>
            <li><strong>Device tokens</strong> &mdash; tenant-scoped JWTs bound to a device ID. Device credentials cannot be used as user credentials.</li>
            <li><strong>MFA elevation</strong> &mdash; broadcast-to-all sends require a short-lived token containing an MFA claim.</li>
            <li><strong>TOTP replay control</strong> &mdash; accepted TOTP time steps are persisted; codes from the same or an earlier 30-second step are rejected.</li>
            <li><strong>Registration</strong> &mdash; SMS verification codes are hashed at rest with SHA-256 and expire after 10 minutes. Password setup uses ASP.NET Identity email-confirmation tokens.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="signing-heading">
          <h2 id="signing-heading">Payload signing</h2>
          <p>
            Notification payloads are signed with a tenant-specific HMAC-SHA256 key before
            delivery. The Windows agent verifies the signature before rendering a notification.
            Payloads that fail verification are dropped and logged.
          </p>
          <p>
            Registered agents store their endpoint configuration using Windows DPAPI scoped to
            the current user. This protects the service-to-agent delivery path from unsigned
            or modified payloads while keeping tenant signing material separate per tenant.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="tenancy-heading">
          <h2 id="tenancy-heading">Tenant isolation</h2>
          <p>
            Tenant data is scoped by tenant ID throughout the API. Database reads use tenant
            filters, and tenant-facing controllers apply tenant predicates before returning
            data. Platform-administration views are separate from tenant-administration views.
          </p>
          <p>
            Audit-log access is enforced separately for tenant-level and platform-level views.
            Tenant audit endpoints scope every query to the caller&rsquo;s tenant before
            applying date filters or export formatting. Platform administrators read tenant
            data through a distinct cross-tenant view.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="moderation-heading">
          <h2 id="moderation-heading">Content controls</h2>
          <p>
            Tenant blocklists are enforced before notification delivery. The platform also
            includes a moderation pipeline for notification text and image inputs. Where
            external content-safety credentials are configured, inputs are scored before
            queueing; review decisions place notifications into an approval queue instead of
            delivering them immediately.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="atrest-heading">
          <h2 id="atrest-heading">Data protection</h2>
          <ul>
            <li><strong>Database storage</strong> &mdash; production data is stored on encrypted infrastructure storage.</li>
            <li><strong>Agent configuration</strong> &mdash; tenant ID, server URL, device JWT, and tenant signing material are stored with Windows DPAPI protection.</li>
            <li><strong>API keys</strong> &mdash; stored as SHA-256 hashes. The raw key is shown once at creation and cannot be recovered later.</li>
            <li><strong>SMS verification codes</strong> &mdash; stored as SHA-256 hashes with a 10-minute expiry.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="logging-heading">
          <h2 id="logging-heading">Logging and audit trail</h2>
          <p>
            Toast Notification maintains tenant audit records at the application layer. Tenant
            users cannot delete or alter audit entries. The following events are recorded:
          </p>
          <ul>
            <li>Notification created, sent, delivered, clicked, dismissed, and failed</li>
            <li>User login, logout, MFA enrollment, and MFA verification</li>
            <li>Device registration, heartbeat, and decommission</li>
            <li>Template created, modified, and deleted</li>
            <li>API key created and revoked</li>
            <li>Asset uploaded, moderated, and deleted</li>
            <li>Tenant settings modified</li>
          </ul>
          <p>
            Audit records include timestamp in UTC, actor, action, and affected resource.
            Tenant administrators see their own tenant&rsquo;s records. Platform administrators
            have a separate cross-tenant operational view.
          </p>
          <p>
            Customer-facing SIEM export is not currently part of the standard service.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="review-heading">
          <h2 id="review-heading">Security review and regression coverage</h2>
          <p>
            Toast Notification operates an internal security review and regression program
            covering the API surface, Windows agent, and notification delivery pipeline. Review
            scope, test artifacts, and any findings are handled through our internal process
            and are not published publicly.
          </p>
          <p>
            Coordinated disclosure inquiries, prospect security documentation requests, and
            customer-facing security reviews under NDA can be directed to{' '}
            <a href="mailto:security@toastnotification.com">security@toastnotification.com</a>.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="limits-heading">
          <h2 id="limits-heading">Current boundaries</h2>
          <ul>
            <li>The production service is single-region.</li>
            <li>Customer-facing SIEM export is not part of the standard service.</li>
            <li>External content-safety scoring depends on configured provider credentials; tenant blocklists remain enforced by the API.</li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="disclosure-heading">
          <h2 id="disclosure-heading">Responsible disclosure</h2>
          <p>
            To report a vulnerability, email{' '}
            <a href="mailto:security@toastnotification.com">security@toastnotification.com</a>.
            Include the affected endpoint or component, reproduction steps, impact, and any
            relevant screenshots or request IDs. We target an initial response within 48 hours.
          </p>
          <p>
            Toast Notification does not currently operate a paid bug bounty program. Please do
            not run destructive tests, high-volume scanners, spam campaigns, or social
            engineering against production systems. We will coordinate reasonable validation
            windows for good-faith reports.
          </p>
        </section>

        <div className="m-security-footer-nav">
          <Link to="/" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            &larr; Back to home
          </Link>
          <Link to="/docs" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            Docs
          </Link>
          <a href="mailto:security@toastnotification.com" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            security@toastnotification.com
          </a>
        </div>

      </div>
    </div>
  );
}
