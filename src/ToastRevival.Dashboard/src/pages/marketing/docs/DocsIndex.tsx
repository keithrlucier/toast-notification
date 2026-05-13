import { Link } from 'react-router-dom';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

const HUB_LINKS = [
  {
    to: '/docs/getting-started',
    title: 'Getting started',
    body: 'Request access, set your password after approval, install the agent on a Windows endpoint, and send your first notification.',
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
    body: 'msiexec with CLIENTID, SERVERURL, and ENROLLMENTKEY properties. Tested with NinjaOne, Datto, ConnectWise Automate, Atera, and any RMM that supports silent MSI deployment.',
  },
  {
    to: '/docs/api',
    title: 'REST API reference',
    body: 'Authentication, devices, notifications, and webhooks. Bearer-token JWT for users and devices. JSON over HTTPS.',
  },
];

export default function DocsIndex() {
  useSeo({
    title: 'Documentation',
    description:
      'Toast Notification documentation: getting started, deployment guides for Microsoft Store, Intune, and RMM, and the REST API reference.',
    path: '/docs',
    jsonLd: [
      techArticleLd({
        headline: 'Toast Notification documentation',
        description:
          'Getting started, deployment guides for Microsoft Store, Intune, and RMM, and the REST API reference.',
        path: '/docs',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
      ]),
    ],
  });

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
        Toast Notification has three components. The <strong>Windows Agent</strong> installs from a signed MSI or MSIX,
        registers itself with the service, and renders notifications through the native Windows notification surface.
        The <strong>multi-tenant service</strong> issues JWTs, signs every payload with a per-tenant HMAC-SHA256 key,
        delivers notifications in real time, and tracks delivery and interaction state. The <strong>admin dashboard</strong>{' '}
        at <code>toastnotification.com</code> lets tenant administrators compose notifications, manage device groups,
        review audit logs, and configure billing.
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
          Tenant isolation is enforced on tenant-facing reads and writes. Cross-tenant requests are not permitted
          through any public endpoint.
        </li>
        <li>
          Rate limiting is per-tenant (60 requests / minute) and per-device (10 requests / hour) on hot paths. A 429
          response means back off and retry after the <code>Retry-After</code> header value.
        </li>
      </ul>

      <h2 id="security-posture">Security posture</h2>
      <ul>
        <li>TLS 1.2/1.3 with HSTS and HTTPS redirect. Certificate renewal is handled server-side.</li>
        <li>Per-tenant HMAC-SHA256 payload signing. The agent verifies every notification before render.</li>
        <li>Tenant blocklists are enforced before delivery. Configured content-safety checks score eligible text and asset inputs.</li>
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
