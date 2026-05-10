import { api, apiErrorFromResponse, authHeaders } from './client';

export interface AuditLogEntry {
  id: string;
  action: string;
  resourceType: string;
  resourceId: string | null;
  userId: string | null;
  ipAddress: string | null;
  timestamp: string;
}

export const auditApi = {
  list: (days = 30, page = 1, pageSize = 50): Promise<AuditLogEntry[]> =>
    api.get<AuditLogEntry[]>(`/api/audit?days=${days}&page=${page}&pageSize=${pageSize}`),

  /** Fetches and triggers a browser file download of the audit log. */
  exportFile: async (format: 'csv' | 'pdf', days = 30): Promise<void> => {
    const path = `/api/audit/export?format=${format}&days=${days}`;
    const res = await fetch(path, { headers: authHeaders() });
    if (!res.ok) {
      throw await apiErrorFromResponse(res, path, `Export failed: ${res.status}`);
    }
    await triggerDownload(res, `audit-log-${today()}.${format}`);
  },

  /** Fetches and triggers a browser file download of a per-notification delivery report. */
  exportDeliveryReport: async (notificationId: string, format: 'csv' | 'pdf'): Promise<void> => {
    const path = `/api/notifications/${notificationId}/report?format=${format}`;
    const res = await fetch(path, { headers: authHeaders() });
    if (!res.ok) {
      throw await apiErrorFromResponse(res, path, `Export failed: ${res.status}`);
    }
    await triggerDownload(res, `delivery-${notificationId.slice(0, 8)}.${format}`);
  },
};

async function triggerDownload(res: Response, filename: string): Promise<void> {
  const blob = await res.blob();
  const url  = URL.createObjectURL(blob);
  const a    = document.createElement('a');
  a.href     = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}
