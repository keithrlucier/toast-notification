import { describe, it, expect } from 'vitest';
import { tenantLogoUrlForBrowser } from './tenantBranding';

describe('tenantLogoUrlForBrowser', () => {
  it('returns empty string for null / undefined / whitespace', () => {
    expect(tenantLogoUrlForBrowser(null)).toBe('');
    expect(tenantLogoUrlForBrowser(undefined)).toBe('');
    expect(tenantLogoUrlForBrowser('   ')).toBe('');
  });

  it('passes relative paths through unchanged', () => {
    expect(tenantLogoUrlForBrowser('/assets/logos/abc.png')).toBe('/assets/logos/abc.png');
  });

  it('rewrites an absolute asset URL to a same-origin relative path (avoids mixed-content)', () => {
    expect(tenantLogoUrlForBrowser('https://cdn.example.com/assets/logos/abc.png'))
      .toBe('/assets/logos/abc.png');
  });

  it('preserves query string and hash when rewriting an asset URL', () => {
    expect(tenantLogoUrlForBrowser('https://cdn.example.com/assets/logos/abc.png?v=2#x'))
      .toBe('/assets/logos/abc.png?v=2#x');
  });

  it('leaves non-asset absolute URLs unchanged', () => {
    expect(tenantLogoUrlForBrowser('https://example.com/other/logo.png'))
      .toBe('https://example.com/other/logo.png');
  });

  it('leaves an unparseable value unchanged (browser applies normal rules)', () => {
    expect(tenantLogoUrlForBrowser('not a url')).toBe('not a url');
  });
});
