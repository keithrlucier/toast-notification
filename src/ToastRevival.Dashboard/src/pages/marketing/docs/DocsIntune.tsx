import { Link } from 'react-router-dom';
import { CodeBlock, Callout } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsIntune() {
  useSeo({
    title: 'Intune deployment',
    description:
      'Deploy the Toast Notification agent through Microsoft Intune as a Win32 app or MSIX Line-of-Business app. Covers wrapping the MSI with IntuneWinAppUtil, install commands, detection rules, and tenant ID delivery.',
    path: '/docs/deploy/intune',
    jsonLd: [
      techArticleLd({
        headline: 'Intune deployment',
        description:
          'Deploy the Toast Notification agent through Microsoft Intune as a Win32 app (recommended for MSPs) or MSIX Line-of-Business app.',
        path: '/docs/deploy/intune',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
        { name: 'Intune', path: '/docs/deploy/intune' },
      ]),
    ],
  });

  return (
    <article>
      <h1>Intune</h1>
      <p>
        Two deployment paths are available. <strong>Win32 app</strong> wraps the signed MSI and is the recommended
        path for MSPs and IT admins — it runs under the SYSTEM context, passes tenant properties directly in the
        install command, and has no code-signing enforcement requirements. <strong>MSIX Line-of-Business</strong> is
        the MSIX-native path for organizations already on a Store / MSIX deployment model.
      </p>

      <h2 id="win32">Win32 app (recommended)</h2>
      <p>
        Use this path when deploying to client tenants from an MSP Intune console, or in any environment where
        SYSTEM-context install and inline tenant ID delivery are preferred.
      </p>

      <h3 id="win32-prereqs">Prerequisites</h3>
      <ul>
        <li>Microsoft Intune license with Application Management permissions.</li>
        <li>Target endpoints enrolled in Intune and running Windows 10 build 19041 or Windows 11 (64-bit).</li>
        <li>The latest signed MSI downloaded from the admin dashboard — Devices → Install agent tab.</li>
        <li>
          <a
            href="https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool/releases"
            target="_blank"
            rel="noopener noreferrer"
          >
            IntuneWinAppUtil.exe
          </a>{' '}
          from the Microsoft Win32 Content Prep Tool.
        </li>
        <li>Your tenant GUID and enrollment key from Settings → Tenant on the admin dashboard.</li>
      </ul>

      <h3 id="win32-wrap">Step 1 — Wrap the MSI</h3>
      <p>
        Run IntuneWinAppUtil to package the MSI as a <code>.intunewin</code> file. Place the MSI in a dedicated
        folder (e.g. <code>C:\IntuneSource</code>) and point the output to a separate folder.
      </p>
      <CodeBlock
        language="powershell"
        label="wrap the MSI"
        code={`IntuneWinAppUtil.exe \`
  -c "C:\\IntuneSource" \`
  -s "ToastNotification.Agent-X.Y.Z.0.msi" \`
  -o "C:\\IntuneOutput"`}
      />

      <h3 id="win32-add">Step 2 — Add the app in Intune</h3>
      <ol>
        <li>
          In the Intune portal, navigate to <strong>Apps → Windows → Add</strong> and select{' '}
          <strong>Windows app (Win32)</strong> as the app type.
        </li>
        <li>
          Upload the <code>.intunewin</code> file produced in step 1.
        </li>
        <li>
          On the <strong>App information</strong> tab, set:
          <ul>
            <li><strong>Name:</strong> Toast Notification</li>
            <li><strong>Publisher:</strong> Toast2IT, LLC</li>
            <li><strong>Description:</strong> Managed Windows toast notifications for MSPs.</li>
            <li><strong>Category:</strong> Productivity</li>
          </ul>
        </li>
      </ol>

      <h3 id="win32-program">Step 3 — Configure install and uninstall</h3>
      <p>
        On the <strong>Program</strong> tab, replace the tenant GUID and enrollment key with the values from your
        tenant portal.
      </p>
      <CodeBlock
        language="text"
        label="install command"
        code={`msiexec /i "ToastNotification.Agent-X.Y.Z.0.msi" /qn /norestart CLIENTID=00000000-0000-0000-0000-000000000000 SERVERURL=https://toastnotification.com ENROLLMENTKEY=<your-enrollment-key>`}
      />
      <CodeBlock
        language="powershell"
        label="uninstall command"
        code={`powershell.exe -ExecutionPolicy Bypass -Command "$c=(Get-ItemProperty 'HKLM:\\SOFTWARE\\Toast2IT\\Toast Notification' -EA SilentlyContinue).InstalledProductCode; if($c){Start-Process msiexec -ArgumentList @('/x',$c,'/qn','/norestart') -Wait}"`}
      />
      <ul>
        <li><strong>Install behavior:</strong> System</li>
        <li><strong>Device restart behavior:</strong> No specific action</li>
      </ul>

      <Callout title="CLIENTID, SERVERURL, and ENROLLMENTKEY">
        <p>
          These three properties are written to{' '}
          <code>HKLM\SOFTWARE\Toast2IT\Toast Notification</code> by a Windows Installer custom action during
          install. The agent reads them on first launch, registers the device, and connects to the notification hub.
          The enrollment key limits new device registration — rotate it from Tenant Settings if an install command
          is exposed. Both the tenant GUID and server URL are non-secret; only the enrollment key has a
          registration gate.
        </p>
      </Callout>

      <h3 id="win32-requirements">Step 4 — Requirements</h3>
      <ul>
        <li><strong>OS:</strong> Windows 10 build 19041 or later / Windows 11</li>
        <li><strong>Architecture:</strong> 64-bit</li>
        <li><strong>Disk space:</strong> 80 MB</li>
      </ul>

      <h3 id="win32-detection">Step 5 — Detection rule</h3>
      <p>
        In the <strong>Detection rules</strong> step, choose <strong>Manually configure detection rules</strong>,
        then click <strong>+ Add</strong> and fill in:
      </p>
      <CodeBlock
        language="text"
        label="detection rule fields"
        code={`Rule type:             File
Path:                  C:\\Program Files\\Toast Notification
File or folder name:   ToastNotification.Agent.exe
Detection method:      File or folder exists
Associated with 32-bit app on 64-bit clients:  No`}
      />
      <p>
        Click <strong>OK</strong>, then <strong>Next</strong>. The file{' '}
        <code>ToastNotification.Agent.exe</code> is present if and only if the MSI installed
        successfully — no registry parsing required.
      </p>

      <h3 id="win32-assign">Step 6 — Assign to a group</h3>
      <ol>
        <li>
          On the app's <strong>Assignments</strong> tab, add the target Azure AD group under{' '}
          <strong>Required</strong> for forced install.
        </li>
        <li>
          Save. Intune distributes the package on the next endpoint check-in. Install status appears in the
          per-endpoint device record under <strong>Apps → All apps → Toast Notification → Device install status</strong>.
        </li>
      </ol>

      <h3 id="win32-update">Auto-update</h3>
      <p>
        MSI-installed agents (including Intune Win32 deployments) self-update through the MSI channel: once a day
        the agent polls <code>/api/agent/version</code>, and when a newer release is published it downloads the
        signed MSI, re-verifies its Authenticode signature, and installs it silently via a SYSTEM scheduled task.
        Because the Win32 detection rule keys on the agent file&apos;s presence (not its version), this in-place
        upgrade does not trigger an Intune reinstall — but Intune will keep reporting the originally deployed
        version until you publish the new package. To pin a version and drive every update through Intune instead,
        set <code>HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate = 1</code> via a configuration
        profile (or pass <code>DISABLEAUTOUPDATE=1</code> on the install command), then push new MSI versions as
        app updates.
      </p>
      <Callout title="Keep your Intune package current">
        <p>
          Even with self-update enabled, re-wrap and republish the latest signed MSI when a new version ships.
          Self-update covers devices already enrolled and online; republishing ensures <strong>newly enrolled</strong>{' '}
          devices install the current version on day one and that Intune&apos;s reported version matches what is
          actually running. For a security fix, do both — let self-update carry the existing fleet and republish so
          the management plane is accurate.
        </p>
      </Callout>

      <hr style={{ margin: '40px 0', borderColor: 'rgba(15,23,42,0.1)' }} />

      <h2 id="msix-lob">MSIX Line-of-Business</h2>
      <p>
        Use this path for organizations that manage applications as MSIX packages or want Intune's built-in
        package family detection. The MSIX is signed with our Sectigo OV certificate — publicly trusted by
        Windows, no org certificate enrollment needed.
      </p>

      <h3 id="msix-prereqs">Prerequisites</h3>
      <ul>
        <li>Microsoft Intune license with Application Management permissions.</li>
        <li>Target endpoints enrolled in Intune and running Windows 10 build 19041 or Windows 11.</li>
        <li>The latest signed MSIX downloaded from the admin dashboard's Devices → Install agent tab.</li>
        <li>Your tenant GUID (Settings → Tenant on the admin dashboard).</li>
      </ul>

      <h3 id="msix-upload">Upload the MSIX</h3>
      <ol>
        <li>
          In the Intune portal, navigate to <strong>Apps → Windows → Add</strong> and select{' '}
          <strong>Line-of-business app</strong> as the app type.
        </li>
        <li>
          Select the signed MSIX file <code>ToastNotification.Agent-X.Y.Z.msix</code>. Intune extracts the
          publisher, version, and dependencies automatically from the manifest.
        </li>
        <li>
          On the <strong>App information</strong> tab, set:
          <ul>
            <li><strong>Name:</strong> Toast Notification</li>
            <li><strong>Publisher:</strong> Toast2IT, LLC</li>
            <li><strong>Description:</strong> Managed Windows toast notifications for MSPs.</li>
            <li><strong>Category:</strong> Productivity</li>
          </ul>
        </li>
        <li>Save the app to the Intune apps library.</li>
      </ol>

      <h3 id="msix-tenant-id">Deliver the tenant ID</h3>
      <p>
        The MSIX itself does not embed a tenant GUID — every endpoint shares the same package. Configure the
        tenant ID per-endpoint with one of three options.
      </p>

      <h4>Option A — Intune environment variables policy (recommended)</h4>
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

      <h4>Option B — Per-user bootstrap.json via Win32 wrapper</h4>
      <p>
        For environments where the OMA-URI approach is not available, package a small Win32 app that writes a
        per-user <code>bootstrap.json</code> into the package LocalState directory and assign it as a dependency
        of the MSIX.
      </p>

      <h4>Option C — Provision through self-service</h4>
      <p>
        Push the MSIX without a tenant binding. Users enter their tenant ID in the agent's tray menu on first
        launch. Best for environments where tenant assignment varies per user.
      </p>

      <h3 id="msix-assign">Assign to a group</h3>
      <ol>
        <li>
          On the app's <strong>Properties</strong> page, click <strong>Edit</strong> next to{' '}
          <strong>Assignments</strong>.
        </li>
        <li>
          Add the target Azure AD group under <strong>Required</strong> for forced install or{' '}
          <strong>Available for enrolled devices</strong> for opt-in via Company Portal.
        </li>
        <li>
          Save. Intune begins distributing the MSIX to assigned endpoints. Install confirmation appears in the
          per-endpoint device record under{' '}
          <strong>Apps → All apps → Toast Notification → Device install status</strong>.
        </li>
      </ol>

      <h3 id="msix-detection">Detection rule</h3>
      <p>
        Intune detects the MSIX by package family name automatically — no custom detection rule is required. If
        you need explicit detection (for example to report version skew), use:
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
          The agent MSIX is signed with a publicly trusted Sectigo OV certificate — Windows endpoints trust it
          by default through the Windows root certificate store. No org certificate enrollment or re-signing is
          required for hosted deployments.
        </p>
        <p style={{ marginTop: 8 }}>
          If your org uses custom WDAC or AppLocker policies that allow applications by publisher, add a rule
          for <code>O=Toast2IT, LLC, L=Tallahassee, S=Florida, C=US</code>. This is a policy configuration
          entry — not a re-signing step. If your security policy requires packages signed by an org-controlled
          CA, use the Win32 app path above instead.
        </p>
      </Callout>

      <h3 id="msix-update">Auto-update</h3>
      <p>
        Intune-managed MSIX installs receive updates through Intune's app update mechanism. Push a new app
        version to replace the existing one — endpoints update on the next sync. The Velopack in-process
        auto-updater is no-op for Intune-managed installs.
      </p>

      <h3 id="msix-uninstall">Uninstall</h3>
      <p>
        Reassign the app to <strong>Uninstall</strong> for the target group, or remove the user / device from
        the assignment group. The MSIX uninstalls cleanly on the next Intune sync.
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
