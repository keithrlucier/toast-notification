import { useRef, useState } from 'react';

interface Props {
  machineName: string;
  isOnline: boolean;
  /** Best-effort remote removal — decommissions and pushes the uninstall command
   *  to the agent if it is online and on a build that supports it. Returns when
   *  the request completes. */
  onRemoteUninstall: () => Promise<void>;
  onClose: () => void;
}

// Self-contained, copy-paste removal command. Reads the ProductCode the MSI
// stored at install time and runs a silent uninstall. The MSI's uninstall
// custom actions revert the branded lock screen and strip the Spotlight policy,
// so this single command is a full clean removal. Must run as administrator.
const REMOVAL_COMMAND =
  `$pc = (Get-ItemProperty 'HKLM:\\SOFTWARE\\Toast2IT\\Toast Notification' -Name InstalledProductCode -ErrorAction Stop).InstalledProductCode\n` +
  `Start-Process msiexec.exe -ArgumentList "/x $pc /qn /norestart" -Wait`;

export default function RemoveAgentModal({ machineName, isOnline, onRemoteUninstall, onClose }: Props) {
  const [copied, setCopied] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [error, setError] = useState('');
  // Dismiss on backdrop click only when BOTH mousedown and click land on the
  // backdrop — a drag that starts inside the modal must not close it.
  const downOnBackdrop = useRef(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(REMOVAL_COMMAND);
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
      setError('Remote removal request failed. Use the command above on the device instead.');
    } finally {
      setRemoving(false);
    }
  };

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
          without administrator rights, so the dashboard cannot force-remove it from{' '}
          <strong>{machineName}</strong>. Removing the software takes an administrator —
          run the command below on the device (or push it through your RMM).
        </p>

        <div style={{ marginBottom: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
            <label style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)' }}>
              Run as administrator (PowerShell)
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
            {REMOVAL_COMMAND}
          </pre>
        </div>

        <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 8 }}>
          This reverts the branded lock screen to the device&rsquo;s original image and
          clears the lock screen policy as part of the uninstall. For fleet removal, the
          same steps are scripted in <span style={{ fontFamily: 'var(--font-mono)' }}>uninstall-toast-agent.ps1</span>.
        </p>

        {error && <div className="error-banner" style={{ marginTop: 12 }}>{error}</div>}

        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onClose} disabled={removing}>Close</button>
          <button
            className="btn btn-secondary"
            onClick={() => void handleRemote()}
            disabled={!isOnline || removing}
            title={isOnline
              ? 'Decommission this device and push the removal to the agent (best-effort; needs agent v0.4.32+ online)'
              : 'Device is offline — use the command above on the device'}
          >
            {removing ? <span className="spinner" /> : null}
            Attempt remote removal
          </button>
        </div>
      </div>
    </div>
  );
}
