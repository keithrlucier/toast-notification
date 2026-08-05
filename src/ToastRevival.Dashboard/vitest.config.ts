import { defineConfig } from 'vitest/config';

// Dashboard test harness (CR-P1-005). Tests run under jsdom so frontend modules
// that touch browser globals (localStorage, window, atob) import cleanly even
// when the unit under test is a pure function. Playwright E2E is intentionally
// deferred until there is a runner for it (no CI at present).
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
  },
});
