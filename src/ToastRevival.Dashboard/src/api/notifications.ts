import { api } from './client';

export type TargetType = 'All' | 'Group' | 'Device';
export type NotificationStatus =
  | 'Queued' | 'Sending' | 'Sent' | 'PartialFailure' | 'PartiallyDelivered'
  | 'Failed' | 'PendingReview' | 'Rejected';

export interface ActionButton {
  label: string;
  actionId: string;
  style?: 'Default' | 'Success' | 'Critical';
  type?: 'Action' | 'Url';
  url?: string;
}

export interface SendNotificationRequest {
  title: string;
  bodyLine1?: string;
  bodyLine2?: string;
  heroImageUrl?: string;
  logoUrl?: string;
  actionButtons?: ActionButton[];
  audioSetting?: string;
  scenario?: string;
  targetType: TargetType;
  targetIds?: string[];
  templateId?: string;
  scheduledAt?: string;
}

/** Returned by POST /api/notifications and GET /api/notifications/{id} */
export interface NotificationResponse {
  id: string;
  title: string;
  bodyLine1?: string;
  bodyLine2?: string;
  status: NotificationStatus;
  targetType: TargetType;
  targetDeviceCount: number;
  scheduledAt?: string;
  sentAt?: string;
  createdAt: string;
}

/** Returned by GET /api/notifications (history list) */
export interface NotificationHistoryItem {
  id: string;
  title: string;
  status: NotificationStatus;
  targetDeviceCount: number;
  deliveredCount: number;
  clickedCount: number;
  createdAt: string;
  sentAt?: string;
}

/** Template record from the backend database */
export interface TemplateDbRecord {
  id: string;
  slug: string;
  name: string;
  category: string;
  titleTemplate: string | null;
  bodyLine1Template: string | null;
  bodyLine2Template: string | null;
  actionButtonsJson: string | null;
  audioSetting: string | null;
  scenario: string;
  isDefault: boolean;
}

export interface SaveTemplateRequest {
  name: string;
  title?: string;
  bodyLine1?: string;
  bodyLine2?: string;
  actionButtonsJson?: string;
  audioSetting?: string;
  scenario?: string;
}

export const notificationsApi = {
  list: (page = 1, pageSize = 25) =>
    api.get<NotificationHistoryItem[]>(`/api/notifications?page=${page}&pageSize=${pageSize}`),

  get: (id: string) =>
    api.get<NotificationResponse>(`/api/notifications/${id}`),

  send: (req: SendNotificationRequest) =>
    api.post<NotificationResponse>('/api/notifications', req),

  templates: () =>
    api.get<TemplateDbRecord[]>('/api/templates'),

  saveTemplate: (req: SaveTemplateRequest) =>
    api.post<TemplateDbRecord>('/api/templates', req),

  deleteTemplate: (id: string) =>
    api.delete(`/api/templates/${id}`),
};
