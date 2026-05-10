import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

export default function DeployCommand() {
  const { user } = useAuth();
  const [copied, setCopied] = useState(false);

  const tenantId  = user?.tenantId ?? '<your-tenant-id>';
  const serverUrl = window.location.origin;
  const command   =
    `msiexec /i ToastNotification.msi /qn ^\n  CLIENTID=${tenantId} ^\n  SERVERURL=${serverUrl}`;

  const copy = () => {
    navigator.clipboard.writeText(command.replace(' ^\n ', ' ')).then(() => {
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
          Deploy command
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
            href="https://www.microsoft.com/store/apps/9P5L0MRMFRRF"
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
        fontSize: 13,
        lineHeight: 1.7,
        color: 'var(--text-primary)',
        background: 'var(--bg-tertiary)',
        borderRadius: 4,
        padding: '14px 16px',
        margin: 0,
        overflowX: 'auto',
        whiteSpace: 'pre',
        border: '1px solid rgba(255,255,255,0.06)',
      }}>
        {command}
      </pre>

      <div style={{ marginTop: 12, display: 'flex', gap: 24, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
          <span style={{ color: 'var(--text-secondary)', fontWeight: 600 }}>CLIENTID</span>{' '}
          <code style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--accent)' }}>{tenantId}</code>
        </span>
        <span style={{ fontSize: 12, color: 'var(--text-dim)' }}>
          <span style={{ color: 'var(--text-secondary)', fontWeight: 600 }}>SERVERURL</span>{' '}
          <code style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--accent)' }}>{serverUrl}</code>
        </span>
      </div>

      <p style={{ marginTop: 10, fontSize: 12, color: 'var(--text-dim)' }}>
        Drop this in your RMM, Intune, or run it directly.
        Agents appear in the device list within seconds of first launch.
      </p>
    </div>
  );
}
