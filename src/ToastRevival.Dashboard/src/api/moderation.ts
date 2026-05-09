import { api } from './client';

export interface PendingNotification {
  notificationId: string;
  title: string;
  bodyLine1?: string;
  bodyLine2?: string;
  heroImageUrl?: string;
  submittedAt: string;
  submittedByEmail: string;
  targetType: string;
  deviceCount: number;
  moderationReason?: string;
}

export interface BlocklistEntry {
  id: string;
  term: string;
  createdAt: string;
  createdByEmail?: string;
}

export const moderationApi = {
  pending: () =>
    api.get<PendingNotification[]>('/api/moderation/pending'),

  approve: (id: string) =>
    api.post<void>(`/api/moderation/${id}/approve`),

  reject: (id: string, reason?: string) =>
    api.post<void>(`/api/moderation/${id}/reject`, { reason }),

  blocklist: () =>
    api.get<BlocklistEntry[]>('/api/blocklist'),

  addBlocklistTerm: (term: string) =>
    api.post<BlocklistEntry>('/api/blocklist', { term }),

  removeBlocklistTerm: (id: string) =>
    api.delete(`/api/blocklist/${id}`),
};
