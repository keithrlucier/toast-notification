// Types and api helpers for the asset library
export interface AssetRecord {
  id: string;
  name: string;
  type: 'HeroImage' | 'Logo' | 'Icon';
  url: string;
  moderationResultJson?: string;
  uploadedAt: string;
}

export type ModerationStatus = 'Pass' | 'Review' | 'Block' | 'Unknown';

export function getModerationStatus(json?: string): ModerationStatus {
  if (!json) return 'Unknown';
  try {
    const parsed = JSON.parse(json) as { decision?: string };
    if (parsed.decision === 'Pass') return 'Pass';
    if (parsed.decision === 'Review') return 'Review';
    if (parsed.decision === 'Block') return 'Block';
  } catch { /* ignore */ }
  return 'Unknown';
}

import { api, ApiError } from './client';

export const assetsApi = {
  list: () => api.get<AssetRecord[]>('/api/assets'),
  delete: (id: string) => api.delete<void>(`/api/assets/${id}`),

  upload: async (file: File, name?: string, assetType?: 'HeroImage' | 'Logo' | 'Icon'): Promise<AssetRecord> => {
    const token = localStorage.getItem('token');
    const form = new FormData();
    form.append('file', file);
    if (name) form.append('name', name);
    form.append('assetType', assetType ?? 'HeroImage');

    const res = await fetch('/api/assets', {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
    });

    if (!res.ok) {
      let message = `HTTP ${res.status}`;
      try {
        const body = await res.json() as { message?: string; title?: string };
        message = body.message ?? body.title ?? message;
      } catch { /* ignore */ }
      throw new ApiError(res.status, message);
    }
    return res.json() as Promise<AssetRecord>;
  },
};
