import { api, ApiError } from './client';

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
    const token = localStorage.getItem('token');
    const res = await fetch(`/api/audit/export?format=${format}&days=${days}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!res.ok) {
      let msg = `Export failed: ${res.status}`;
      try { const b = await res.json() as { message?: string }; msg = b.message ?? msg; } catch { /* ignore */ }
      throw new ApiError(res.status, msg);
    }
    await triggerDownload(res, `audit-log-${today()}.${format}`);
  },

  /** Fetches and triggers a browser file download of a per-notification delivery report. */
  exportDeliveryReport: async (notificationId: string, format: 'csv' | 'pdf'): Promise<void> => {
    const token = localStorage.getItem('token');
    const res = await fetch(`/api/notifications/${notificationId}/report?format=${format}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!res.ok) {
      let msg = `Export failed: ${res.status}`;
      try { const b = await res.json() as { message?: string }; msg = b.message ?? msg; } catch { /* ignore */ }
      throw new ApiError(res.status, msg);
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
