import { Link } from 'react-router-dom';
import { useSeo, breadcrumbLd } from '../../lib/seo';

export default function Security() {
  useSeo({
    title: 'Security posture',
    description:
      'Toast Notification security architecture: TLS 1.3, HMAC-SHA256 payload signing, Azure Content Safety, EF Core tenant isolation, Sectigo OV code signing, pen-test results, logging policy, AWS infrastructure.',
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
          <p className="m-eyebrow">Security posture</p>
          <h1>We wrote this ourselves.<br />No consultant. No template.</h1>
          <p className="m-security-lede">
            This page documents what we actually built, how it actually works, and what we
            actually tested. If something is missing, incomplete, or wrong —{' '}
            <a href="mailto:security@toastnotification.com">security@toastnotification.com</a>.
          </p>
          <div className="m-security-meta">
            <span>Last reviewed: May 2026</span>
            <span>Pen-tested: May 2026</span>
            <span>Stack: .NET 8 / ASP.NET Core 8 / PostgreSQL 16</span>
            <span>Infrastructure: AWS us-east-1</span>
          </div>
        </header>

        {/* Infrastructure */}
        <section className="m-security-section" aria-labelledby="infra-heading">
          <h2 id="infra-heading">Infrastructure</h2>
          <p>
            The production environment runs on AWS Lightsail in <strong>us-east-1</strong>.
            All data — notification payloads, tenant records, audit logs, device registrations —
            is stored in the United States. There is no multi-region replication and we don&rsquo;t
            claim otherwise.
          </p>
          <ul>
            <li><strong>API server (TOASTWEB1)</strong> — Ubuntu 22.04, nginx TLS termination, ASP.NET Core 8 Kestrel backend. Let&rsquo;s Encrypt certificates, auto-renewed. HSTS enforced with a 1-year max-age.</li>
            <li><strong>Database server (TOASTDATA1)</strong> — PostgreSQL 16 on a separate private-network instance. Not exposed to the public internet. Connection is private LAN only.</li>
            <li><strong>Agent</strong> — .NET 8 / Windows App SDK 1.7, runs as a per-user scheduled task. Code-signed with a Sectigo OV certificate on a Thales hardware security module. Available via signed MSI, Intune LOB, RMM deployment, and the Microsoft Store (listing 9P5L0MRMFRRF).</li>
          </ul>
        </section>

        {/* Transport */}
        <section className="m-security-section" aria-labelledby="transport-heading">
          <h2 id="transport-heading">Transport security</h2>
          <ul>
            <li>TLS 1.3 enforced. TLS 1.0 and 1.1 disabled at the nginx level.</li>
            <li>HSTS with <code>max-age=31536000; includeSubDomains</code> at the server scope. All HTTP traffic redirects to HTTPS.</li>
            <li>Certificates issued by Let&rsquo;s Encrypt. Auto-renewed via certbot. Current expiry documented in CONTEXT.md.</li>
            <li>SignalR (used for real-time agent↔backend communication) passes JWT as a URL query parameter on WebSocket handshake — this is the standard pattern for SignalR. The <code>OnMessageReceived</code> handler extracts and validates it before any hub code runs.</li>
          </ul>
        </section>

        {/* Authentication */}
        <section className="m-security-section" aria-labelledby="auth-heading">
          <h2 id="auth-heading">Authentication &amp; tokens</h2>
          <ul>
            <li><strong>User JWTs</strong> — HMAC-SHA256, 60-minute expiry, zero clock skew tolerance. Issued by ASP.NET Core Identity, validated on every request.</li>
            <li><strong>Device JWTs</strong> — 365-day expiry, bound to a specific device ID claim. Devices cannot impersonate users and users cannot use device tokens.</li>
            <li><strong>MFA tokens</strong> — 15-minute expiry, include a <code>mfa=true</code> claim. Required for broadcast-to-all sends (target type = All). TOTP via OtpNet 1.4.0, RFC 6238 compliant.</li>
            <li><strong>Replay prevention</strong> — <code>LastTotpStep</code> is persisted on the user record after every successful TOTP verify. Any code whose matched 30-second step ≤ the stored value is rejected.</li>
            <li><strong>Registration flow</strong> — mobile verified via ClickSend SMS (6-digit code, SHA-256 hashed at rest, 10-minute expiry). Password set via ASP.NET Identity email confirmation token delivered through Mailjet.</li>
          </ul>
        </section>

        {/* Payload signing */}
        <section className="m-security-section" aria-labelledby="signing-heading">
          <h2 id="signing-heading">Payload signing</h2>
          <p>
            Every notification payload is signed with a per-tenant HMAC-SHA256 key before it
            leaves the server. The agent verifies the signature before rendering. A payload
            that fails verification is silently dropped and logged.
          </p>
          <p>
            This means a compromised agent binary on one endpoint cannot be used to inject
            unsigned notifications into another tenant&rsquo;s fleet. The signing key is
            tenant-specific and stored server-side only — agents never hold signing keys,
            only verification material.
          </p>
        </section>

        {/* Tenant isolation */}
        <section className="m-security-section" aria-labelledby="tenancy-heading">
          <h2 id="tenancy-heading">Tenant isolation</h2>
          <p>
            All database reads go through EF Core global query filters that enforce
            <code>TenantId</code> scoping. A tenant admin cannot query another
            tenant&rsquo;s data. This is enforced at the ORM layer, not just the
            application layer.
          </p>
          <p>
            The one exception is <code>AuditLog</code>, which intentionally has no global
            query filter — platform administrators need a cross-tenant audit view. Per-tenant
            controllers that read audit data explicitly apply a <code>tenantId</code> predicate
            before any other filter. This was the subject of FIX-M8C-001, identified and patched
            during our May 2026 pen-test.
          </p>
        </section>

        {/* Content moderation */}
        <section className="m-security-section" aria-labelledby="moderation-heading">
          <h2 id="moderation-heading">Content moderation</h2>
          <p>
            Every notification is scanned by <strong>Azure Content Safety</strong> before it
            is queued for delivery. Notifications that trigger moderation are placed in a
            pending review queue and are not delivered until a platform administrator reviews
            and approves them. Tenants can see that a notification is in moderation;
            they cannot bypass it.
          </p>
        </section>

        {/* Encryption at rest */}
        <section className="m-security-section" aria-labelledby="atrest-heading">
          <h2 id="atrest-heading">Encryption at rest</h2>
          <ul>
            <li><strong>Database</strong> — PostgreSQL 16 on AWS. Data at rest encrypted via AWS-managed AES-256 volume encryption.</li>
            <li><strong>Agent configuration</strong> — CLIENTID, SERVERURL, and device credentials stored on the endpoint using Windows DPAPI, scoped to the current user. Not readable by other users on the same machine.</li>
            <li><strong>API keys</strong> — Stored as salted SHA-256 hashes. The raw key is shown exactly once at creation. It cannot be recovered.</li>
          </ul>
        </section>

        {/* Logging */}
        <section className="m-security-section" aria-labelledby="logging-heading">
          <h2 id="logging-heading">Logging &amp; audit trail</h2>
          <p>
            Toast Notification maintains an <strong>append-only audit log</strong> for every
            tenant. Writes cannot be deleted by tenant users or administrators. The following
            events are recorded:
          </p>
          <ul>
            <li>Notification created, sent, delivered, clicked, dismissed, failed</li>
            <li>User login, logout, MFA enroll, MFA verify</li>
            <li>Device registration, heartbeat, decommission</li>
            <li>Template created, modified, deleted</li>
            <li>API key created, revoked</li>
            <li>Asset uploaded, moderated, deleted</li>
            <li>Tenant settings modified</li>
          </ul>
          <p>
            Audit records include timestamp (UTC), actor (user ID or device ID), action, and
            affected resource. Logs are exportable as CSV or PDF. Platform administrators
            have a cross-tenant audit view; tenant administrators see only their own tenant.
          </p>
          <p>
            <strong>Infrastructure logs</strong> (nginx access logs, systemd journal) are
            retained on-server and are not currently shipped to a SIEM. This is an honest
            gap — if you need a SIEM feed, contact us.
          </p>
        </section>

        {/* Code signing */}
        <section className="m-security-section" aria-labelledby="codesign-heading">
          <h2 id="codesign-heading">Code signing</h2>
          <p>
            The Windows agent MSI and MSIX are signed with an <strong>Organization Validation
            (OV) certificate</strong> issued by Sectigo. Signing is performed on a Thales
            hardware security module — the private key cannot be extracted.
            The published Store listing passes Windows certification review.
          </p>
          <p>
            The <code>WinVerifyTrust</code> API is called on the agent binary during startup
            to verify its own signature before loading. A tampered binary will fail this check
            and refuse to run.
          </p>
        </section>

        {/* Pen test */}
        <section className="m-security-section" aria-labelledby="pentest-heading">
          <h2 id="pentest-heading">Pen-test results — May 2026</h2>
          <p>
            We ran a structured security pen-test against the production API in May 2026,
            covering the following lanes:
          </p>
          <ul>
            <li><strong>Tenant isolation</strong> — device list, device by ID, notification send targeting, catch-up endpoint, audit log, hub group events. All passed except one finding.</li>
            <li><strong>Auth bypass</strong> — expired JWT, wrong signing key, missing device ID claim, user JWT on device endpoints, broadcast without MFA claim. All rejected correctly.</li>
            <li><strong>Content injection</strong> — XSS in body, oversized title, Unicode boundary, <code>&lt;/script&gt;</code> in body. All handled by Azure Content Safety or input validation.</li>
            <li><strong>Privilege escalation</strong> — Technician inviting users, Admin changing own role, Admin targeting other-tenant users, Technician broadcasting to 100+ devices, Admin without platform admin claim accessing system endpoints. All rejected correctly.</li>
          </ul>

          <div className="m-security-finding">
            <p className="m-security-finding-label">Finding patched in this session</p>
            <p>
              <strong>FIX-M8C-001 — AuditController cross-tenant read (MEDIUM)</strong>
            </p>
            <p>
              The <code>AuditController.List</code> and <code>AuditController.Export</code>
              endpoints were missing a <code>tenantId</code> predicate. An authenticated
              tenant admin could read audit log entries from other tenants by calling
              <code>GET /api/audit</code> without any filter parameter.
            </p>
            <p>
              <strong>Fix:</strong> Both endpoints now extract <code>tenantId</code> from
              the JWT claim and apply <code>.Where(l =&gt; l.TenantId == tenantId)</code>
              before any timestamp filter. A regression test was added that seeds two tenants,
              seeds audit rows for both, and asserts that Tenant A cannot see Tenant B&rsquo;s rows.
            </p>
          </div>

          <p>
            No other findings. The pen-test was conducted against the live production API using
            the same test harness committed to the repository at{' '}
            <code>tests/ToastRevival.Api.Tests/SecurityTests.cs</code> (20 test cases).
          </p>
        </section>

        {/* Responsible disclosure */}
        <section className="m-security-section" aria-labelledby="disclosure-heading">
          <h2 id="disclosure-heading">Responsible disclosure</h2>
          <p>
            Found something? Email{' '}
            <a href="mailto:security@toastnotification.com">security@toastnotification.com</a>.
            We&rsquo;ll respond within 48 hours. We don&rsquo;t have a bug bounty program
            (we&rsquo;re a small operation) but we&rsquo;ll credit you publicly if you want
            and we&rsquo;ll fix it fast.
          </p>
          <p>
            Please don&rsquo;t run automated scanners against the production environment.
            The test suite at <code>tests/ToastRevival.Api.Tests/SecurityTests.cs</code> is
            the right place to probe the security surface in a controlled way.
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
