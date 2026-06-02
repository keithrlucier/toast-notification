import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../api/client';

interface TenantSettingsLite {
  enrollmentKey: string | null;
}

function triggerDownload(filename: string, content: string) {
  const blob = new Blob([content], { type: 'application/octet-stream' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function ScriptRow({
  title,
  when,
  action,
}: {
  title: string;
  when: string;
  action: React.ReactNode;
}) {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: 16,
      padding: '14px 0',
      borderBottom: '1px solid var(--border-subtle)',
    }}>
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)' }}>{title}</div>
        <div style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginTop: 2 }}>{when}</div>
      </div>
      <div style={{ flexShrink: 0 }}>{action}</div>
    </div>
  );
}

export default function DeploymentScripts() {
  const { user } = useAuth();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const serverUrl = window.location.origin;
  const tenantId = user?.tenantId ?? '';

  const downloadInstall = async () => {
    setBusy(true);
    setError(null);
    try {
      const tmplRes = await fetch('/downloads/install-toast-agent.template.ps1');
      if (!tmplRes.ok) throw new Error(`Template fetch failed (HTTP ${tmplRes.status})`);
      const tmpl = await tmplRes.text();
      let enrollmentKey = '';
      try {
        const settings = await api.get<TenantSettingsLite>('/api/tenant/settings');
        enrollmentKey = settings.enrollmentKey ?? '';
      } catch {
        enrollmentKey = '';
      }
      const filled = tmpl
        .split('__TENANTID__').join(tenantId)
        .split('__SERVERURL__').join(serverUrl)
        .split('__ENROLLMENTKEY__').join(enrollmentKey);
      triggerDownload('install-toast-agent.ps1', filled);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not generate the install script.');
    } finally {
      setBusy(false);
    }
  };

  const linkBtn: React.CSSProperties = {
    fontSize: 12,
    padding: '6px 14px',
    minHeight: 0,
    textDecoration: 'none',
    whiteSpace: 'nowrap',
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <h2 style={{ fontSize: 16, fontWeight: 700, marginBottom: 4 }}>Deployment scripts</h2>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: 8 }}>
        PowerShell scripts for use with an RMM (any tool that runs scripts as SYSTEM). Run <strong>one</strong> per
        device, picked by what you are doing. The uninstall script removes the agent and resets the lock screen in
        one pass, so you never run it together with the reset script.
      </p>

      <ScriptRow
        title="Install agent"
        when="Deploy Toast to a device. Pre-filled with this tenant's ID, server, and enrollment key."
        action={
          <button className="btn btn-primary" style={{ ...linkBtn, cursor: 'pointer' }} onClick={() => void downloadInstall()} disabled={busy}>
            {busy ? 'Preparing…' : 'Download install.ps1'}
          </button>
        }
      />

      <ScriptRow
        title="Uninstall agent"
        when="Remove Toast from a device. Removes the agent (MSI and Store/MSIX) and hard-deletes all lock-screen branding, returning the lock screen to the Windows default."
        action={
          <a className="btn btn-secondary" style={linkBtn} href="/downloads/uninstall-toast-agent.ps1" download>
            Download uninstall.ps1
          </a>
        }
      />

      <ScriptRow
        title="Reset lock screen"
        when="Fix a device where a branded lock screen is stuck or two images are still selectable (Toast staying installed, or already removed). Does not touch the agent."
        action={
          <a className="btn btn-secondary" style={linkBtn} href="/downloads/Reset-ToastLockScreen.ps1" download>
            Download reset-lockscreen.ps1
          </a>
        }
      />

      {error && (
        <div style={{ color: 'var(--status-error)', fontSize: 13, marginTop: 12 }}>{error}</div>
      )}

      <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 12 }}>
        Run as SYSTEM (via an RMM) or an elevated admin shell. The uninstall and reset scripts may return
        exit code 3010 — that means a reboot will finalize the lock-screen change; everything else is already done.
      </p>
    </div>
  );
}
