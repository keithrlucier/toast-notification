import { useState, useEffect } from 'react';
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
  const [maskedPhone, setMaskedPhone] = useState<string | null>(null);
  const [smsSent, setSmsSent] = useState(false);
  const [useSms, setUseSms] = useState<boolean | null>(null); // null = detecting

  // On mount, try SMS first. If account has no phone, fall back to TOTP.
  useEffect(() => {
    if (!requiresMfa || mfaVerified) return;
    authApi.mfaSendSms()
      .then((res: { masked: string }) => {
        setMaskedPhone(res.masked);
        setSmsSent(true);
        setUseSms(true);
      })
      .catch(() => {
        // No phone number — fall back to TOTP
        setUseSms(false);
      });
  }, [requiresMfa, mfaVerified]);

  const handleResend = async () => {
    setError('');
    try {
      const res = await authApi.mfaSendSms();
      setMaskedPhone(res.masked);
      setSmsSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to send code.');
    }
  };

  const handleVerify = async () => {
    if (!mfaCode.trim()) return;
    setVerifying(true);
    setError('');
    try {
      const res = useSms
        ? await authApi.mfaVerifySms({ code: mfaCode.trim() })
        : await authApi.mfaVerify({ code: mfaCode.trim() });
      setMfaToken(res.mfaToken);
      setMfaVerified(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed.');
    } finally {
      setVerifying(false);
    }
  };

  const handleConfirm = () => {
    if (requiresMfa && !mfaVerified) {
      setError('MFA verification required before broadcasting.');
      return;
    }
    onConfirm();
  };

  const detecting = requiresMfa && !mfaVerified && useSms === null;

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
          {requiresMfa && ' Broadcasting to all devices requires verification.'}
        </p>

        {requiresMfa && !mfaVerified && !detecting && (
          <div style={{ marginBottom: 24 }}>
            <div className="field" style={{ marginBottom: 8 }}>
              <label>
                {useSms
                  ? `Verification Code${maskedPhone ? ` sent to ${maskedPhone}` : ''}`
                  : 'Authenticator Code'}
              </label>
              <input
                type="text"
                inputMode="numeric"
                pattern="[0-9]{6}"
                maxLength={6}
                placeholder="000000"
                value={mfaCode}
                onChange={e => setMfaCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                onKeyDown={e => e.key === 'Enter' && handleVerify()}
                autoFocus={smsSent}
                style={{ fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20, textAlign: 'center' }}
              />
            </div>
            {useSms && (
              <button
                className="btn btn-ghost"
                style={{ fontSize: 12, marginBottom: 8 }}
                onClick={handleResend}
              >
                Resend code
              </button>
            )}
            {error && <div className="error-banner">{error}</div>}
            <button
              className="btn btn-secondary"
              style={{ width: '100%' }}
              onClick={handleVerify}
              disabled={verifying || mfaCode.length !== 6}
            >
              {verifying ? <span className="spinner" /> : null}
              Verify Code
            </button>
          </div>
        )}

        {requiresMfa && !mfaVerified && detecting && (
          <div style={{ textAlign: 'center', padding: '16px 0', color: 'var(--text-dim)' }}>
            <span className="spinner" /> Sending code…
          </div>
        )}

        {requiresMfa && mfaVerified && (
          <div className="success-banner">Verified. You may proceed.</div>
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
