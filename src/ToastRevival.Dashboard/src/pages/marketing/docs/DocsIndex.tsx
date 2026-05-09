import { useEffect } from 'react';
import { Link } from 'react-router-dom';

const HUB_LINKS = [
  {
    to: '/docs/getting-started',
    title: 'Getting started',
    body: 'Sign up, register a tenant, install the agent on a Windows endpoint, and send your first notification in under ten minutes.',
  },
  {
    to: '/docs/deploy/store',
    title: 'Microsoft Store',
    body: 'Install the signed MSIX from the Microsoft Store. Best for individual users, BYOD endpoints, and quick proof-of-concept rollouts.',
  },
  {
    to: '/docs/deploy/intune',
    title: 'Intune (LOB)',
    body: 'Upload the signed MSIX as a Line-of-Business app. The MDM-managed corporate path with assignment groups and silent install.',
  },
  {
    to: '/docs/deploy/rmm',
    title: 'RMM silent install',
    body: 'msiexec with CLIENTID and SERVERURL properties. Tested with NinjaOne, Datto, ConnectWise Automate, Atera, and any RMM that supports silent MSI deployment.',
  },
  {
    to: '/docs/api',
    title: 'REST API reference',
    body: 'Authentication, devices, notifications, and webhooks. Bearer-token JWT for users and devices. JSON over HTTPS.',
  },
];

export default function DocsIndex() {
  useEffect(() => {
    document.title = 'Documentation - Toast Notification';
    const description =
      'Toast Notification documentation: getting started, deployment guides for Microsoft Store, Intune, and RMM, and the REST API reference.';
    let meta = document.querySelector('meta[name="description"]');
    if (!meta) {
      meta = document.createElement('meta');
      meta.setAttribute('name', 'description');
      document.head.appendChild(meta);
    }
    meta.setAttribute('content', description);
  }, []);

  return (
    <article>
      <h1>Documentation</h1>
      <p>
        Everything you need to deploy Toast Notification, register Windows endpoints, send branded toasts, and integrate
        with the REST API. Start with the getting-started guide if you have not yet provisioned a tenant.
      </p>

      <h2 id="quick-links">Quick links</h2>
      <div className="m-docs-hub-grid">
        {HUB_LINKS.map((link) => (
          <Link key={link.to} to={link.to} className="m-docs-hub-card">
            <h3>{link.title}</h3>
            <p>{link.body}</p>
          </Link>
        ))}
      </div>

      <h2 id="how-the-platform-fits-together">How the platform fits together</h2>
      <p>
        Toast Notification has three components. The <strong>Windows Agent</strong> is a signed .NET 8 / Windows App SDK
        application that installs from a signed MSI or MSIX, registers itself with the API, and renders notifications
        through the native Windows App Notification surface. The <strong>multi-tenant API</strong> is an ASP.NET Core 8
        service backed by PostgreSQL; it issues JWTs, signs every payload with a per-tenant HMAC-SHA256 key, fans
        notifications out over SignalR, and tracks delivery and interaction state. The <strong>admin dashboard</strong>{' '}
        is a React SPA at <code>toastnotification.com</code> for tenant administrators to compose notifications, manage
        device groups, review audit logs, and configure billing.
      </p>

      <h2 id="endpoint-conventions">Endpoint conventions</h2>
      <ul>
        <li>
          API base: <code>https://toastnotification.com/api</code>. SignalR hub:{' '}
          <code>https://toastnotification.com/hubs/notifications</code>.
        </li>
        <li>
          User JWTs are issued by <code>POST /api/auth/login</code> and expire after 60 minutes. Device JWTs are issued
          by <code>POST /api/devices/register</code> and expire after 365 days.
        </li>
        <li>
          Tenant isolation is enforced by EF Core query filters on every read. Cross-tenant requests are not possible
          through any public endpoint.
        </li>
        <li>
          Rate limiting is per-tenant (60 requests / minute) and per-device (10 requests / hour) on hot paths. A 429
          response means back off and retry after the <code>Retry-After</code> header value.
        </li>
      </ul>

      <h2 id="security-posture">Security posture</h2>
      <ul>
        <li>TLS 1.3 with HSTS via Let's Encrypt. Certificate auto-renewal handled server-side.</li>
        <li>Per-tenant HMAC-SHA256 payload signing. The agent verifies every notification before render.</li>
        <li>Azure Content Safety scans every notification before fan-out. Blocked sends are logged.</li>
        <li>TOTP MFA enforced on broadcast (target = all devices) sends.</li>
        <li>Append-only audit log with CSV and PDF export for incident review and compliance attestation.</li>
      </ul>

      <div className="m-docs-next">
        <Link to="/docs/getting-started">
          <span className="m-docs-next-label">Next</span>
          <span className="m-docs-next-title">Getting started →</span>
        </Link>
      </div>
    </article>
  );
}
