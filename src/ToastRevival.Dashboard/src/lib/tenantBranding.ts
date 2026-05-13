export const TENANT_BRANDING_UPDATED_EVENT = 'toast:tenant-branding-updated';

export function tenantLogoUrlForBrowser(logoUrl?: string | null): string {
  const trimmed = logoUrl?.trim();
  if (!trimmed) return '';

  if (trimmed.startsWith('/')) return trimmed;

  try {
    const parsed = new URL(trimmed);
    if (parsed.pathname.startsWith('/assets/logos/')) {
      return `${parsed.pathname}${parsed.search}${parsed.hash}`;
    }
  } catch {
    // Leave non-URL values unchanged so the browser can apply its normal rules.
  }

  return trimmed;
}

export function notifyTenantBrandingUpdated(): void {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new Event(TENANT_BRANDING_UPDATED_EVENT));
}
