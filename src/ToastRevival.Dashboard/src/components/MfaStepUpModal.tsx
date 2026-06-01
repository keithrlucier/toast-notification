import { useEffect, useState } from 'react';
import { authApi } from '../api/auth';
import { ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

/**
 * Reusable step-up verification prompt. Shown when a sensitive action returns
 * 403 { error: "mfa_required" } under tenant-wide MFA enforcement. Tries SMS first
 * (if the account has a phone) and falls back to the authenticator code. On success
 * it elevates the session (setMfaToken → mfa=true) and calls onVerified so the caller
 * can retry the original action. Mirrors the verify half of BroadcastConfirmModal.
 */
export default function MfaStepUpModal({
  action,
  onVerified,
  onCancel,
}: {
  action: string;
  onVerified: () => void;
  onCancel: () => void;
}) {
  const { setMfaToken } = useAuth();
  const [code, setCode]         = useState('');
  const [error, setError]       = useState('');
  const [verifying, setVerifying] = useState(false);
  const [useSms, setUseSms]     = useState<boolean | null>(null); // null = detecting
  const [maskedPhone, setMaskedPhone] = useState<string | null>(null);

  useEffect(() => {
    authApi.mfaSendSms()
      .then(res => { setMaskedPhone(res.masked); setUseSms(true); })
      .catch(() => setUseSms(false)); // no phone → authenticator
  }, []);

  const resend = async () => {
    setError('');
    try {
      const res = await authApi.mfaSendSms();
      setMaskedPhone(res.masked);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to send code.');
    }
  };

  const verify = async () => {
    if (code.length !== 6) return;
    setVerifying(true);
    setError('');
    try {
      const res = useSms
        ? await authApi.mfaVerifySms({ code })
        : await authApi.mfaVerify({ code });
      setMfaToken(res.mfaToken);
      onVerified();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Verification failed.');
    } finally {
      setVerifying(false);
    }
  };

  return (
    <div className="modal-overlay" onMouseDown={e => e.stopPropagation()} onClick={e => e.stopPropagation()}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <h2 style={{ color: 'var(--status-warning)' }}>Verification required</h2>
        <p>{action} requires multi-factor verification. Enter a code to continue.</p>

        {useSms === null ? (
          <div style={{ textAlign: 'center', padding: '16px 0', color: 'var(--text-dim)' }}>
            <span className="spinner" /> Sending code…
          </div>
        ) : (
          <div style={{ marginBottom: 24 }}>
            <div className="field" style={{ marginBottom: 8 }}>
              <label>{useSms ? `Verification code${maskedPhone ? ` sent to ${maskedPhone}` : ''}` : 'Authenticator code'}</label>
              <input
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                maxLength={6}
                placeholder="000000"
                value={code}
                onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                onKeyDown={e => e.key === 'Enter' && verify()}
                autoFocus
                style={{ fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20, textAlign: 'center' }}
              />
            </div>
            {useSms && (
              <button className="btn btn-ghost" style={{ fontSize: 12, marginBottom: 8 }} onClick={resend}>
                Resend code
              </button>
            )}
            {error && <div className="error-banner">{error}</div>}
          </div>
        )}

        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onCancel} disabled={verifying}>Cancel</button>
          <button className="btn btn-primary" onClick={verify} disabled={verifying || code.length !== 6}>
            {verifying ? <span className="spinner" /> : null} Verify
          </button>
        </div>
      </div>
    </div>
  );
}
