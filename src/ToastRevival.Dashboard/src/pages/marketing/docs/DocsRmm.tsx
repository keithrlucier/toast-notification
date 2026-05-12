import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsRmm() {
  useSeo({
    title: 'RMM silent install',
    description:
      'Silent MSI install of the Toast Notification agent through NinjaOne, Datto, ConnectWise Automate, Atera, and any RMM that supports msiexec.',
    path: '/docs/deploy/rmm',
    jsonLd: [
      techArticleLd({
        headline: 'RMM silent install',
        description:
          'Silent MSI install with CLIENTID, SERVERURL, and ENROLLMENTKEY properties. Tested with NinjaOne, Datto, ConnectWise Automate, and Atera.',
        path: '/docs/deploy/rmm',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
        { name: 'RMM', path: '/docs/deploy/rmm' },
      ]),
    ],
  });

  return (
    <article>
      <h1>RMM silent install</h1>
      <p>
        Deploy the signed MSI through any RMM that runs scheduled scripts as <code>SYSTEM</code> with administrative
        rights. Tenant ID, server URL, and enrollment key are passed as MSI public properties at install time.
      </p>

      <h2 id="install-command">Install command</h2>
      <CodeBlock
        language="powershell"
        code={`msiexec /i "C:\\Temp\\ToastNotification.Agent-0.4.0.0.msi" /qn /norestart \`
  CLIENTID=00000000-0000-0000-0000-000000000000 \`
  SERVERURL=https://toastnotification.com \`
  ENROLLMENTKEY=<tenant-enrollment-key>`}
      />
      <p>
        The public properties <code>CLIENTID</code>, <code>SERVERURL</code>, and <code>ENROLLMENTKEY</code> are written to{' '}
        <code>%ProgramData%\Toast2IT\Toast Notification\bootstrap.json</code> by a deferred custom action during the
        InstallFiles sequence. The agent reads them on first launch in the user's context, registers the device, and
        connects to the SignalR hub.
      </p>

      <Callout title="Properties are public, not secret">
        <p>
          <code>CLIENTID</code>, <code>SERVERURL</code>, and <code>ENROLLMENTKEY</code> are MSI public properties and
          can appear in install logs. The enrollment key limits new device registration for that tenant; rotate it from
          Tenant Settings if an install command is exposed.
        </p>
      </Callout>

      <h2 id="ninjaone">NinjaOne</h2>
      <ol>
        <li>
          Upload <code>ToastNotification.Agent-X.Y.Z.0.msi</code> to <strong>Configuration → Documents</strong>.
        </li>
        <li>
          Create a script under <strong>Configuration → Scripts</strong> with the install command above. Use the
          NinjaOne <code>$Document$</code> token for the MSI path.
        </li>
        <li>
          Add a policy item that runs the script on first endpoint check-in for organizations whose tenant GUID matches
          the <code>CLIENTID</code> value.
        </li>
      </ol>

      <h2 id="datto-rmm">Datto RMM</h2>
      <ol>
        <li>
          In <strong>Component Library → New Component → Application</strong>, upload the MSI.
        </li>
        <li>
          Set the install command to the line above, with <code>$(WorkDir)</code> as the file root.
        </li>
        <li>
          Schedule the component to run as a one-off job against the target site.
        </li>
      </ol>

      <h2 id="connectwise-automate">ConnectWise Automate</h2>
      <ol>
        <li>
          In the <strong>Plug-In Manager → Software Manager</strong>, register the MSI with the install command above.
        </li>
        <li>
          Assign to a group and run as a scheduled push. Automate's MSI handler captures the exit code; expect{' '}
          <code>0</code> on success.
        </li>
      </ol>

      <h2 id="atera">Atera</h2>
      <ol>
        <li>
          In <strong>Library → Scripts</strong>, create a PowerShell script wrapping the install command above. Use the
          Atera built-in file delivery to drop the MSI on the endpoint before invoking <code>msiexec</code>.
        </li>
        <li>
          Schedule the script against the target customer or device group.
        </li>
      </ol>

      <h2 id="generic-msiexec">Any RMM that supports msiexec</h2>
      <p>
        The install is plain <code>msiexec /i /qn</code>. As long as the RMM can deliver the MSI to{' '}
        <code>%TEMP%</code> on each endpoint and run a scheduled command as <code>SYSTEM</code>, the install completes
        in under sixty seconds. The exit code is the standard Windows Installer return code:
      </p>
      <ul>
        <li>
          <code>0</code> — Install succeeded.
        </li>
        <li>
          <code>1641</code> — Install succeeded, restart pending. The agent does not require restart but the MSI
          declares the property defensively.
        </li>
        <li>
          <code>1638</code> — Another version is installed. Major-upgrade rules apply: a higher version replaces a
          lower one with no further action.
        </li>
        <li>
          <code>1603</code> — Fatal error. Capture the install log with <code>/L*v</code>:
        </li>
      </ul>
      <CodeBlock
        language="powershell"
        code={`msiexec /i ToastNotification.Agent-0.4.0.0.msi /qn /norestart \`
  /L*v "%ProgramData%\\Toast2IT\\install.log" \`
  CLIENTID=... SERVERURL=https://toastnotification.com ENROLLMENTKEY=...`}
      />
      <p>
        Log to a tenant-known path so the RMM can collect it for triage. The agent's own diagnostic log is at{' '}
        <code>%LOCALAPPDATA%\Toast2IT\Toast Notification\diag.log</code> once it has run for the first time.
      </p>

      <h2 id="uninstall">Uninstall</h2>
      <CodeBlock
        language="powershell"
        code={`msiexec /x "{PRODUCT-CODE}" /qn /norestart`}
      />
      <p>
        The MSI's product code is published in the latest release notes on the admin dashboard. Uninstall removes the
        scheduled task, deletes <code>bootstrap.json</code>, and unregisters the device from the API.
      </p>

      <h2 id="auto-update">Auto-update</h2>
      <p>
        The MSI installs the Velopack in-process auto-updater. Once a day the agent checks the release feed at{' '}
        <code>https://releases.toastnotification.com/agent/win-x64</code>; new versions download in the background and
        apply on next agent restart. To disable per-tenant policy, set the registry value{' '}
        <code>HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate = 1</code>.
      </p>

      <div className="m-docs-next">
        <Link to="/docs/api">
          <span className="m-docs-next-label">Next</span>
          <span className="m-docs-next-title">REST API →</span>
        </Link>
        <Link to="/docs/getting-started">
          <span className="m-docs-next-label">Back to</span>
          <span className="m-docs-next-title">Getting started</span>
        </Link>
      </div>
    </article>
  );
}
