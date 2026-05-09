import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';

export default function DocsGettingStarted() {
  useEffect(() => {
    document.title = 'Getting started - Toast Notification';
    const description =
      'Sign up, register your tenant, install the Windows agent, and send your first toast notification in under ten minutes.';
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
      <h1>Getting started</h1>
      <p>
        From empty browser to first delivered notification in four steps. Plan on ten minutes including the agent
        install on a single test endpoint.
      </p>

      <h2 id="prerequisites">Prerequisites</h2>
      <ul>
        <li>A Windows 10 (build 19041 or later) or Windows 11 endpoint with administrative install rights.</li>
        <li>An email address you can receive mail at — used for tenant registration and the admin login.</li>
        <li>A modern browser for the admin dashboard. Chrome, Edge, Firefox, and Safari are all supported.</li>
      </ul>

      <h2 id="step-1-register">Step 1 — Register a tenant</h2>
      <p>
        Open <Link to="/register">toastnotification.com/register</Link>. Provide your organization name, your email,
        and a password. The first account on a tenant is the tenant administrator and can invite additional users
        from the admin dashboard.
      </p>
      <p>
        Registration creates the tenant record, your administrator user, six pre-built notification templates, and
        a per-tenant HMAC signing key. You are routed straight to the admin dashboard.
      </p>

      <Callout title="14-day trial">
        <p>
          New tenants start a 14-day trial during Stripe checkout. Add up to 100 devices during the trial and the
          monthly minimum applies after the trial converts.
        </p>
      </Callout>

      <h2 id="step-2-tenant-id">Step 2 — Note your tenant ID and server URL</h2>
      <p>
        On the admin dashboard, open <strong>Settings → Tenant</strong>. The Tenant ID is the GUID you will pass to the
        agent installer. The Server URL is <code>https://toastnotification.com</code> for production tenants.
      </p>
      <p>
        These two values flow into the agent's <code>bootstrap.json</code> at install time. The agent also reads them
        from the environment variables <code>TOAST_TENANT_ID</code> and <code>TOAST_SERVER_URL</code> if present.
      </p>

      <h2 id="step-3-install-agent">Step 3 — Install the agent</h2>
      <p>
        Pick the install path that matches your environment. For a single test endpoint, the signed MSI is the
        fastest path. For fleet deployment, jump to{' '}
        <Link to="/docs/deploy/intune">Intune</Link> or <Link to="/docs/deploy/rmm">RMM silent install</Link>.
      </p>

      <h3>Signed MSI (single endpoint)</h3>
      <p>
        Download the latest <code>ToastNotification.Agent-X.Y.Z.0.msi</code> from the admin dashboard's{' '}
        <strong>Devices → Install agent</strong> tab. From an elevated PowerShell prompt:
      </p>
      <CodeBlock
        language="powershell"
        code={`msiexec /i ToastNotification.Agent-0.4.0.0.msi /qn \`
  CLIENTID=00000000-0000-0000-0000-000000000000 \`
  SERVERURL=https://toastnotification.com`}
      />
      <p>
        Replace the <code>CLIENTID</code> with your tenant GUID. The MSI registers a <code>SCHEDULED TASK</code> at{' '}
        <code>\Toast2IT\ToastNotificationAgentLogon</code> that launches the agent in the user's context at next
        logon.
      </p>

      <h3>MSIX from the Microsoft Store</h3>
      <p>
        See the <Link to="/docs/deploy/store">Microsoft Store guide</Link>.
      </p>

      <h2 id="step-4-first-notification">Step 4 — Send your first notification</h2>
      <p>
        Back on the admin dashboard, open <strong>Compose</strong>. Pick the <code>Announcement</code> template, fill in
        a title and body, choose the device you just registered, and press <strong>Send</strong>. The toast renders on
        the endpoint within a second of fan-out.
      </p>
      <p>
        Open <strong>History</strong> to see the delivery and interaction record. Each delivery is reported back from
        the agent in real time over SignalR.
      </p>

      <h2 id="next-steps">Next steps</h2>
      <ul>
        <li>
          Configure additional users and roles under <strong>Users</strong>.
        </li>
        <li>
          Build device groups under <strong>Devices → Groups</strong> for targeted broadcast.
        </li>
        <li>
          Upload tenant-branded hero images and logos under <strong>Assets</strong>.
        </li>
        <li>
          Customize the six pre-built templates under <strong>Templates</strong>.
        </li>
        <li>
          Plan a fleet rollout with <Link to="/docs/deploy/intune">Intune</Link>{' '}
          or <Link to="/docs/deploy/rmm">RMM silent install</Link>.
        </li>
      </ul>

      <div className="m-docs-next">
        <Link to="/docs/deploy/store">
          <span className="m-docs-next-label">Next</span>
          <span className="m-docs-next-title">Microsoft Store →</span>
        </Link>
        <Link to="/docs/api">
          <span className="m-docs-next-label">Reference</span>
          <span className="m-docs-next-title">REST API →</span>
        </Link>
      </div>
    </article>
  );
}
