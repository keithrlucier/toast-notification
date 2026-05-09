import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { authApi } from '../api/auth';
import { ApiError } from '../api/client';

interface Props {
  deviceCount: number;
  requiresMfa: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export default function BroadcastConfirmModal({ deviceCount, requiresMfa, onConfirm, onCancel }: Props) {
  const { user, setMfaToken } = useAuth();
  const [mfaCode, setMfaCode] = useState('');
  const [error, setError] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [mfaVerified, setMfaVerified] = useState(user?.mfaElevated ?? false);

  const handleMfaVerify = async () => {
    if (!mfaCode.trim()) return;
    setVerifying(true);
    setError('');
    try {
      const res = await authApi.mfaVerify({ code: mfaCode.trim() });
      setMfaToken(res.token, res.role);
      setMfaVerified(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'MFA verification failed.');
    } finally {
      setVerifying(false);
    }
  };

  const handleConfirm = () => {
    if (requiresMfa && !mfaVerified) {
      setError('MFA verification required before broadcasting to all devices.');
      return;
    }
    onConfirm();
  };

  return (
    <div
      className="modal-overlay"
      onMouseDown={e => e.stopPropagation()}
      onClick={e => e.stopPropagation()}
    >
      <div className="modal" onClick={e => e.stopPropagation()}>
        <h2 style={{ color: requiresMfa ? 'var(--status-warning)' : 'var(--text-primary)' }}>
          {requiresMfa ? 'Broadcast Confirmation Required' : 'Confirm Notification Send'}
        </h2>
        <p>
          This will send a notification to <strong>{deviceCount.toLocaleString()} device{deviceCount !== 1 ? 's' : ''}</strong>.
          {requiresMfa && ' Broadcasting to all devices requires MFA verification.'}
        </p>

        {requiresMfa && !mfaVerified && (
          <div style={{ marginBottom: 24 }}>
            <div className="field" style={{ marginBottom: 12 }}>
              <label>Authenticator Code</label>
              <input
                type="text"
                inputMode="numeric"
                pattern="[0-9]{6}"
                maxLength={6}
                placeholder="000000"
                value={mfaCode}
                onChange={e => setMfaCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                onKeyDown={e => e.key === 'Enter' && handleMfaVerify()}
                autoFocus
                style={{ fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20, textAlign: 'center' }}
              />
            </div>
            {error && <div className="error-banner">{error}</div>}
            <button
              className="btn btn-secondary"
              style={{ width: '100%' }}
              onClick={handleMfaVerify}
              disabled={verifying || mfaCode.length !== 6}
            >
              {verifying ? <span className="spinner" /> : null}
              Verify Code
            </button>
          </div>
        )}

        {requiresMfa && mfaVerified && (
          <div className="success-banner">MFA verified. You may proceed.</div>
        )}

        {!requiresMfa && error && <div className="error-banner">{error}</div>}

        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onCancel}>Cancel</button>
          <button
            className="btn btn-primary"
            onClick={handleConfirm}
            disabled={requiresMfa && !mfaVerified}
          >
            Send Now
          </button>
        </div>
      </div>
    </div>
  );
}
