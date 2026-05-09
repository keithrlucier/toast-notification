import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';

export default function DocsIntune() {
  useEffect(() => {
    document.title = 'Intune deployment - Toast Notification';
    const description =
      'Deploy the Toast Notification agent through Microsoft Intune as a Line-of-Business app. Includes assignment groups, detection rules, and tenant-ID delivery.';
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
      <h1>Intune (LOB)</h1>
      <p>
        Deploy the Toast Notification agent through Microsoft Intune as a Line-of-Business app. The MSIX is signed
        with our Sectigo OV certificate and works under default AppLocker and WDAC policies.
      </p>

      <h2 id="prerequisites">Prerequisites</h2>
      <ul>
        <li>Microsoft Intune license with Application Management permissions.</li>
        <li>Target endpoints enrolled in Intune and running Windows 10 build 19041 or Windows 11.</li>
        <li>The latest signed MSIX downloaded from the admin dashboard's Devices → Install agent tab.</li>
        <li>Your tenant GUID (Settings → Tenant on the admin dashboard).</li>
      </ul>

      <h2 id="upload-msix">Upload the MSIX</h2>
      <ol>
        <li>
          In the Intune portal, navigate to <strong>Apps → Windows → Add</strong> and select{' '}
          <strong>Line-of-business app</strong> as the app type.
        </li>
        <li>
          Select the signed MSIX file <code>ToastNotification.Agent-X.Y.Z.msix</code>. Intune extracts the publisher,
          version, and dependencies automatically from the manifest.
        </li>
        <li>
          On the <strong>App information</strong> tab, set:
          <ul>
            <li>
              <strong>Name:</strong> Toast Notification
            </li>
            <li>
              <strong>Publisher:</strong> Toast2IT, LLC
            </li>
            <li>
              <strong>Description:</strong> Managed Windows toast notifications for MSPs.
            </li>
            <li>
              <strong>Category:</strong> Productivity
            </li>
          </ul>
        </li>
        <li>
          Save the app to the Intune apps library.
        </li>
      </ol>

      <h2 id="deliver-tenant-id">Deliver the tenant ID</h2>
      <p>
        The MSIX itself does not embed a tenant GUID — every endpoint shares the same package. Configure the tenant ID
        per-endpoint with one of three options.
      </p>

      <h3>Option A — Intune environment variables policy (recommended)</h3>
      <p>
        Use a configuration profile to push <code>TOAST_TENANT_ID</code> and <code>TOAST_SERVER_URL</code> as user
        environment variables to all assigned endpoints.
      </p>
      <CodeBlock
        language="text"
        label="Intune configuration profile"
        code={`Name:        Toast Notification — Tenant Bootstrap
Platform:    Windows 10 and later
Profile:     Templates → Custom (OMA-URI)
OMA-URI:     ./User/Vendor/MSFT/Policy/Config/EnvironmentVariables
Data type:   String
Value:       TOAST_TENANT_ID=00000000-0000-0000-0000-000000000000
             TOAST_SERVER_URL=https://toastnotification.com`}
      />

      <h3>Option B — Per-user bootstrap.json via Win32 wrapper</h3>
      <p>
        For environments where the OMA-URI approach is not available, package a small Win32 app that writes a
        per-user <code>bootstrap.json</code> into the package LocalState directory and assign it as a dependency of
        the MSIX.
      </p>

      <h3>Option C — Provision through self-service</h3>
      <p>
        Push the MSIX without a tenant binding. Users enter their tenant ID in the agent's tray menu on first launch.
        Best for environments where tenant assignment varies per user.
      </p>

      <h2 id="assign">Assign to a group</h2>
      <ol>
        <li>
          On the app's <strong>Properties</strong> page, click <strong>Edit</strong> next to <strong>Assignments</strong>.
        </li>
        <li>
          Add the target Azure AD group under <strong>Required</strong> for forced install or{' '}
          <strong>Available for enrolled devices</strong> for opt-in via Company Portal.
        </li>
        <li>
          Save. Intune begins distributing the MSIX to assigned endpoints. Install confirmation appears in the
          per-endpoint device record under <strong>Apps → All apps → Toast Notification → Device install status</strong>.
        </li>
      </ol>

      <h2 id="detection">Detection rule</h2>
      <p>
        Intune detects the MSIX by package family name automatically — no custom detection rule is required. If you
        need explicit detection (for example to report version skew), use:
      </p>
      <CodeBlock
        language="powershell"
        label="custom detection script"
        code={`$pkg = Get-AppxPackage -Name 'Toast2IT.ToastNotification' -AllUsers
if ($pkg -and $pkg.Version -ge '0.4.0.0') { Write-Output 'Installed' }
exit 0`}
      />

      <Callout title="WDAC and AppLocker">
        <p>
          The agent MSIX is signed with a Sectigo OV certificate. Under default WDAC and AppLocker policies the
          package installs and runs without further configuration. Custom WDAC policies that whitelist publishers
          should include <code>O=Toast2IT, LLC, L=Tallahassee, S=Florida, C=US</code>.
        </p>
      </Callout>

      <h2 id="auto-update">Auto-update</h2>
      <p>
        Intune-managed MSIX installs receive updates through Intune's app update mechanism. Push a new app version to
        replace the existing one — endpoints update on the next sync. The Velopack in-process auto-updater is no-op for
        Intune-managed installs.
      </p>

      <h2 id="uninstall">Uninstall</h2>
      <p>
        Reassign the app to <strong>Uninstall</strong> for the target group, or remove the user / device from the
        assignment group. The MSIX uninstalls cleanly on the next Intune sync.
      </p>

      <div className="m-docs-next">
        <Link to="/docs/deploy/rmm">
          <span className="m-docs-next-label">Next</span>
          <span className="m-docs-next-title">RMM silent install →</span>
        </Link>
        <Link to="/docs/api">
          <span className="m-docs-next-label">Reference</span>
          <span className="m-docs-next-title">REST API →</span>
        </Link>
      </div>
    </article>
  );
}
