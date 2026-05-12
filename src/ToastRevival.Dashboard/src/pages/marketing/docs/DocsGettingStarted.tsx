import { Link } from 'react-router-dom';
import { CodeBlock } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsGettingStarted() {
  useSeo({
    title: 'Getting started',
    description:
      'Request trial access, install the Windows agent after approval, and send your first toast notification.',
    path: '/docs/getting-started',
    jsonLd: [
      techArticleLd({
        headline: 'Getting started with Toast Notification',
        description:
          'From approved trial to first delivered notification in four steps: set password, open install values, install agent, send notification.',
        path: '/docs/getting-started',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
        { name: 'Getting started', path: '/docs/getting-started' },
      ]),
    ],
  });

  return (
    <article>
      <h1>Getting started</h1>
      <p>
        From approved trial to first delivered notification in four steps. Plan on ten minutes including the agent
        install on a single test endpoint after access is approved.
      </p>

      <h2 id="prerequisites">Prerequisites</h2>
      <ul>
        <li>A Windows 10 (build 19041 or later) or Windows 11 endpoint with administrative install rights.</li>
        <li>An email address you can receive mail at — used for tenant registration and the admin login.</li>
        <li>A modern browser for the admin dashboard. Chrome, Edge, Firefox, and Safari are all supported.</li>
      </ul>

      <h2 id="step-1-register">Step 1 - Request access</h2>
      <p>
        Open <Link to="/register">toastnotification.com/register</Link>. Provide company, website, contact telephone,
        job title, and intended use case. The request is reviewed before a tenant or administrator account is created.
      </p>
      <p>
        After approval, you receive a password setup email. That creates the first tenant owner account with access
        to the dashboard, MSI download, tenant ID, and enrollment key.
      </p>

      <h2 id="step-2-tenant-id">Step 2 — Note your tenant ID and server URL</h2>
      <p>
        On the admin dashboard, open <strong>Install Agent</strong>. The Tenant ID is the GUID you will pass to the
        agent installer. The Server URL is <code>https://toastnotification.com</code> for production tenants, and the
        enrollment key is unique to your tenant.
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
        <strong>Install Agent</strong> page. From an elevated PowerShell prompt:
      </p>
      <CodeBlock
        language="powershell"
        code={`msiexec /i ToastNotification.Agent-0.4.0.0.msi /qn \`
  CLIENTID=00000000-0000-0000-0000-000000000000 \`
  SERVERURL=https://toastnotification.com \`
  ENROLLMENTKEY=<tenant-enrollment-key>`}
      />
      <p>
        Replace the <code>CLIENTID</code> and <code>ENROLLMENTKEY</code> with the values shown on your Install Agent page.
        The MSI registers a <code>SCHEDULED TASK</code> at{' '}
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
