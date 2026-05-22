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

import { api, apiErrorFromResponse, authHeaders } from './client';

export const assetsApi = {
  list: () => api.get<AssetRecord[]>('/api/assets'),
  delete: (id: string) => api.delete<void>(`/api/assets/${id}`),
  rename: (id: string, name: string) =>
    api.patch<AssetRecord>(`/api/assets/${id}`, { name }),

  upload: async (file: File, name?: string, assetType?: 'HeroImage' | 'Logo' | 'Icon'): Promise<AssetRecord> => {
    const form = new FormData();
    form.append('file', file);
    if (name) form.append('name', name);
    form.append('assetType', assetType ?? 'HeroImage');

    const res = await fetch('/api/assets', {
      method: 'POST',
      headers: authHeaders(),
      body: form,
    });

    if (!res.ok) {
      throw await apiErrorFromResponse(res, '/api/assets');
    }
    return res.json() as Promise<AssetRecord>;
  },
};
