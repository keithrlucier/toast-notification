import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { ApiError } from '../api/client';
import {
  enrollmentTokensApi,
  type EnrollmentToken,
  type EnrollmentTokenStatus,
  type IssuedEnrollmentToken,
} from '../api/enrollmentTokens';

// Hex literals (not CSS vars) so the alpha-append tint trick below produces a
// valid 8-digit hex — appending to a var(...) reference yields invalid CSS that
// silently renders transparent. Values mirror the :root --status-* tokens.
const SUCCESS_HEX = '#4ADE80';
const STATUS_COLORS: Record<EnrollmentTokenStatus, string> = {
  active: SUCCESS_HEX,
  used: '#6B7280',
  expired: '#FBBF24',
  revoked: '#F87171',
};

function StatusPill({ status }: { status: EnrollmentTokenStatus }) {
  const color = STATUS_COLORS[status];
  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 5,
      fontSize: 11,
      fontWeight: 600,
      color,
      background: `${color}1A`,
      borderRadius: 4,
      padding: '2px 8px',
      textTransform: 'uppercase',
      letterSpacing: '0.04em',
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: color, flexShrink: 0 }} />
      {status}
    </span>
  );
}

function fmt(s: string | null): string {
  if (!s) return '—';
  const d = new Date(s);
  return Number.isNaN(d.getTime())
    ? '—'
    : d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

export default function EnrollmentTokens() {
  const { user } = useAuth();
  const [tokens, setTokens] = useState<EnrollmentToken[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [label, setLabel] = useState('');
  const [ttlHours, setTtlHours] = useState(24);
  const [issuing, setIssuing] = useState(false);
  const [issued, setIssued] = useState<IssuedEnrollmentToken | null>(null);
  const [copied, setCopied] = useState<'token' | 'command' | null>(null);

  // Two-step inline confirm (Diana's destructive-action pattern): first click arms,
  // onBlur clears, second click revokes.
  const [armedRevoke, setArmedRevoke] = useState<string | null>(null);
  const [revoking, setRevoking] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    try {
      setTokens(await enrollmentTokensApi.list());
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load enrollment tokens.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const tenantId = user?.tenantId ?? '<your-tenant-id>';
  const serverUrl = window.location.origin;
  const msiUrl = `${serverUrl}/downloads/ToastNotification.msi`;

  const installCommand = (token: string): string => {
    const args =
      `/i \`"$f\`" /qn CLIENTID=${tenantId} SERVERURL=${serverUrl} ENROLLMENTKEY=${token}`;
    return (
      `$f="$env:TEMP\\ToastNotification.msi"; ` +
      `Invoke-WebRequest "${msiUrl}" -OutFile $f; ` +
      `Start-Process msiexec -ArgumentList "${args}" -Verb RunAs -Wait`
    );
  };

  const handleIssue = async () => {
    setIssuing(true);
    try {
      const res = await enrollmentTokensApi.issue({
        label: label.trim() || null,
        ttlHours: Number.isFinite(ttlHours) ? ttlHours : null,
      });
      setIssued(res);
      setLabel('');
      setError(null);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to issue enrollment token.');
    } finally {
      setIssuing(false);
    }
  };

  const copy = (text: string, which: 'token' | 'command') => {
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(which);
      setTimeout(() => setCopied(c => (c === which ? null : c)), 2000);
    });
  };

  const handleRevokeClick = async (id: string) => {
    if (armedRevoke !== id) { setArmedRevoke(id); return; }
    setArmedRevoke(null);
    setRevoking(id);
    try {
      await enrollmentTokensApi.revoke(id);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to revoke enrollment token.');
    } finally {
      setRevoking(null);
    }
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <h2 style={{ fontSize: 16, fontWeight: 700, marginBottom: 4 }}>Enrollment tokens</h2>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: 16 }}>
        Single-use tokens for high-trust device enrollment. Issue one per device and paste it as the{' '}
        <code style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>ENROLLMENTKEY</code> in the install
        command. A token can be redeemed once, expires automatically, and a spent token left on a device
        cannot enroll another machine.
      </p>

      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap', marginBottom: 20 }}>
        <div className="field" style={{ flex: '1 1 220px', minWidth: 200 }}>
          <label htmlFor="et-label">Label (optional)</label>
          <input
            id="et-label"
            type="text"
            value={label}
            maxLength={120}
            placeholder="e.g. Reception PC"
            onChange={e => setLabel(e.target.value)}
          />
        </div>
        <div className="field" style={{ width: 150 }}>
          <label htmlFor="et-ttl">Expires in (hours)</label>
          <input
            id="et-ttl"
            type="number"
            min={1}
            max={168}
            value={ttlHours}
            onChange={e => setTtlHours(Math.max(1, Math.min(168, Number(e.target.value) || 24)))}
          />
        </div>
        <button className="btn btn-primary" onClick={() => void handleIssue()} disabled={issuing}>
          {issuing ? 'Issuing…' : 'Issue token'}
        </button>
      </div>

      {issued && (
        <div style={{
          border: `1px solid ${SUCCESS_HEX}`,
          background: `${SUCCESS_HEX}14`,
          borderRadius: 6,
          padding: 16,
          marginBottom: 20,
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, gap: 12 }}>
            <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-primary)' }}>
              Token issued{issued.label ? ` — ${issued.label}` : ''}. Copy it now — it will not be shown again.
            </span>
            <button
              className="btn btn-ghost"
              style={{ fontSize: 12, padding: '4px 10px', minHeight: 0 }}
              onClick={() => setIssued(null)}
            >
              Dismiss
            </button>
          </div>
          <div style={{
            fontFamily: 'var(--font-mono)',
            fontSize: 13,
            color: 'var(--text-primary)',
            background: 'var(--bg-tertiary)',
            borderRadius: 4,
            padding: '10px 12px',
            wordBreak: 'break-all',
            marginBottom: 10,
          }}>
            {issued.token}
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button
              className="btn btn-secondary"
              style={{ fontSize: 12, padding: '6px 14px', minHeight: 0 }}
              onClick={() => copy(issued.token, 'token')}
            >
              {copied === 'token' ? '✓ Copied' : 'Copy token'}
            </button>
            <button
              className="btn btn-secondary"
              style={{ fontSize: 12, padding: '6px 14px', minHeight: 0 }}
              onClick={() => copy(installCommand(issued.token), 'command')}
            >
              {copied === 'command' ? '✓ Copied' : 'Copy install command'}
            </button>
            <span style={{ fontSize: 12, color: 'var(--text-dim)', alignSelf: 'center' }}>
              Expires {fmt(issued.expiresAt)}
            </span>
          </div>
        </div>
      )}

      {error && (
        <div style={{ color: 'var(--status-error)', fontSize: 13, marginBottom: 12 }}>{error}</div>
      )}

      {loading ? (
        <p style={{ fontSize: 13, color: 'var(--text-dim)' }}>Loading…</p>
      ) : tokens.length === 0 ? (
        <p style={{ fontSize: 13, color: 'var(--text-dim)' }}>
          No enrollment tokens yet. Issue one above for single-use device enrollment.
        </p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Label</th>
              <th>Status</th>
              <th>Redeemed by</th>
              <th>Issued</th>
              <th>Expires</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {tokens.map(t => (
              <tr key={t.id}>
                <td>{t.label ?? <span style={{ color: 'var(--text-dim)' }}>—</span>}</td>
                <td><StatusPill status={t.status} /></td>
                <td style={{ fontSize: 12, color: 'var(--text-dim)' }}>
                  {t.usedByDeviceName
                    ? `${t.usedByDeviceName}${t.usedByUsername ? ` (${t.usedByUsername})` : ''}`
                    : '—'}
                </td>
                <td style={{ color: 'var(--text-dim)' }}>{fmt(t.createdAt)}</td>
                <td style={{ color: 'var(--text-dim)' }}>{fmt(t.expiresAt)}</td>
                <td style={{ textAlign: 'right' }}>
                  {t.status === 'active' ? (
                    <button
                      className="btn btn-ghost"
                      style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-error)' }}
                      onClick={() => void handleRevokeClick(t.id)}
                      onBlur={() => setArmedRevoke(a => (a === t.id ? null : a))}
                      disabled={revoking === t.id}
                    >
                      {revoking === t.id ? 'Revoking…' : armedRevoke === t.id ? 'Confirm?' : 'Revoke'}
                    </button>
                  ) : (
                    <span style={{ color: 'var(--text-dim)', fontSize: 12 }}>—</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
