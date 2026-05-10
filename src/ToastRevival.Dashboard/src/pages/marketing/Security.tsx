import { Link } from 'react-router-dom';
import { useSeo, breadcrumbLd } from '../../lib/seo';

export default function Security() {
  useSeo({
    title: 'Security architecture',
    description:
      'Toast Notification security architecture: HTTPS transport, HMAC-SHA256 payload signing, tenant isolation, MFA controls, audit logging, code signing, and responsible disclosure.',
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
            <span>Last architecture review: May 2026</span>
            <span>Security test review: May 2026</span>
            <span>Controls: signed delivery, MFA elevation, audit export</span>
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
            <li><strong>Windows agent</strong> &mdash; installed per endpoint and distributed through signed installer channels.</li>
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
            Audit-log access was specifically reviewed in May 2026 because it supports both
            tenant-level reporting and platform-level administration. Tenant audit endpoints
            now scope every query to the caller&rsquo;s tenant before applying date filters or
            export formatting.
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

        <section className="m-security-section" aria-labelledby="codesign-heading">
          <h2 id="codesign-heading">Code signing</h2>
          <p>
            Windows agent packages are signed with an Organization Validation certificate
            issued to Toast2IT, LLC. Signing uses hardware-backed key storage; the private key
            is not exportable.
          </p>
          <p>
            The agent validates its own signature before following its managed update path. A
            binary that fails signature validation does not continue through that update
            redirect flow.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="testing-heading">
          <h2 id="testing-heading">Security testing - May 2026</h2>
          <p>
            The May 2026 security review covered these areas of the API and agent delivery
            model:
          </p>
          <ul>
            <li><strong>Tenant isolation</strong> &mdash; device lists, device lookups, notification targeting, catch-up delivery, audit-log reads, and hub group events.</li>
            <li><strong>Authentication bypass</strong> &mdash; expired JWTs, invalid signing keys, missing device claims, user tokens on device endpoints, and broadcast sends without MFA elevation.</li>
            <li><strong>Content injection</strong> &mdash; script payloads, oversized titles, Unicode boundary cases, and HTML/script delimiters in notification body fields.</li>
            <li><strong>Privilege escalation</strong> &mdash; role-restricted user invitations, role changes, cross-tenant user targeting, high-volume sends, and platform-admin-only endpoints.</li>
          </ul>

          <div className="m-security-finding">
            <p className="m-security-finding-label">Closed finding</p>
            <p>
              <strong>Audit-log tenant isolation</strong>
            </p>
            <p>
              A medium-severity tenant-isolation issue was identified in the per-tenant
              audit-log list and export endpoints. The issue allowed an authenticated tenant
              administrator to retrieve audit rows outside their tenant through those audit
              endpoints.
            </p>
            <p>
              The endpoints now scope every audit query to the caller&rsquo;s tenant ID before
              applying date filters or export formatting. Regression coverage verifies that one
              tenant cannot retrieve another tenant&rsquo;s audit rows.
            </p>
          </div>

          <p>
            The review did not document any unresolved high- or critical-severity findings.
            Security regression coverage remains part of the API test suite.
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
