// Single source of truth for docs route paths.
// DocsLayout.tsx (nav sidebar) and App.tsx (router) both import from here — edit
// one and the other gets the change automatically.

export const DOCS_PATHS = {
  index:          '/docs',
  gettingStarted: '/docs/getting-started',
  deployStore:    '/docs/deploy/store',
  deployIntune:   '/docs/deploy/intune',
  deployRmm:      '/docs/deploy/rmm',
  api:            '/docs/api',
  moderation:     '/docs/moderation',
} as const;
