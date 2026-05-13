import { useCallback, useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

type TrialStatus = 'Pending' | 'Approved' | 'Rejected';

interface TrialRequest {
  id: string;
  companyName: string;
  website: string;
  fullName: string;
  email: string;
  phone: string;
  jobTitle: string;
  intendedUseCase: string;
  intendedUseCaseDetails: string | null;
  status: TrialStatus;
  submittedAt: string;
  reviewedAt: string | null;
  reviewNote: string | null;
  createdTenantId: string | null;
  remoteIpAddress: string | null;
  userAgent: string | null;
  turnstileHostname: string | null;
}

const STATUS_OPTIONS: TrialStatus[] = ['Pending', 'Approved', 'Rejected'];

function formatDate(iso: string | null): string {
  if (!iso) return '-';
  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}

function useCaseLabel(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\bMsp\b/, 'MSP');
}

export default function TrialRequests() {
  const { user } = useAuth();
  const [status, setStatus] = useState<TrialStatus>('Pending');
  const [requests, setRequests] = useState<TrialRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [actingId, setActingId] = useState<string | null>(null);
  const [error, setError] = useState('');

  const isPlatformAdmin = user?.isPlatformAdmin ?? false;

  const load = useCallback(async () => {
    if (!isPlatformAdmin) return;
    setLoading(true);
    setError('');
    try {
      const res = await api.get<{ requests: TrialRequest[] }>(`/api/system/trial-requests?status=${status}`);
      setRequests(res.requests);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load trial requests.');
    } finally {
      setLoading(false);
    }
  }, [isPlatformAdmin, status]);

  useEffect(() => { void load(); }, [load]);

  const approve = async (request: TrialRequest) => {
    const note = window.prompt(`Approve trial for ${request.companyName}? Optional note:`);
    if (note === null) return;
    setActingId(request.id);
    setError('');
    try {
      await api.post(`/api/system/trial-requests/${request.id}/approve`, { note: note || null });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Approve failed.');
    } finally {
      setActingId(null);
    }
  };

  const reject = async (request: TrialRequest) => {
    const note = window.prompt(`Reject trial for ${request.companyName}? Add a short internal note:`);
    if (note === null) return;
    setActingId(request.id);
    setError('');
    try {
      await api.post(`/api/system/trial-requests/${request.id}/reject`, { note: note || null });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Reject failed.');
    } finally {
      setActingId(null);
    }
  };

  if (!isPlatformAdmin) {
    return (
      <div className="card">
        <h1 style={{ fontSize: 20, marginBottom: 8 }}>Platform access required</h1>
        <p style={{ color: 'var(--text-secondary)' }}>Trial review is available to platform administrators only.</p>
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Trial Requests</h1>
          <p className="subtitle">Review submitted company details before creating tenant access</p>
        </div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      <div style={{ display: 'flex', gap: 1, background: 'var(--bg-secondary)', borderRadius: 'var(--radius-sm)', border: '1px solid rgba(15,23,42,0.12)', overflow: 'hidden', width: 'fit-content', marginBottom: 20 }}>
        {STATUS_OPTIONS.map(s => (
          <button
            key={s}
            type="button"
            onClick={() => setStatus(s)}
            style={{
              background: status === s ? 'var(--bg-tertiary)' : 'transparent',
              border: 'none',
              color: status === s ? 'var(--text-primary)' : 'var(--text-dim)',
              padding: '9px 16px',
              cursor: 'pointer',
              fontSize: 13,
              fontWeight: status === s ? 700 : 500,
            }}
          >
            {s}
          </button>
        ))}
      </div>

      {error && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}

      {loading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
          <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
        </div>
      ) : requests.length === 0 ? (
        <div className="card" style={{ color: 'var(--text-secondary)' }}>
          No {status.toLowerCase()} trial requests.
        </div>
      ) : (
        <div style={{ display: 'grid', gap: 16 }}>
          {requests.map(request => (
            <div key={request.id} className="card">
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start', marginBottom: 16 }}>
                <div>
                  <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 4 }}>{request.companyName}</h2>
                  <a href={request.website} target="_blank" rel="noreferrer" style={{ color: 'var(--accent)', fontSize: 13 }}>
                    {request.website}
                  </a>
                </div>
                <div style={{ textAlign: 'right', color: 'var(--text-dim)', fontSize: 12 }}>
                  Submitted {formatDate(request.submittedAt)}
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 14, marginBottom: 16 }}>
                <Detail label="Contact" value={`${request.fullName} - ${request.jobTitle}`} />
                <Detail label="Email" value={request.email} />
                <Detail label="Phone" value={request.phone} />
                <Detail label="Use case" value={useCaseLabel(request.intendedUseCase)} />
                <Detail label="IP" value={request.remoteIpAddress ?? '-'} />
                <Detail label="Turnstile host" value={request.turnstileHostname ?? '-'} />
              </div>

              {request.intendedUseCaseDetails && (
                <p style={{ color: 'var(--text-secondary)', fontSize: 13, lineHeight: 1.5, marginBottom: 16 }}>
                  {request.intendedUseCaseDetails}
                </p>
              )}

              {request.reviewNote && (
                <p style={{ color: 'var(--text-dim)', fontSize: 12, marginBottom: 16 }}>
                  Review note: {request.reviewNote}
                </p>
              )}

              {status === 'Pending' ? (
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className="btn btn-primary" onClick={() => void approve(request)} disabled={actingId === request.id}>
                    {actingId === request.id ? 'Working...' : 'Approve'}
                  </button>
                  <button className="btn btn-secondary" onClick={() => void reject(request)} disabled={actingId === request.id}>
                    Reject
                  </button>
                </div>
              ) : (
                <div style={{ color: 'var(--text-dim)', fontSize: 12 }}>
                  Reviewed {formatDate(request.reviewedAt)}
                  {request.createdTenantId && <> - Tenant {request.createdTenantId}</>}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 4 }}>
        {label}
      </div>
      <div style={{ fontSize: 13, color: 'var(--text-primary)', wordBreak: 'break-word' }}>{value}</div>
    </div>
  );
}
