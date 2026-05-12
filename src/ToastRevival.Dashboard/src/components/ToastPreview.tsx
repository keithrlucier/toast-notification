import { useEffect, useState } from 'react';
import type { ActionButton } from '../api/notifications';
import { api } from '../api/client';

interface ToastPreviewProps {
  title: string;
  bodyLine1?: string;
  bodyLine2?: string;
  heroImageUrl?: string;
  logoUrl?: string;
  actionButtons?: ActionButton[];
  scenario?: string;
  scale?: number;
  /** Override the app-attribution shown in the preview header. Defaults to the
   *  signed-in tenant's display name (matches what the agent writes as the
   *  AUMID DisplayName at runtime). Falls back to 'Toast Notification' when
   *  unauthenticated or before the fetch resolves. */
  appName?: string;
}

interface TenantSettingsLite {
  tenantName: string;
  logoUrl: string | null;
}

interface TenantPreviewIdentity {
  name: string | null;
  logoUrl: string | null;
}

let _tenantIdentityCache: Promise<TenantPreviewIdentity> | null = null;

/** Fetches the signed-in tenant's display name + logo for preview fidelity.
 *  Cached at module level so the request fires once per dashboard load even
 *  when ToastPreview is rendered many times (e.g. Templates page grid). */
function getTenantIdentity(): Promise<TenantPreviewIdentity> {
  if (!_tenantIdentityCache) {
    _tenantIdentityCache = api.get<TenantSettingsLite>('/api/tenant/settings')
      .then(res => ({
        name: res.tenantName?.trim() || null,
        logoUrl: res.logoUrl?.trim() || null,
      }))
      .catch(() => ({ name: null, logoUrl: null }));
  }
  return _tenantIdentityCache;
}

/* Character truncation limits matching Windows Action Center rendering */
const TITLE_MAX   = 48;
const BODY_MAX    = 90;

function truncate(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 1) + '…' : s;
}

