import { api } from './client';

export type TargetType = 'All' | 'Group' | 'Device';
export type NotificationStatus =
  | 'Queued' | 'Sending' | 'Sent' | 'PartiallyDelivered'
  | 'Failed' | 'PendingReview' | 'Rejected';

export interface ActionButton {
  label: string;
  actionId: string;
  style?: 'Default' | 'Success' | 'Critical';
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

export interface NotificationTemplate {
  id: string;
  name: string;
  description?: string;
  defaultTitle: string;
  defaultBodyLine1?: string;
  defaultBodyLine2?: string;
  defaultAudioSetting?: string;
  defaultScenario?: string;
  defaultButtons?: ActionButton[];
}

export const notificationsApi = {
  list: (page = 1, pageSize = 25) =>
    api.get<NotificationHistoryItem[]>(`/api/notifications?page=${page}&pageSize=${pageSize}`),

  get: (id: string) =>
    api.get<NotificationResponse>(`/api/notifications/${id}`),

  send: (req: SendNotificationRequest) =>
    api.post<NotificationResponse>('/api/notifications', req),

  templates: () =>
    api.get<NotificationTemplate[]>('/api/templates'),
};
