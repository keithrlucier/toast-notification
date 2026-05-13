import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';

// ── Types ────────────────────────────────────────────────────────────────────

interface ApiKeyResponse {
  id: string;
  name: string;
  keyPrefix: string;
  createdAt: string;
  lastUsedAt: string | null;
  isRevoked: boolean;
}

interface ApiKeyCreatedResponse {
  id: string;
  name: string;
  keyPrefix: string;
  fullKey: string;
  createdAt: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

// ── Main page ────────────────────────────────────────────────────────────────

export default function ApiKeys() {
  const [keys, setKeys]               = useState<ApiKeyResponse[]>([]);
  const [loading, setLoading]         = useState(true);
  const [error, setError]             = useState('');
  const [newKeyName, setNewKeyName]   = useState('');
  const [generating, setGenerating]   = useState(false);
  const [genError, setGenError]       = useState('');
  const [revokingId, setRevokingId]   = useState<string | null>(null);
  const [revokeError, setRevokeError] = useState('');
  const [createdKey, setCreatedKey]   = useState<ApiKeyCreatedResponse | null>(null);
  const [copyFeedback, setCopyFeedback] = useState(false);
  const fullKeyRef = useRef<HTMLTextAreaElement>(null);

  const loadKeys = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await api.get<ApiKeyResponse[]>('/api/apikeys');
      setKeys(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load API keys.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void loadKeys(); }, []);

  // Auto-select key text when modal opens
  useEffect(() => {
    if (createdKey && fullKeyRef.current) {
      fullKeyRef.current.select();
    }
  }, [createdKey]);

  const handleGenerate = async () => {
    const trimmed = newKeyName.trim();
    if (!trimmed) {
      setGenError('Key name is required.');
      return;
    }
    setGenerating(true);
    setGenError('');
    try {
      const result = await api.post<ApiKeyCreatedResponse>('/api/apikeys', { name: trimmed });
      setCreatedKey(result);
      setNewKeyName('');
      await loadKeys();
    } catch (err) {
      setGenError(err instanceof ApiError ? err.message : 'Failed to generate API key.');
    } finally {
      setGenerating(false);
    }
  };

  const handleRevoke = async (id: string) => {
    setRevokingId(id);
    setRevokeError('');
    try {
      await api.delete(`/api/apikeys/${id}`);
      await loadKeys();
    } catch (err) {
      setRevokeError(err instanceof ApiError ? err.message : 'Failed to revoke API key.');
    } finally {
      setRevokingId(null);
    }
  };

  const handleCopy = async () => {
    if (!createdKey) return;
    try {
      await navigator.clipboard.writeText(createdKey.fullKey);
      setCopyFeedback(true);
      setTimeout(() => setCopyFeedback(false), 2000);
    } catch {
      // Fallback: text is already selected in the textarea
      if (fullKeyRef.current) fullKeyRef.current.select();
    }
  };

  const handleDone = () => {
    setCreatedKey(null);
    setCopyFeedback(false);
  };