export default function ToastPreview({
  title,
  bodyLine1,
  bodyLine2,
  heroImageUrl,
  logoUrl,
  actionButtons = [],
  scenario,
  scale = 1,
  appName,
}: ToastPreviewProps) {
  const isUrgent = scenario === 'urgent';

  const displayTitle = title ? truncate(title, TITLE_MAX) : 'Notification Title';
  const displayBody1 = bodyLine1 ? truncate(bodyLine1, BODY_MAX) : undefined;
  const displayBody2 = bodyLine2 ? truncate(bodyLine2, BODY_MAX) : undefined;

  // When the caller hasn't supplied an explicit appName / logoUrl, fetch the
  // tenant's display name + logo so the preview matches what the delivered
  // toast will show: tenantName mirrors the AUMID DisplayName the agent writes
  // to HKCU; logoUrl mirrors the per-notification LogoUrl the API defaults to
  // tenant.LogoUrl when the sender doesn't override it
  // (NotificationsController.Send). Without both fetches the preview would
  // diverge from the runtime delivery.
  const [tenantIdentity, setTenantIdentity] = useState<TenantPreviewIdentity | null>(null);
  useEffect(() => {
    if (appName && logoUrl) return;
    let cancelled = false;
    void getTenantIdentity().then(identity => { if (!cancelled) setTenantIdentity(identity); });
    return () => { cancelled = true; };
  }, [appName, logoUrl]);

  const headerAppName = (appName?.trim() || tenantIdentity?.name || 'Toast Notification');
  const headerLogoUrl = logoUrl?.trim() || tenantIdentity?.logoUrl || undefined;

  return (
    /* Windows 11 desktop background */
    <div style={{
      background: 'var(--preview-bg)',
      borderRadius: 12,
      padding: 24,
      display: 'flex',
      justifyContent: 'flex-end',
      alignItems: 'flex-start',
      minHeight: 160,
      boxShadow: 'var(--shadow-3)',
    }}>
      {/* Toast card — positioned bottom-right like Windows Action Center */}
      <div
        role="status"
        aria-label="Toast notification preview"
        style={{
          width: 364,
          background: 'var(--preview-card)',
          borderRadius: 8,
          overflow: 'hidden',
          boxShadow: '0 4px 24px rgba(0,0,0,0.5)',
          fontFamily: "'Segoe UI', system-ui, sans-serif",
          fontSize: scale !== 1 ? `${13 * scale}px` : '13px',
          transform: scale !== 1 ? `scale(${scale})` : undefined,
          transformOrigin: scale !== 1 ? 'top right' : undefined,
          border: isUrgent ? '1px solid rgba(251,191,36,0.4)' : '1px solid rgba(255,255,255,0.06)',
        }}
      >
        {/* App header row */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          padding: '10px 12px 6px',
          borderBottom: heroImageUrl ? 'none' : '1px solid rgba(255,255,255,0.04)',
        }}>
          {/* App logo / icon */}
          <div style={{
            width: 16,
            height: 16,
            borderRadius: 3,
            background: '#F59E0B',
            flexShrink: 0,
            overflow: 'hidden',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}>
            {headerLogoUrl ? (
              <img src={headerLogoUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
            ) : (
              <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
                <rect x="0.5" y="2" width="9" height="6" rx="1" fill="#0F1117" />
                <rect x="1.5" y="3" width="5" height="1" rx="0.5" fill="white" fillOpacity="0.9" />
                <rect x="1.5" y="5" width="4" height="0.75" rx="0.375" fill="white" fillOpacity="0.5" />
              </svg>
            )}
          </div>

          {/* App name — matches the AUMID DisplayName written by the agent on the
              installed device, so the preview reflects the actual attribution users
              will see in Windows Action Center. */}
          <span style={{
            flex: 1,
            fontSize: 11,
            color: 'rgba(255,255,255,0.5)',
            fontWeight: 400,
            letterSpacing: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}>
            {headerAppName}
          </span>

          {/* Urgency badge */}
          {isUrgent && (
            <span style={{
              fontSize: 10,
              color: '#FBBF24',
              fontWeight: 600,
              background: 'rgba(251,191,36,0.15)',
              borderRadius: 3,
              padding: '1px 5px',
            }}>
              URGENT
            </span>
          )}

          {/* Close button */}
          <div style={{
            width: 16,
            height: 16,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: 'rgba(255,255,255,0.3)',
            cursor: 'default',
            flexShrink: 0,
          }}>
            <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
              <path d="M1.5 1.5l7 7M8.5 1.5l-7 7" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
            </svg>
          </div>
        </div>

        {/* Hero image */}
        {heroImageUrl && (
          <div style={{ width: '100%', aspectRatio: '364/180', overflow: 'hidden', background: 'rgba(255,255,255,0.05)' }}>
            <img
              src={heroImageUrl}
              alt=""
              style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
              onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
            />
          </div>
        )}

        {/* Content */}
        <div style={{ padding: '10px 12px' }}>
          <div style={{
            color: 'var(--preview-text)',
            fontWeight: 600,
            fontSize: 13,
            lineHeight: 1.3,
            marginBottom: displayBody1 || displayBody2 ? 4 : 0,
            wordBreak: 'break-word',
          }}>
            {displayTitle}
          </div>

          {displayBody1 && (
            <div style={{
              color: 'var(--preview-sub)',
              fontSize: 12,
              lineHeight: 1.4,
              marginBottom: displayBody2 ? 2 : 0,
              wordBreak: 'break-word',
            }}>
              {displayBody1}
            </div>
          )}

          {displayBody2 && (
            <div style={{
              color: 'var(--preview-sub)',
              fontSize: 12,
              lineHeight: 1.4,
              wordBreak: 'break-word',
            }}>
              {displayBody2}
            </div>
          )}
        </div>

        {/* Action buttons */}
        {actionButtons.length > 0 && (
          <div style={{
            display: 'flex',
            gap: 1,
            borderTop: '1px solid rgba(255,255,255,0.06)',
          }}>
            {actionButtons.slice(0, 3).map((btn, i) => (
              <div
                key={i}
                style={{
                  flex: 1,
                  padding: '9px 12px',
                  textAlign: 'center',
                  fontSize: 12,
                  fontWeight: 500,
                  cursor: 'default',
                  color: btn.style === 'Critical'
                    ? '#F87171'
                    : btn.style === 'Success'
                    ? '#4ADE80'
                    : 'rgba(255,255,255,0.85)',
                  background: 'rgba(255,255,255,0.04)',
                  borderRight: i < actionButtons.slice(0, 3).length - 1 ? '1px solid rgba(255,255,255,0.06)' : 'none',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  userSelect: 'none',
                }}
              >
                {btn.label}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

/* Character count helper for the composer */
export function CharCount({ current, max }: { current: number; max: number }) {
  const remaining = max - current;
  const cls = remaining < 0 ? 'char-counter error' : remaining < 10 ? 'char-counter warning' : 'char-counter';
  return <span className={cls}>{current}/{max}</span>;
}
