import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../api/client';
import { getEnrollmentKey } from '../api/tenantSettings';

interface IntuneWinInfo {
  url: string;
  version: string | null;
  available: boolean;
  lastModifiedUtc: string | null;
  sizeBytes: number;
}

function formatSize(bytes: number): string {
  if (!bytes) return '';
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function formatDate(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/**
 * A labelled, copy-able command block — the per-field equivalent of the one-liner
 * box in DeployCommand. Intune's "Add app" wizard has separate fields for the
 * install command, uninstall command, and detection rule, so each gets its own
 * copy button rather than one combined blob.
 */
function CopyBlock({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  const copy = () => {
    navigator.clipboard.writeText(value).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };
  return (
    <div style={{ marginTop: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
        <span style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--text-secondary)' }}>
          {label}
        </span>
        <button
          onClick={copy}
          className="btn btn-secondary"
          style={{ fontSize: 11, padding: '4px 12px', minHeight: 0 }}
        >
          {copied ? '✓ Copied' : 'Copy'}
        </button>
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
        whiteSpace: 'pre-wrap',
        border: '1px solid rgba(255,255,255,0.06)',
        wordBreak: 'break-all',
      }}>
        {value}
      </pre>
    </div>
  );
}

export default function IntuneDeployment() {
  const { user } = useAuth();
  const [enrollmentKey, setEnrollmentKey] = useState<string | null>(null);
  const [info, setInfo] = useState<IntuneWinInfo | null>(null);

  useEffect(() => {
    let cancelled = false;
    getEnrollmentKey().then(key => { if (!cancelled) setEnrollmentKey(key); });
    api.get<IntuneWinInfo>('/api/agent/intunewin-info')
      .then(res => { if (!cancelled) setInfo(res); })
      .catch(() => { if (!cancelled) setInfo(null); });
    return () => { cancelled = true; };
  }, []);

  const tenantId  = user?.tenantId ?? '<your-tenant-id>';
  const serverUrl = window.location.origin;
  const downloadUrl = info?.url ?? '/downloads/ToastNotification.intunewin';
  const available = info?.available ?? false;

  // Intune fills the .intunewin's bundled MSI by name, so the install command
  // references ToastNotification.msi — not a download URL (the package already
  // carries the MSI). Tenant values are written to HKLM by a WiX custom action
  // on install; the agent reads them on first launch.
  const enrollmentPart = enrollmentKey ? ` ENROLLMENTKEY=${enrollmentKey}` : ' ENROLLMENTKEY=<your-enrollment-key>';
  const installCommand =
    `msiexec /i "ToastNotification.msi" /qn /norestart ` +
    `CLIENTID=${tenantId} SERVERURL=${serverUrl}${enrollmentPart}`;

  // Uninstall reads the live ProductCode from the registry, so it survives agent
  // self-updates (a hardcoded GUID goes stale the moment the agent updates itself).
  const uninstallCommand =
    `powershell.exe -ExecutionPolicy Bypass -Command "$c=(Get-ItemProperty ` +
    `'HKLM:\\SOFTWARE\\Toast2IT\\Toast Notification' -EA SilentlyContinue).InstalledProductCode; ` +
    `if($c){Start-Process msiexec -ArgumentList @('/x',$c,'/qn','/norestart') -Wait}"`;

  const detectionRule =
    `Rule type:             File\n` +
    `Path:                  C:\\Program Files\\Toast Notification\n` +
    `File or folder name:   ToastNotification.Agent.exe\n` +
    `Detection method:      File or folder exists\n` +
    `Associated with 32-bit app on 64-bit clients:  No`;

  const metaBits = [
    info?.version ? `v${info.version}` : null,
    formatSize(info?.sizeBytes ?? 0),
    info?.lastModifiedUtc ? `updated ${formatDate(info.lastModifiedUtc)}` : null,
  ].filter(Boolean).join('  ·  ');

  return (
    <div style={{
      background: 'var(--bg-secondary)',
      border: '1px solid rgba(0,201,167,0.25)',
      borderRadius: 8,
      padding: 24,
      marginBottom: 24,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4, gap: 12, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--accent)' }}>
          Intune — Win32 app
        </span>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          {available ? (
            <a
              href={downloadUrl}
              download
              className="btn btn-primary"
              style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, textDecoration: 'none' }}
            >
              ↓ Download .intunewin
            </a>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
              Package not yet published — use the wrap step in the guide.
            </span>
          )}
          <a
            href="/docs/deploy/intune"
            className="btn btn-secondary"
            style={{ fontSize: 12, padding: '6px 14px', minHeight: 0, textDecoration: 'none' }}
          >
            Full guide ↗
          </a>
        </div>
      </div>

      {metaBits && (
        <div style={{ fontSize: 11, color: 'var(--text-dim)', fontFamily: 'var(--font-mono)', marginBottom: 4 }}>
          {metaBits}
        </div>
      )}

      <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginTop: 8, marginBottom: 4 }}>
        Pre-wrapped Win32 package for Microsoft Intune. In the Intune portal go to{' '}
        <strong>Apps → Windows → Add → Windows app (Win32)</strong>, upload the file above, then paste the
        fields below. They are pre-filled with <strong>this tenant&apos;s</strong> values — no manual wrapping or
        GUID lookup required. Set <strong>Install behavior: System</strong>.
      </p>

      <CopyBlock label="Install command" value={installCommand} />
      <CopyBlock label="Uninstall command" value={uninstallCommand} />
      <CopyBlock label="Detection rule" value={detectionRule} />

      <div style={{ marginTop: 16, display: 'flex', gap: 24, flexWrap: 'wrap' }}>
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

      <p style={{ marginTop: 12, fontSize: 12, color: 'var(--text-dim)' }}>
        The package is identical for every tenant — what differs is the install command above. Rotate the
        enrollment key from Settings → Tenant if a command is ever exposed; existing devices keep working.
      </p>
    </div>
  );
}
