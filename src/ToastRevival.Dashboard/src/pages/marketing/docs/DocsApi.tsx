import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsApi() {
  useSeo({
    title: 'REST API reference',
    description:
      'Toast Notification REST API: authentication, devices, notifications, and delivery reporting. Bearer-token JWT, JSON over HTTPS, multi-tenant isolation enforced server-side.',
    path: '/docs/api',
    jsonLd: [
      techArticleLd({
        headline: 'Toast Notification REST API reference',
        description:
          'Authentication, devices, notifications, delivery reporting. Bearer-token JWT, JSON over HTTPS, multi-tenant isolation server-side.',
        path: '/docs/api',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
        { name: 'API reference', path: '/docs/api' },
      ]),
    ],
  });

  return (
    <article>
      <h1>REST API reference</h1>
      <p>
        The Toast Notification API is JSON over HTTPS. Authentication is bearer-token JWT — separate token classes for
        users and devices. Tenant isolation is enforced by EF Core query filters on every read; cross-tenant access is
        not possible through any public endpoint.
      </p>

      <h2 id="conventions">Conventions</h2>
      <ul>
        <li>
          Base URL: <code>https://toastnotification.com/api</code>
        </li>
        <li>
          Content type: <code>application/json; charset=utf-8</code>
        </li>
        <li>
          Authentication: <code>Authorization: Bearer &lt;jwt&gt;</code>
        </li>
        <li>
          Errors: standard HTTP status codes. <code>400</code> validation, <code>401</code> unauthenticated,{' '}
          <code>403</code> forbidden, <code>404</code> not found, <code>409</code> conflict, <code>429</code> rate
          limited, <code>5xx</code> server error.
        </li>
        <li>
          Identifiers: <code>Guid</code> in canonical lowercase format. Timestamps:{' '}
          <code>ISO 8601 / RFC 3339</code> in UTC.
        </li>
      </ul>

      <h2 id="authentication">Authentication</h2>

      <h3>POST /api/auth/register/init</h3>
      <p>
        Submit a reviewed trial request. This public endpoint validates Turnstile, stores the company/contact details,
        and waits for Platform Admin approval before any tenant or user is created.
      </p>
      <CodeBlock
        language="json"
        label="request body"
        code={`{
  "companyName": "Acme MSP",
  "website": "https://acme-msp.com",
  "fullName": "Jane Smith",
  "email": "owner@acme-msp.com",
  "phone": "+1 555 000 0000",
  "jobTitle": "Service Desk Manager",
  "intendedUseCase": "MspClientCommunication",
  "intendedUseCaseDetails": "Client maintenance and security notices",
  "turnstileToken": "<token>"
}`}
      />
      <CodeBlock
        language="json"
        label="response"
        code={`{
  "requestId": "00000000-0000-0000-0000-000000000000",
  "step": "pending_review",
  "message": "Thanks. Your trial request is pending review."
}`}
      />

      <h3>POST /api/auth/login</h3>
      <p>Exchange credentials for an 8-hour user JWT. Public endpoint.</p>
      <CodeBlock
        language="json"
        label="request body"
        code={`{ "email": "owner@acme-msp.com", "password": "********" }`}
      />
      <CodeBlock
        language="json"
        label="response"
        code={`{
  "userToken": "eyJhbGciOi...",
  "expiresAt": "2026-05-09T08:00:00Z",
  "user": { "id": "...", "role": "SuperAdmin", "tenantId": "..." }
}`}
      />

      <Callout title="Token lifetimes">
        <p>
          User tokens expire after 8 hours; refresh by re-issuing through <code>POST /api/auth/login</code>. Device
          tokens expire after 365 days and are rotated by re-registering the device.
        </p>
      </Callout>

      <h2 id="devices">Devices</h2>
      <p>
        The agent registers itself once per machine. Subsequent calls use the device JWT to ping, report deliveries,
        and pull pending notifications.
      </p>

      <table className="m-docs-endpoint-table">
        <thead>
          <tr>
            <th>Endpoint</th>
            <th>Auth</th>
            <th>Purpose</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <span className="m-docs-method">POST</span>/api/devices/register
            </td>
            <td>None</td>
            <td>Agent self-registers, returns device JWT and per-tenant signing key.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">GET</span>/api/devices
            </td>
            <td>User</td>
            <td>List all devices in the caller's tenant.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">GET</span>/api/devices/{'{id}'}
            </td>
            <td>User</td>
            <td>Get a single device record.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">DELETE</span>/api/devices/{'{id}'}
            </td>
            <td>User (admin)</td>
            <td>Decommission a device. Frees the seat for billing.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">POST</span>/api/devices/ping
            </td>
            <td>Device</td>
            <td>30-minute heartbeat that updates LastPing.</td>
          </tr>
        </tbody>
      </table>

      <h3>POST /api/devices/register</h3>
      <CodeBlock
        language="json"
        label="request body"
        code={`{
  "tenantId":     "00000000-0000-0000-0000-000000000000",
  "machineName":  "WIN-LAB-01",
  "userName":     "alice",
  "operatingSystem": "Windows 11 23H2 (10.0.22631)",
  "agentVersion": "0.4.38"
}`}
      />
      <CodeBlock
        language="json"
        label="response"
        code={`{
  "deviceId":     "10000000-0000-0000-0000-000000000000",
  "deviceToken":  "eyJhbGciOi...",
  "signingKey":   "base64-encoded-32-byte-key",
  "serverUrl":    "https://toastnotification.com"
}`}
      />

      <h2 id="notifications">Notifications</h2>

      <table className="m-docs-endpoint-table">
        <thead>
          <tr>
            <th>Endpoint</th>
            <th>Auth</th>
            <th>Purpose</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <span className="m-docs-method">POST</span>/api/notifications
            </td>
            <td>User</td>
            <td>Create a notification. Expand target list. Enqueue for fan-out.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">GET</span>/api/notifications
            </td>
            <td>User</td>
            <td>List notifications in the caller's tenant.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">GET</span>/api/notifications/{'{id}'}
            </td>
            <td>User</td>
            <td>Detail with per-device delivery rows.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">GET</span>/api/notifications/pending
            </td>
            <td>Device</td>
            <td>Pull missed notifications since a timestamp. Catch-up after offline windows.</td>
          </tr>
          <tr>
            <td>
              <span className="m-docs-method">POST</span>/api/notifications/{'{id}'}/interactions
            </td>
            <td>Device</td>
            <td>Report a click or dismissal. Activation-handler fallback when SignalR is unavailable.</td>
          </tr>
        </tbody>
      </table>

      <h3>POST /api/notifications</h3>
      <CodeBlock
        language="json"
        label="request body"
        code={`{
  "templateId":   "20000000-0000-0000-0000-000000000000",
  "title":        "Maintenance window tonight",
  "bodyLine1":    "Backups pause from 22:00 to 02:00 ET.",
  "bodyLine2":    "Contact the help desk for exceptions.",
  "scenario":     "reminder",
  "heroImageUrl": "https://cdn.acme-msp.com/maintenance.png",
  "actionButtons": [
    { "text": "Acknowledge", "action": "acknowledge" },
    { "text": "Open ticket", "action": "open-ticket" }
  ],
  "target":       { "type": "DeviceGroup", "id": "30000000-..." }
}`}
      />
      <CodeBlock
        language="json"
        label="response"
        code={`{
  "id":           "40000000-0000-0000-0000-000000000000",
  "createdAt":    "2026-05-09T15:30:00Z",
  "status":       "Queued",
  "targetCount":  127
}`}
      />

      <Callout title="Per-tenant HMAC signing">
        <p>
          Every payload is signed with the tenant's HMAC-SHA256 key before fan-out. The signature accompanies the
          payload as a separate SignalR argument; the agent verifies in constant time before render. Forgery requires
          the per-tenant signing key, which never leaves the server in cleartext outside of the device registration
          response.
        </p>
      </Callout>

      <h2 id="delivery-status">Delivery status &amp; reporting</h2>
      <p>
        Toast Notification does not push outbound webhooks. Delivery and interaction state is reported by the agent
        and read back through the API and dashboard:
      </p>
      <ul>
        <li>
          <code>GET /api/notifications/{'{id}'}</code> returns the notification with one delivery row per device —{' '}
          <code>Pending</code>, <code>Delivered</code>, <code>Clicked</code>, <code>Dismissed</code>, or{' '}
          <code>Failed</code>.
        </li>
        <li>
          The dashboard streams the same delivery and interaction events live over SignalR as the agent reports them.
        </li>
        <li>
          Aggregate delivery and interaction rates, plus the full per-tenant audit log, export to CSV and PDF from the
          dashboard for tickets and incident review.
        </li>
      </ul>

      <h2 id="rate-limits">Rate limits</h2>
      <ul>
        <li>
          User-authenticated endpoints: 60 requests / minute per tenant, sliding window.
        </li>
        <li>
          Device-authenticated endpoints: 10 requests / hour per device, fixed window.
        </li>
        <li>
          On 429, the response includes <code>Retry-After</code> in seconds. Honor it.
        </li>
      </ul>

      <div className="m-docs-next">
        <Link to="/docs/getting-started">
          <span className="m-docs-next-label">Back to</span>
          <span className="m-docs-next-title">Getting started</span>
        </Link>
        <Link to="/docs">
          <span className="m-docs-next-label">Back to</span>
          <span className="m-docs-next-title">Documentation home</span>
        </Link>
      </div>
    </article>
  );
}
