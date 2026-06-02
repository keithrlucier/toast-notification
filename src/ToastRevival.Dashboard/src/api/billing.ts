import { api } from './client';

export interface BillingPlan {
  planName: string;
  pricePerDevice: number;
  freeTierLimit: number;
  deviceCount: number;
  billableDevices: number;
  currentBill: number;
  billingStatus: string;
  licenseStart: string | null;
  licenseEnd: string | null;
  trialEnd: string | null;
  stripeCustomerId: string | null;
  billingEnabled?: boolean;
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

  createCheckout: () =>
    api.post<{ url: string }>('/api/billing/checkout'),

  createPortal: () =>
    api.post<{ url: string }>('/api/billing/portal'),

  getInvoices: () =>
    api.get<{ invoices: Invoice[] }>('/api/billing/invoices'),
};
