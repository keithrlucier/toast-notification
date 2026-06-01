import { useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { authApi, type MfaStatusResponse } from '../api/auth';
import { ApiError } from '../api/client';
import { useAuth } from '../contexts/AuthContext';

/**
 * Per-user authenticator (TOTP) self-enrollment — the native MFA control that needs
 * zero platform involvement. Available to every signed-in user (Technicians too, so
 * they can satisfy tenant-wide enforcement). Wired to /api/auth/mfa/{status,enroll,
 * enroll/confirm,disable}. Distinct from the Microsoft SSO "require MFA" toggle.
 *
 * onStatusChange lets the parent (e.g. the force-enrollment gate) react when the
 * user flips their own MFA on/off.
 */
export default function TwoFactorCard({ onStatusChange }: { onStatusChange?: (enabled: boolean) => void }) {
  const { setMfaToken } = useAuth();

  const [status, setStatus]   = useState<MfaStatusResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState('');
  const [success, setSuccess] = useState('');

  // Enrollment-in-progress state
  const [enrolling, setEnrolling] = useState(false);
  const [secret, setSecret]       = useState('');
  const [qrDataUrl, setQrDataUrl] = useState('');
  const [code, setCode]           = useState('');
  const [busy, setBusy]           = useState(false);

  // Disable-flow state
  const [disarming, setDisarming]   = useState(false);
  const [disableCode, setDisableCode] = useState('');

  useEffect(() => {
    authApi.mfaStatus()
      .then(setStatus)
      .catch(() => { /* leave null — card stays hidden */ })
      .finally(() => setLoading(false));
  }, []);

  const refreshStatus = (enabled: boolean) => {
    setStatus(s => (s ? { ...s, enabled } : s));
    onStatusChange?.(enabled);
  };

  const startEnroll = async () => {
    setError(''); setSuccess(''); setBusy(true);
    try {
      const res = await authApi.mfaEnroll();
      setSecret(res.secret);
      setQrDataUrl(await QRCode.toDataURL(res.qrUri, { width: 200, margin: 1 }));
      setEnrolling(true);
      setCode('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not start setup. Try again.');
    } finally {
      setBusy(false);
    }
  };

  const confirmEnroll = async () => {
    if (code.length !== 6) return;
    setError(''); setBusy(true);
    try {
      const res = await authApi.mfaEnrollConfirm({ code });
      setMfaToken(res.mfaToken);          // also elevates the session (mfa=true)
      setEnrolling(false);
      setSecret(''); setQrDataUrl(''); setCode('');
      setSuccess('Authenticator enabled. You’ll be asked for a code at your next sign-in.');
      setTimeout(() => setSuccess(''), 5000);
      refreshStatus(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That code didn’t match. Check your authenticator and try again.');
    } finally {
      setBusy(false);
    }
  };

  const cancelEnroll = () => {
    setEnrolling(false);
    setSecret(''); setQrDataUrl(''); setCode(''); setError('');
  };

  const disable = async () => {
    if (disableCode.length !== 6) return;
    setError(''); setBusy(true);
    try {
      await authApi.mfaDisable({ code: disableCode });
      setDisarming(false); setDisableCode('');
      setSuccess('Authenticator turned off.');
      setTimeout(() => setSuccess(''), 4000);
      refreshStatus(false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not turn off MFA.');
    } finally {
      setBusy(false);
    }
  };

  if (loading || !status) return null;

  const codeInputStyle: React.CSSProperties = {
    fontFamily: 'var(--font-mono)', letterSpacing: '0.15em', fontSize: 20, textAlign: 'center',
  };

  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600 }}>Two-Factor Authentication</h2>
        <span style={{
          fontSize: 12, fontWeight: 600, padding: '2px 10px', borderRadius: 999,
          color: status.enabled ? 'var(--status-success)' : 'var(--text-dim)',
          background: status.enabled ? 'rgba(74,222,128,0.12)' : 'var(--bg-tertiary)',
          border: `1px solid ${status.enabled ? 'rgba(74,222,128,0.3)' : 'rgba(148,148,160,0.25)'}`,
        }}>
          {status.enabled ? 'On' : 'Off'}
        </span>
      </div>
      <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16, lineHeight: 1.5 }}>
        Protect your account with an authenticator app (Google Authenticator, Microsoft Authenticator,
        1Password, Authy). After setup you’ll enter a 6-digit code when you sign in.
        {status.tenantRequired && (
          <span style={{ display: 'block', marginTop: 6, color: 'var(--status-warning)' }}>
            Your workspace requires MFA — this can’t be turned off while that policy is on.
          </span>
        )}
      </p>

      {error   && <div className="error-banner" style={{ marginBottom: 16 }}>{error}</div>}
      {success && (
        <div style={{
          background: 'rgba(74,222,128,0.1)', border: '1px solid rgba(74,222,128,0.3)',
          borderRadius: 'var(--radius-sm)', padding: '10px 14px',
          color: 'var(--status-success)', fontSize: 14, marginBottom: 16,
        }}>{success}</div>
      )}

      {/* ── Not enabled, not mid-enroll: offer setup ─────────────────────── */}
      {!status.enabled && !enrolling && (
        <button className="btn btn-primary" onClick={startEnroll} disabled={busy}>
          {busy ? <span className="spinner" /> : null} Set up authenticator
        </button>
      )}

      {/* ── Enrollment in progress: QR + manual key + confirm ────────────── */}
      {!status.enabled && enrolling && (
        <div>
          <ol style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.7, paddingLeft: 18, marginBottom: 16 }}>
            <li>Open your authenticator app and add a new account.</li>
            <li>Scan this QR code (or enter the key manually).</li>
            <li>Enter the 6-digit code it shows to finish.</li>
          </ol>

          <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap', alignItems: 'flex-start', marginBottom: 16 }}>
            {qrDataUrl && (
              <img
                src={qrDataUrl}
                alt="Authenticator QR code"
                width={200}
                height={200}
                style={{ borderRadius: 'var(--radius-sm)', background: '#fff', padding: 8 }}
              />
            )}
            <div style={{ flex: 1, minWidth: 220 }}>
              <div className="field" style={{ marginBottom: 16 }}>
                <label>Can’t scan? Enter this key</label>
                <input
                  type="text"
                  readOnly
                  value={secret}
                  onFocus={e => e.currentTarget.select()}
                  style={{ fontFamily: 'var(--font-mono)', fontSize: 13 }}
                />
              </div>
              <div className="field" style={{ marginBottom: 12 }}>
                <label>6-digit code</label>
                <input
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  placeholder="000000"
                  value={code}
                  onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  onKeyDown={e => e.key === 'Enter' && confirmEnroll()}
                  autoFocus
                  style={codeInputStyle}
                />
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', gap: 8 }}>
            <button className="btn btn-primary" onClick={confirmEnroll} disabled={busy || code.length !== 6}>
              {busy ? <span className="spinner" /> : null} Verify & turn on
            </button>
            <button className="btn btn-ghost" onClick={cancelEnroll} disabled={busy}>Cancel</button>
          </div>
        </div>
      )}

      {/* ── Enabled: show disable (guarded) ──────────────────────────────── */}
      {status.enabled && !disarming && (
        <button
          className="btn btn-secondary"
          onClick={() => { setDisarming(true); setDisableCode(''); setError(''); }}
          disabled={status.tenantRequired}
          title={status.tenantRequired ? 'Your workspace requires MFA.' : undefined}
          style={status.tenantRequired ? { opacity: 0.5, cursor: 'not-allowed' } : undefined}
        >
          Turn off authenticator
        </button>
      )}

      {status.enabled && disarming && (
        <div style={{ maxWidth: 320 }}>
          <div className="field" style={{ marginBottom: 12 }}>
            <label>Enter a current code to turn it off</label>
            <input
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              placeholder="000000"
              value={disableCode}
              onChange={e => setDisableCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
              onKeyDown={e => e.key === 'Enter' && disable()}
              autoFocus
              style={codeInputStyle}
            />
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button className="btn btn-secondary" onClick={disable} disabled={busy || disableCode.length !== 6}>
              {busy ? <span className="spinner" /> : null} Confirm turn off
            </button>
            <button className="btn btn-ghost" onClick={() => { setDisarming(false); setDisableCode(''); }} disabled={busy}>
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
