import { useEffect, useRef, useState } from 'react';
import { devicesApi, type UninstallScriptInfo } from '../api/devices';

interface Props {
  machineName: string;
  isOnline: boolean;
  /** Best-effort remote removal — decommissions and pushes the uninstall command
   *  to the agent if it is online and on a build that supports it. Returns when
   *  the request completes. */
  onRemoteUninstall: () => Promise<void>;
  onClose: () => void;
}

// Quick single-machine removal. Reads the ProductCode the MSI stored at install
// (name-agnostic) and runs a silent uninstall; the MSI's uninstall actions revert
// the lock screen + strip the Spotlight policy. For a fleet, use the downloadable
// script instead — it also covers Microsoft Store / MSIX installs and purges config.
const QUICK_COMMAND =
  `$pc = (Get-ItemProperty 'HKLM:\\SOFTWARE\\Toast2IT\\Toast Notification' -Name InstalledProductCode -ErrorAction Stop).InstalledProductCode\n` +
  `Start-Process msiexec.exe -ArgumentList "/x $pc /qn /norestart" -Wait`;

function formatDate(iso: string | null): string {
  if (!iso) return 'unknown';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return 'unknown';
  return d.toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZoneName: 'short',
  });
}

export default function RemoveAgentModal({ machineName, isOnline, onRemoteUninstall, onClose }: Props) {
  const [copied, setCopied] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [error, setError] = useState('');
  const [info, setInfo] = useState<UninstallScriptInfo | null>(null);
  // Dismiss on backdrop click only when BOTH mousedown and click land on the
  // backdrop — a drag that starts inside the modal must not close it.
  const downOnBackdrop = useRef(false);

  useEffect(() => {
    let active = true;
    devicesApi.uninstallScriptInfo()
      .then(i => { if (active) setInfo(i); })
      .catch(() => { /* download still works via the static path even if meta fails */ });
    return () => { active = false; };
  }, []);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(QUICK_COMMAND);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setError('Could not copy to clipboard — select the command and copy manually.');
    }
  };

  const handleRemote = async () => {
    setRemoving(true);
    setError('');
    try {
      await onRemoteUninstall();
      onClose();
    } catch {
      setError('Remote removal request failed. Use the script or command below on the device instead.');
    } finally {
      setRemoving(false);
    }
  };

  const scriptUrl = info?.url ?? '/downloads/uninstall-toast-agent.ps1';

  return (
    <div
      className="modal-overlay"
      onMouseDown={e => { downOnBackdrop.current = e.target === e.currentTarget; }}
      onClick={e => { if (e.target === e.currentTarget && downOnBackdrop.current) onClose(); }}
    >
      <div className="modal" onMouseDown={e => e.stopPropagation()} onClick={e => e.stopPropagation()}>
        <h2 style={{ color: 'var(--text-primary)' }}>Remove agent</h2>

        <p style={{ color: 'var(--text-secondary)' }}>
          The Toast Notification agent runs inside each user&rsquo;s Windows session
          without administrator rights, so the dashboard can&rsquo;t force-remove it from{' '}
          <strong>{machineName}</strong>. Removing the software takes an administrator —
          deploy the clean-removal script below through your RMM (or run it on the device).
        </p>

        {/* Primary: downloadable fleet clean-removal script */}
        <div
          style={{
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-sm)',
            padding: '14px 16px',
            marginBottom: 14,
            background: '#F7FAFC',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
            <div>
              <div style={{ fontWeight: 700, color: 'var(--text-primary)', fontSize: 14 }}>
                Clean-removal script (.ps1)
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 2 }}>
                Removes MSI <em>and</em> Microsoft Store installs by name, reverts the lock screen &amp;
                policy, and purges config. Run as SYSTEM/admin or push fleet-wide via RMM.
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 6, fontFamily: 'var(--font-mono)' }}>
                Last modified: {info ? formatDate(info.lastModifiedUtc) : '…'}
              </div>
            </div>
            <a
              className="btn btn-primary"
              href={scriptUrl}
              download="uninstall-toast-agent.ps1"
              style={{ fontSize: 13, whiteSpace: 'nowrap', textDecoration: 'none' }}
            >
              Download script
            </a>
          </div>
        </div>

        {/* Secondary: quick one-box single-machine command */}
        <div style={{ marginBottom: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
            <label style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)' }}>
              Or, quick single-machine command (PowerShell, as admin)
            </label>
            <button
              className="btn btn-ghost"
              style={{ fontSize: 12, padding: '4px 10px' }}
              onClick={() => void handleCopy()}
            >
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
          <pre
            style={{
              margin: 0,
              padding: '12px 14px',
              background: '#0F172A',
              color: '#E2E8F0',
              borderRadius: 'var(--radius-sm)',
              fontFamily: 'var(--font-mono)',
              fontSize: 12,
              lineHeight: 1.6,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              overflowX: 'auto',
            }}
          >
            {QUICK_COMMAND}
          </pre>
        </div>

        {error && <div className="error-banner" style={{ marginTop: 12 }}>{error}</div>}

        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onClose} disabled={removing}>Close</button>
          <button
            className="btn btn-secondary"
            onClick={() => void handleRemote()}
            disabled={!isOnline || removing}
            title={isOnline
              ? 'Decommission this device and push the removal to the agent (best-effort; needs agent v0.4.32+ online)'
              : 'Device is offline — use the script or command on the device'}
          >
            {removing ? <span className="spinner" /> : null}
            Attempt remote removal
          </button>
        </div>
      </div>
    </div>
  );
}
