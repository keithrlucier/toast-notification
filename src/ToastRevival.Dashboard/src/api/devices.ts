import { api } from './client';

export interface Device {
  id: string;
  tenantId: string;
  machineName: string;
  username: string;
  osVersion: string;
  agentVersion: string;
  isOnline: boolean;
  lastSeen: string | null;
  registeredAt: string;
  groupIds: string[];
}

export interface DeviceGroup {
  id: string;
  name: string;
  description: string | null;
  deviceCount: number;
  createdAt: string;
}

export const devicesApi = {
  list: () => api.get<Device[]>('/api/devices'),
  get: (id: string) => api.get<Device>(`/api/devices/${id}`),
  decommission: (id: string) => api.delete(`/api/devices/${id}`),
  listGroups: () => api.get<DeviceGroup[]>('/api/devicegroups'),
};
