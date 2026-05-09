import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsApi() {
  useSeo({
    title: 'REST API reference',
    description:
      'Toast Notification REST API: authentication, devices, notifications, and webhooks. Bearer-token JWT, JSON over HTTPS, multi-tenant isolation enforced server-side.',
    path: '/docs/api',
    jsonLd: [
      techArticleLd({
        headline: 'Toast Notification REST API reference',
        description:
          'Authentication, devices, notifications, webhooks. Bearer-token JWT, JSON over HTTPS, multi-tenant isolation server-side.',
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

      <h3>POST /api/auth/register</h3>
      <p>Create a new tenant and the first administrator. Public endpoint.</p>
      <CodeBlock
        language="json"
        label="request body"
        code={`{
  "organizationName": "Acme MSP",
  "email": "owner@acme-msp.com",
  "password": "********"
}`}
      />
      <CodeBlock
        language="json"
        label="response"
        code={`{
  "userToken": "eyJhbGciOi...",
  "expiresAt": "2026-05-09T08:00:00Z",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "user": { "id": "...", "email": "owner@acme-msp.com", "role": "SuperAdmin" }
}`}
      />

      <h3>POST /api/auth/login</h3>
      <p>Exchange credentials for a 60-minute user JWT. Public endpoint.</p>
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
          User tokens expire after 60 minutes; refresh by re-issuing through <code>POST /api/auth/login</code>. Device
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
  "agentVersion": "0.4.0.0"
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

      <h2 id="webhooks">Webhooks</h2>
      <p>
        Webhooks fire on notification lifecycle events. Configure the URL and signing secret under{' '}
        <strong>Settings → API keys → Webhooks</strong> on the admin dashboard.
      </p>

      <h3>Events</h3>
      <ul>
        <li>
          <code>notification.delivered</code> — agent reported successful render.
        </li>
        <li>
          <code>notification.interacted</code> — user clicked an action button or dismissed.
        </li>
        <li>
          <code>notification.failed</code> — agent reported render failure or signature mismatch.
        </li>
        <li>
          <code>device.registered</code> — new device successfully registered.
        </li>
        <li>
          <code>device.decommissioned</code> — device removed.
        </li>
      </ul>

      <h3>Signature verification</h3>
      <p>
        Each webhook delivery includes an <code>X-Toast-Signature</code> header. The signature is the HMAC-SHA256 of
        the raw request body, hex-encoded, using the webhook signing secret. Verify in the receiver before processing
        the payload.
      </p>
      <CodeBlock
        language="javascript"
        label="Node.js example"
        code={`const crypto = require('crypto');

function verify(rawBody, header, secret) {
  const computed = crypto
    .createHmac('sha256', secret)
    .update(rawBody)
    .digest('hex');
  return crypto.timingSafeEqual(
    Buffer.from(computed, 'hex'),
    Buffer.from(header, 'hex')
  );
}`}
      />

      <h3>Retries</h3>
      <p>
        Failed deliveries (non-2xx responses or timeouts beyond 10 seconds) retry with exponential backoff: 30s, 5m,
        30m, 2h, 6h. After five attempts the delivery is marked failed and surfaced in the dashboard's webhook log.
      </p>

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
