import { api } from './client';

export interface BillingPlan {
  planName: string;
  pricePerDevice: number;
  minimumDevices: number;
  monthlyFloor: number;
  deviceCount: number;
  billableDevices: number;
  currentBill: number;
  billingStatus: string;
  licenseStart: string | null;
  licenseEnd: string | null;
  trialEnd: string | null;
  stripeCustomerId: string | null;
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

export interface BillingConfig {
  perDevicePriceId: string;
  isConfigured: boolean;
  pricePerDevice: number;
  minimumDevices: number;
  monthlyFloor: number;
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

  getBillingConfig: () =>
    api.get<BillingConfig>('/api/system/billing/config'),

  updateBillingConfig: (perDevicePriceId: string) =>
    api.post<BillingConfig>('/api/system/billing/config', { perDevicePriceId }),
};