  return (
    <div style={{ maxWidth: 900, margin: '0 auto', padding: '32px 24px' }}>
      {/* Page heading */}
      <div style={{ marginBottom: 32 }}>
        <h1 style={{
          fontSize: 22,
          fontWeight: 700,
          color: 'var(--text-primary)',
          marginBottom: 8,
        }}>
          API Keys
        </h1>
        <p style={{ color: 'var(--text-secondary)', fontSize: 14 }}>
          Generate API keys for programmatic access from RMM tools and automation.
        </p>
      </div>

      {/* Keys list card */}
      <div style={{
        background: 'var(--bg-secondary)',
        borderRadius: 8,
        padding: 24,
        marginBottom: 24,
        boxShadow: 'var(--shadow-1)',
      }}>
        {error && (
          <div style={{
            background: 'rgba(248,113,113,0.1)',
            border: '1px solid rgba(248,113,113,0.3)',
            borderRadius: 4,
            padding: '10px 14px',
            color: 'var(--status-error)',
            fontSize: 13,
            marginBottom: 16,
          }}>
            {error}
          </div>
        )}

        {revokeError && (
          <div style={{
            background: 'rgba(248,113,113,0.1)',
            border: '1px solid rgba(248,113,113,0.3)',
            borderRadius: 4,
            padding: '10px 14px',
            color: 'var(--status-error)',
            fontSize: 13,
            marginBottom: 16,
          }}>
            {revokeError}
          </div>
        )}

        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: '32px 0' }}>
            <span className="spinner" />
          </div>
        ) : keys.length === 0 ? (
          <div style={{
            textAlign: 'center',
            padding: '40px 0',
            color: 'var(--text-dim)',
            fontSize: 14,
          }}>
            No API keys. Generate your first key below.
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
              <thead>
                <tr>
                  {['Name', 'Key Prefix', 'Created', 'Last Used', 'Status', 'Actions'].map(h => (
                    <th key={h} style={{
                      textAlign: 'left',
                      padding: '8px 12px',
                      color: 'var(--text-dim)',
                      fontWeight: 600,
                      fontSize: 11,
                      textTransform: 'uppercase',
                      letterSpacing: '0.06em',
                      borderBottom: '1px solid rgba(15,23,42,0.10)',
                    }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {keys.map(k => (
                  <tr
                    key={k.id}
                    style={{ opacity: k.isRevoked ? 0.5 : 1 }}
                  >
                    <td style={{ padding: '12px 12px', color: 'var(--text-primary)', fontWeight: 500 }}>
                      {k.name}
                    </td>
                    <td style={{ padding: '12px 12px' }}>
                      <code style={{
                        fontFamily: 'var(--font-mono)',
                        fontSize: 12,
                        background: 'var(--bg-tertiary)',
                        padding: '2px 6px',
                        borderRadius: 3,
                        color: 'var(--text-secondary)',
                      }}>
                        {k.keyPrefix}…
                      </code>
                    </td>
                    <td style={{ padding: '12px 12px', color: 'var(--text-secondary)' }}>
                      {formatDate(k.createdAt)}
                    </td>
                    <td style={{ padding: '12px 12px', color: 'var(--text-secondary)' }}>
                      {formatDate(k.lastUsedAt)}
                    </td>
                    <td style={{ padding: '12px 12px' }}>
                      {k.isRevoked ? (
                        <span style={{
                          display: 'inline-block',
                          padding: '2px 8px',
                          borderRadius: 4,
                          fontSize: 11,
                          fontWeight: 600,
                          background: 'rgba(122,122,146,0.15)',
                          color: 'var(--text-dim)',
                          textTransform: 'uppercase',
                          letterSpacing: '0.04em',
                        }}>
                          Revoked
                        </span>
                      ) : (
                        <span style={{
                          display: 'inline-block',
                          padding: '2px 8px',
                          borderRadius: 4,
                          fontSize: 11,
                          fontWeight: 600,
                          background: 'rgba(0,201,167,0.12)',
                          color: 'var(--accent)',
                          textTransform: 'uppercase',
                          letterSpacing: '0.04em',
                        }}>
                          Active
                        </span>
                      )}
                    </td>
                    <td style={{ padding: '12px 12px' }}>
                      {!k.isRevoked && (
                        <button
                          onClick={() => { void handleRevoke(k.id); }}
                          disabled={revokingId === k.id}
                          style={{
                            background: 'transparent',
                            border: '1px solid rgba(248,113,113,0.4)',
                            borderRadius: 4,
                            color: 'var(--status-error)',
                            fontSize: 12,
                            fontWeight: 500,
                            padding: '4px 10px',
                            cursor: revokingId === k.id ? 'not-allowed' : 'pointer',
                            opacity: revokingId === k.id ? 0.6 : 1,
                            fontFamily: 'var(--font-sans)',
                          }}
                        >
                          {revokingId === k.id ? 'Revoking...' : 'Revoke'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Generate new key card */}
      <div style={{
        background: 'var(--bg-secondary)',
        borderRadius: 8,
        padding: 24,
        boxShadow: 'var(--shadow-1)',
      }}>
        <h2 style={{
          fontSize: 15,
          fontWeight: 600,
          color: 'var(--text-primary)',
          marginBottom: 16,
        }}>
          Generate New Key
        </h2>

        {genError && (
          <div style={{
            background: 'rgba(248,113,113,0.1)',
            border: '1px solid rgba(248,113,113,0.3)',
            borderRadius: 4,
            padding: '10px 14px',
            color: 'var(--status-error)',
            fontSize: 13,
            marginBottom: 16,
          }}>
            {genError}
          </div>
        )}

        <div style={{ marginBottom: 16 }}>
          <label style={{
            display: 'block',
            fontSize: 13,
            fontWeight: 500,
            color: 'var(--text-secondary)',
            marginBottom: 6,
          }}>
            Key Name
          </label>
          <input
            type="text"
            value={newKeyName}
            onChange={e => setNewKeyName(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') void handleGenerate(); }}
            placeholder="e.g. NinjaOne RMM"
            maxLength={100}
            style={{
              width: '100%',
              maxWidth: 360,
              background: 'var(--bg-tertiary)',
              border: '1px solid rgba(15,23,42,0.14)',
              borderRadius: 4,
              padding: '9px 12px',
              color: 'var(--text-primary)',
              fontSize: 14,
              fontFamily: 'var(--font-sans)',
              outline: 'none',
            }}
          />
        </div>

        <button
          onClick={() => { void handleGenerate(); }}
          disabled={generating}
          style={{
            background: generating ? 'var(--accent-pressed)' : 'var(--accent)',
            border: 'none',
            borderRadius: 4,
            color: '#FFFFFF',
            fontSize: 14,
            fontWeight: 600,
            padding: '9px 20px',
            cursor: generating ? 'not-allowed' : 'pointer',
            opacity: generating ? 0.8 : 1,
            fontFamily: 'var(--font-sans)',
          }}
        >
          {generating ? 'Generating...' : 'Generate Key'}
        </button>
      </div>

      {/* One-time key modal */}
      {createdKey && (
        <div
          onClick={handleDone}
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.7)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
          }}
        >
          <div
            onClick={e => e.stopPropagation()}
            style={{
              background: 'var(--bg-secondary)',
              borderRadius: 8,
              padding: 32,
              width: '100%',
              maxWidth: 520,
              boxShadow: 'var(--shadow-3)',
            }}
          >
            <h2 style={{
              fontSize: 17,
              fontWeight: 700,
              color: 'var(--text-primary)',
              marginBottom: 8,
            }}>
              API Key Generated
            </h2>
            <p style={{
              fontSize: 13,
              color: 'var(--text-secondary)',
              marginBottom: 4,
            }}>
              Key: <strong style={{ color: 'var(--text-primary)' }}>{createdKey.name}</strong>
            </p>

            <div style={{
              background: 'rgba(251,191,36,0.1)',
              border: '1px solid rgba(251,191,36,0.3)',
              borderRadius: 4,
              padding: '10px 14px',
              color: 'var(--status-warning)',
              fontSize: 13,
              marginBottom: 16,
              marginTop: 12,
            }}>
              Copy this key now. It will not be shown again.
            </div>

            <textarea
              ref={fullKeyRef}
              readOnly
              value={createdKey.fullKey}
              rows={3}
              style={{
                width: '100%',
                background: 'var(--bg-tertiary)',
                border: '1px solid rgba(15,23,42,0.14)',
                borderRadius: 4,
                padding: '10px 12px',
                color: 'var(--accent)',
                fontFamily: 'var(--font-mono)',
                fontSize: 13,
                resize: 'none',
                wordBreak: 'break-all',
                marginBottom: 20,
                outline: 'none',
              }}
              onClick={() => fullKeyRef.current?.select()}
            />

            <div style={{ display: 'flex', gap: 12 }}>
              <button
                onClick={() => { void handleCopy(); }}
                style={{
                  background: 'var(--accent)',
                  border: 'none',
                  borderRadius: 4,
                  color: '#FFFFFF',
                  fontSize: 14,
                  fontWeight: 600,
                  padding: '9px 20px',
                  cursor: 'pointer',
                  fontFamily: 'var(--font-sans)',
                  minWidth: 140,
                }}
              >
                {copyFeedback ? 'Copied!' : 'Copy to Clipboard'}
              </button>
              <button
                onClick={handleDone}
                style={{
                  background: 'transparent',
                  border: '1px solid rgba(15,23,42,0.20)',
                  borderRadius: 4,
                  color: 'var(--text-secondary)',
                  fontSize: 14,
                  fontWeight: 500,
                  padding: '9px 20px',
                  cursor: 'pointer',
                  fontFamily: 'var(--font-sans)',
                }}
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
