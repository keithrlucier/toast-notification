import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { getEnrollmentKey } from '../api/tenantSettings';

export default function DeployCommand() {
  const { user } = useAuth();
  const [copied, setCopied] = useState(false);
  const [enrollmentKey, setEnrollmentKey] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getEnrollmentKey().then(key => { if (!cancelled) setEnrollmentKey(key); });
    return () => { cancelled = true; };
  }, []);

  const tenantId   = user?.tenantId ?? '<your-tenant-id>';
  const serverUrl  = window.location.origin;
  const msiUrl     = `${serverUrl}/downloads/ToastNotification.msi`;
  const enrollmentPart = enrollmentKey ? ` ENROLLMENTKEY=${enrollmentKey}` : '';

  // RMM-safe one-liner (runs correctly under SYSTEM via Tactical RMM, Intune, etc.).
  // C:\Temp instead of $env:TEMP — $env:TEMP resolves to C:\Windows\Temp under SYSTEM,
  // which EDR products block for IStorage access (MSI error 2203/1619).
  // MSI staged to C:\Windows\Installer — the trusted package cache path that all
  // EDR/AV products allow msiexec to open IStorage from.
  // TLS 1.2 enforced (ServicePointManager value 3072); BITS fallback for endpoints
  // where SSL inspection or WinINET proxy restrictions block WebClient under SYSTEM.
  // Authenticode signer gate ($g) is enforced before install — mirrors the signed
  // install template (install-toast-agent.template.ps1) so the convenience one-liner
  // is held to the same "Valid + Toast2IT, LLC signer" bar as the hardened path. (RMM-L1)
  // $f/$s/$g are PowerShell variables set at runtime — not JS template expressions.
  const oneLiner =
    `[Net.ServicePointManager]::SecurityProtocol=3072; ` +
    `if(!(Test-Path 'C:\\Temp')){$null=New-Item 'C:\\Temp' -ItemType Directory -Force}; ` +
    `$f='C:\\Temp\\ToastNotification.msi'; ` +
    `try{(New-Object Net.WebClient).DownloadFile('${msiUrl}',$f)}catch{Start-BitsTransfer -Source '${msiUrl}' -Destination $f}; ` +
    `$g=Get-AuthenticodeSignature $f; ` +
    `if($g.Status -ne 'Valid' -or $g.SignerCertificate.Subject -notlike '*Toast2IT, LLC*'){Remove-Item $f -Force -EA 0; throw 'Toast MSI signature invalid'}; ` +
    `$s='C:\\Windows\\Installer\\toast_rmm.msi'; Copy-Item $f $s -Force -EA 0; ` +
    `Start-Process msiexec -ArgumentList "/i \`"$s\`" /qn CLIENTID=${tenantId} SERVERURL=${serverUrl}${enrollmentPart}" -Wait; ` +
    `Remove-Item $f,$s -Force -EA 0`;

  const copy = () => {
    navigator.clipboard.writeText(oneLiner).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <div style={{
      background: 'var(--bg-secondary)',
      border: '1px solid rgba(0,201,167,0.25)',
      borderRadius: 8,
      padding: 24,
      marginBottom: 24,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
        <span style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--accent)' }}>
          Deploy agents
        </span>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            onClick={copy}
            className="btn btn-secondary"
            style={{ fontSize: 12, padding: '6px 14px', minHeight: 0 }}
          >
            {copied ? '✓ Copied' : 'Copy'}
          </button>
          <a
            href={msiUrl}
            download
            className="btn btn-primary"
            style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, textDecoration: 'none' }}
          >
            ↓ Download MSI
          </a>
          <a
            href="https://apps.microsoft.com/detail/9PFD6004DVTN?hl=en-us&gl=US&ocid=pdpshare"
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-secondary"
            style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, textDecoration: 'none' }}
          >
            Microsoft Store ↗
          </a>
          <a
            href="/docs/getting-started"
            className="btn btn-secondary"
            style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, textDecoration: 'none' }}
          >
            Docs ↗
          </a>
        </div>
      </div>

      <pre style={{
        fontFamily: 'var(--font-mono)',
        fontSize: 12,
        lineHeight: 1.7,
        color: 'var(--text-primary)',
        background: 'var(--bg-tertiary)',
        borderRadius: 4,
        padding: '14px 16px',
        margin: 0,
        overflowX: 'auto',
        whiteSpace: 'pre',
        border: '1px solid rgba(255,255,255,0.06)',
        wordBreak: 'break-all',
      }}>
        {oneLiner}
      </pre>

      <div style={{ marginTop: 12, display: 'flex', gap: 24, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
          <span style={{ color: 'var(--text-secondary)', fontWeight: 600 }}>CLIENTID</span>{' '}
          <code style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--accent)' }}>{tenantId}</code>
        </span>
        {enrollmentKey && (
          <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
            <span style={{ color: 'var(--text-secondary)', fontWeight: 600 }}>ENROLLMENTKEY</span>{' '}
            <code style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--accent)' }}>{enrollmentKey}</code>
          </span>
        )}
        <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
          <span style={{ color: 'var(--text-secondary)', fontWeight: 600 }}>SERVERURL</span>{' '}
          <code style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--accent)' }}>{serverUrl}</code>
        </span>
      </div>

      <p style={{ marginTop: 10, fontSize: 12, color: 'var(--text-dim)' }}>
        Paste into PowerShell on any endpoint — downloads and installs silently.
        Drop the same command in your RMM or Intune for mass deployment.
        Agents appear in the device list within seconds of first launch.
      </p>
    </div>
  );
}
