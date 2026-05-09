import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';

export default function DocsStore() {
  useEffect(() => {
    document.title = 'Microsoft Store deployment - Toast Notification';
    const description =
      'Install the Toast Notification agent from the Microsoft Store. Best for individual users, BYOD endpoints, and quick proof-of-concept rollouts.';
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
      <h1>Microsoft Store</h1>
      <p>
        Install the signed MSIX from the Microsoft Store. This is the lowest-friction path for individual users and
        BYOD endpoints, and the path most likely to satisfy AppLocker / WDAC policies because the package is
        Microsoft-signed.
      </p>

      <h2 id="who-this-is-for">Who this is for</h2>
      <ul>
        <li>Individual users on personal or unmanaged Windows endpoints.</li>
        <li>BYOD endpoints in environments without MDM or RMM enrollment.</li>
        <li>Quick demos and proof-of-concept rollouts before fleet deployment.</li>
      </ul>
      <p>
        For MDM-managed corporate endpoints, see <Link to="/docs/deploy/intune">Intune</Link>. For silent install
        across a managed fleet, see <Link to="/docs/deploy/rmm">RMM silent install</Link>.
      </p>

      <h2 id="install">Install</h2>
      <ol>
        <li>
          Open the <strong>Microsoft Store</strong> on the target endpoint.
        </li>
        <li>
          Search for <strong>Toast Notification</strong> and select the listing published by{' '}
          <strong>Toast2IT, LLC</strong>.
        </li>
        <li>
          Click <strong>Install</strong>. The agent installs into your user profile and is ready to register on first
          launch.
        </li>
      </ol>

      <h2 id="register-the-tenant">Register the tenant</h2>
      <p>
        The Microsoft Store install path does not embed your tenant GUID. Provide it in one of two ways before first
        launch.
      </p>

      <h3>Option A — Environment variables (recommended)</h3>
      <p>
        Set two user environment variables, then start the agent from Start Menu:
      </p>
      <CodeBlock
        language="powershell"
        code={`[Environment]::SetEnvironmentVariable('TOAST_TENANT_ID',  '00000000-0000-0000-0000-000000000000', 'User')
[Environment]::SetEnvironmentVariable('TOAST_SERVER_URL', 'https://toastnotification.com', 'User')`}
      />
      <p>
        The agent reads these variables on first launch, registers the device with the API, persists the device JWT to{' '}
        <code>%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification_*\LocalState\config.json</code>, and connects to the
        SignalR hub.
      </p>

      <h3>Option B — bootstrap.json</h3>
      <p>
        Place a <code>bootstrap.json</code> file in the agent's package LocalState directory before first launch:
      </p>
      <CodeBlock
        language="json"
        code={`{
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "serverUrl": "https://toastnotification.com"
}`}
      />

      <h2 id="auto-update">Auto-update</h2>
      <p>
        Microsoft Store installs auto-update through the Store itself. The Velopack auto-updater is no-op when the
        package is installed via Store (<code>IsInstalled = false</code> in Velopack terms). New agent versions roll
        out through the Store update queue with no per-endpoint action required.
      </p>

      <Callout kind="warning" title="Store-managed updates">
        <p>
          Disabling automatic updates in the Microsoft Store will pause Toast Notification agent updates as well. For
          centrally-controlled update policy, deploy via Intune LOB or RMM silent install instead.
        </p>
      </Callout>

      <h2 id="uninstall">Uninstall</h2>
      <p>
        Settings → Apps → Installed apps → Toast Notification → Uninstall. The package and all per-package state are
        removed cleanly. The device row in the API is preserved with status <code>Decommissioned</code> for audit
        purposes; re-registering the same machine reuses the slot for billing.
      </p>

      <div className="m-docs-next">
        <Link to="/docs/deploy/intune">
          <span className="m-docs-next-label">Next</span>
          <span className="m-docs-next-title">Intune (LOB) →</span>
        </Link>
        <Link to="/docs/deploy/rmm">
          <span className="m-docs-next-label">Or</span>
          <span className="m-docs-next-title">RMM silent install →</span>
        </Link>
      </div>
    </article>
  );
}
