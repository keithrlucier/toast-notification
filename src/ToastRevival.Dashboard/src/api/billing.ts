import { api } from './client';

export interface BillingPlan {
  tier: string;
  tierLabel: string;
  licenseCount: number;
  deviceLimit: number | null;
  consumedCount: number;
  billingStatus: string;
  licenseStart: string | null;
  licenseEnd: string | null;
  stripeCustomerId: string | null;
  isNearLimit: boolean;
  isAtLimit: boolean;
}

export interface Invoice {
  id: string;
  status: string;
  amount: number;
  currency: string;
  created: string;
  periodStart: string;
  periodEnd: string;
  pdfUrl: string | null;
  hostedUrl: string | null;
}

export const billingApi = {
  getPlan: () =>
    api.get<BillingPlan>('/api/billing/plan'),

  createCheckout: (tier: 'Pro' | 'Enterprise') =>
    api.post<{ url: string }>('/api/billing/checkout', { tier }),

  createPortal: () =>
    api.post<{ url: string }>('/api/billing/portal'),

  getInvoices: () =>
    api.get<{ invoices: Invoice[] }>('/api/billing/invoices'),
};
