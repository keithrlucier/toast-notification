import { Link } from 'react-router-dom';
import DeployCommand from '../components/DeployCommand';
import { useAuth } from '../contexts/AuthContext';

export default function InstallAgent() {
  const { user } = useAuth();
  const serverUrl = window.location.origin;
  const msiUrl = `${serverUrl}/downloads/ToastNotification.msi`;

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Install Agent</h1>
          <p className="subtitle">MSI download and tenant-specific enrollment values</p>
        </div>
        <a href={msiUrl} download className="btn btn-primary" style={{ textDecoration: 'none' }}>
          Download MSI
        </a>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 16, marginBottom: 24 }}>
        <div className="metric-card">
          <div className="metric-label">Tenant ID</div>
          <div className="metric-value" style={{ fontSize: 14, fontFamily: 'var(--font-mono)', wordBreak: 'break-all' }}>
            {user?.tenantId ?? '-'}
          </div>
          <div className="metric-sub">CLIENTID</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Server URL</div>
          <div className="metric-value" style={{ fontSize: 14, fontFamily: 'var(--font-mono)', wordBreak: 'break-all' }}>
            {serverUrl}
          </div>
          <div className="metric-sub">SERVERURL</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Installer URL</div>
          <div className="metric-value" style={{ fontSize: 14, fontFamily: 'var(--font-mono)', wordBreak: 'break-all' }}>
            {msiUrl}
          </div>
          <div className="metric-sub">direct download</div>
        </div>
      </div>

      <DeployCommand />

      <div className="card">
        <h2 style={{ fontSize: 16, fontWeight: 700, marginBottom: 12 }}>Deployment paths</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 16 }}>
          <DeploymentPath
            title="Single endpoint"
            body="Download the signed MSI and run the PowerShell command above from an elevated session."
            href="/docs/getting-started"
          />
          <DeploymentPath
            title="RMM rollout"
            body="Use the same MSI command in NinjaOne, Datto RMM, ConnectWise Automate, Atera, or any RMM that can run msiexec."
            href="/docs/deploy/rmm"
          />
          <DeploymentPath
            title="Store or Intune"
            body="Use the Microsoft Store or Intune deployment guides when you want package-managed installs."
            href="/docs/deploy/intune"
          />
        </div>
      </div>
    </div>
  );
}

function DeploymentPath({ title, body, href }: { title: string; body: string; href: string }) {
  return (
    <div style={{ border: '1px solid rgba(15,23,42,0.12)', borderRadius: 6, padding: 16 }}>
      <h3 style={{ fontSize: 14, fontWeight: 700, marginBottom: 8 }}>{title}</h3>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: 12 }}>{body}</p>
      <Link to={href} style={{ color: 'var(--accent)', fontSize: 13, fontWeight: 600, textDecoration: 'none' }}>
        Open guide
      </Link>
    </div>
  );
}
