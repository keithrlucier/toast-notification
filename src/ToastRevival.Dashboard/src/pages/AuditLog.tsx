import { useEffect, useState } from 'react';
import { auditApi, type AuditLogEntry } from '../api/audit';
import { ApiError } from '../api/client';

const DAYS_OPTIONS = [7, 30, 90] as const;
type Days = (typeof DAYS_OPTIONS)[number];
const PAGE_SIZE = 50;

function formatTs(iso: string): string {
  return new Date(iso).toLocaleString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: 'numeric', minute: '2-digit', second: '2-digit',
  });
}

export default function AuditLog() {
  const [logs, setLogs]         = useState<AuditLogEntry[]>([]);
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState('');
  const [days, setDays]         = useState<Days>(30);
  const [page, setPage]         = useState(1);
  const [exporting, setExporting] = useState(false);
  const [exportErr, setExportErr] = useState('');

  const load = async (d: Days, p: number) => {
    setLoading(true);
    setError('');
    try {
      const data = await auditApi.list(d, p, PAGE_SIZE);
      setLogs(data);
      setPage(p);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load audit log.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(days, 1); }, [days]);

  const handleExport = async (format: 'csv' | 'pdf') => {
    setExporting(true);
    setExportErr('');
    try {
      await auditApi.exportFile(format, days);
    } catch (err) {
      setExportErr(err instanceof ApiError ? err.message : 'Export failed.');
    } finally {
      setExporting(false);
    }
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Audit Log</h1>
          <p className="subtitle">All tenant activity — device registrations, notification sends, configuration changes</p>
        </div>

        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {/* Time range selector */}
          <div style={{ display: 'flex', background: 'var(--bg-secondary)', border: '1px solid rgba(255,255,255,0.08)', borderRadius: 4, overflow: 'hidden' }}>
            {DAYS_OPTIONS.map(d => (
              <button
                key={d}
                onClick={() => setDays(d)}
                style={{
                  padding: '7px 14px',
                  fontSize: 13,
                  fontWeight: 500,
                  border: 'none',
                  cursor: 'pointer',
                  background: days === d ? 'var(--accent)' : 'transparent',
                  color: days === d ? 'var(--bg-primary)' : 'var(--text-secondary)',
                  transition: 'background 0.15s, color 0.15s',
                }}
              >
                {d}d
              </button>
            ))}
          </div>

          {/* Export dropdown */}
          <ExportMenu onExport={handleExport} busy={exporting} />
        </div>
      </div>

      {error     && <div className="error-banner">{error}</div>}
      {exportErr && <div className="error-banner">{exportErr}</div>}

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
            <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
          </div>
        ) : logs.length === 0 ? (
          <div className="empty-state">
            <p>No audit entries in the last {days} days.</p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Timestamp</th>
                <th>Action</th>
                <th>Resource Type</th>
                <th>Resource ID</th>
                <th>User</th>
                <th>IP Address</th>
              </tr>
            </thead>
            <tbody>
              {logs.map(l => (
                <tr key={l.id}>
                  <td style={{ color: 'var(--text-dim)', whiteSpace: 'nowrap', fontFamily: 'var(--font-mono)', fontSize: 12 }}>
                    {formatTs(l.timestamp)}
                  </td>
                  <td style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{l.action}</td>
                  <td style={{ color: 'var(--text-secondary)' }}>{l.resourceType}</td>
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--text-dim)' }}>
                    {l.resourceId ? l.resourceId.slice(0, 16) + (l.resourceId.length > 16 ? '…' : '') : '—'}
                  </td>
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--text-dim)' }}>
                    {l.userId ? l.userId.slice(0, 8) + '…' : '—'}
                  </td>
                  <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--text-dim)' }}>
                    {l.ipAddress ?? '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && logs.length > 0 && (
        <div style={{ display: 'flex', gap: 8, justifyContent: 'center', marginTop: 16 }}>
          <button
            className="btn btn-ghost"
            onClick={() => void load(days, page - 1)}
            disabled={page === 1 || loading}
          >
            Previous
          </button>
          <span style={{ lineHeight: '38px', color: 'var(--text-dim)', fontSize: 13 }}>Page {page}</span>
          <button
            className="btn btn-ghost"
            onClick={() => void load(days, page + 1)}
            disabled={logs.length < PAGE_SIZE || loading}
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}

// ── Export menu ───────────────────────────────────────────────────────────────

function ExportMenu({ onExport, busy }: { onExport: (fmt: 'csv' | 'pdf') => void; busy: boolean }) {
  const [open, setOpen] = useState(false);

  return (
    <div style={{ position: 'relative' }}>
      <button
        className="btn btn-ghost"
        onClick={() => setOpen(o => !o)}
        disabled={busy}
        style={{ display: 'flex', alignItems: 'center', gap: 6 }}
      >
        {busy ? (
          <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} />
        ) : (
          <DownloadIcon />
        )}
        Export
        <ChevronIcon open={open} />
      </button>

      {open && (
        <>
          {/* Backdrop */}
          <div
            style={{ position: 'fixed', inset: 0, zIndex: 10 }}
            onClick={() => setOpen(false)}
          />
          <div style={{
            position: 'absolute', right: 0, top: '100%', marginTop: 4,
            background: 'var(--bg-tertiary)',
            border: '1px solid rgba(255,255,255,0.10)',
            borderRadius: 6,
            boxShadow: '0 4px 16px rgba(0,0,0,0.4)',
            zIndex: 20,
            minWidth: 140,
            overflow: 'hidden',
          }}>
            {(['csv', 'pdf'] as const).map(fmt => (
              <button
                key={fmt}
                onClick={() => { setOpen(false); onExport(fmt); }}
                style={{
                  display: 'block', width: '100%', textAlign: 'left',
                  padding: '10px 14px', fontSize: 13, fontWeight: 500,
                  background: 'transparent', border: 'none', cursor: 'pointer',
                  color: 'var(--text-secondary)',
                }}
                onMouseEnter={e => (e.currentTarget.style.background = 'var(--bg-secondary)')}
                onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
              >
                {fmt === 'csv' ? 'Download CSV' : 'Download PDF'}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

function DownloadIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
      <path d="M7 1v8M4 6l3 3 3-3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M2 11h10" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg
      width="12" height="12" viewBox="0 0 12 12" fill="none"
      style={{ transform: open ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s' }}
    >
      <path d="M2 4l4 4 4-4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
