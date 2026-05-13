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

interface DeviceApiResponse {
  id?: string;
  deviceId?: string;
  tenantId?: string;
  machineName?: string;
  deviceName?: string;
  username?: string | null;
  osVersion?: string | null;
  agentVersion?: string | null;
  isOnline?: boolean;
  status?: string;
  lastSeen?: string | null;
  lastPing?: string | null;
  registeredAt?: string;
  groupIds?: string[];
}

export interface DeviceGroup {
  id: string;
  name: string;
  description: string | null;
  deviceCount: number;
  createdAt: string;
}

export interface DeviceGroupMember {
  deviceId: string;
  deviceName: string;
  agentVersion: string | null;
  addedAt: string;
}

export interface SaveDeviceGroupRequest {
  name: string;
  description?: string;
}

function isRecentlyOnline(status: string | undefined, lastSeen: string | null): boolean {
  if (status && status !== 'Active') return false;
  if (!lastSeen) return false;

  const seenAt = new Date(lastSeen).getTime();
  if (Number.isNaN(seenAt)) return false;

  return Date.now() - seenAt <= 45 * 60 * 1000;
}

function normalizeDevice(raw: DeviceApiResponse): Device {
  const lastSeen = raw.lastSeen ?? raw.lastPing ?? null;
  const machineName = raw.machineName ?? raw.deviceName ?? 'Unknown device';

  return {
    id: raw.id ?? raw.deviceId ?? '',
    tenantId: raw.tenantId ?? '',
    machineName,
    username: raw.username?.trim() || 'Unknown user',
    osVersion: raw.osVersion?.trim() || 'Unknown OS',
    agentVersion: raw.agentVersion?.trim() || 'Unknown',
    isOnline: typeof raw.isOnline === 'boolean'
      ? raw.isOnline
      : isRecentlyOnline(raw.status, lastSeen),
    lastSeen,
    registeredAt: raw.registeredAt ?? new Date(0).toISOString(),
    groupIds: raw.groupIds ?? [],
  };
}

export const devicesApi = {
  list: async () => {
    const devices = await api.get<DeviceApiResponse[]>('/api/devices');
    return devices.map(normalizeDevice);
  },
  get: async (id: string) => {
    const device = await api.get<DeviceApiResponse>(`/api/devices/${id}`);
    return normalizeDevice(device);
  },
  decommission: (id: string) => api.delete(`/api/devices/${id}`),
  listGroups: () => api.get<DeviceGroup[]>('/api/devicegroups'),
  createGroup: (req: SaveDeviceGroupRequest) =>
    api.post<DeviceGroup>('/api/devicegroups', req),
  updateGroup: (id: string, req: SaveDeviceGroupRequest) =>
    api.put<DeviceGroup>(`/api/devicegroups/${id}`, req),
  deleteGroup: (id: string) => api.delete(`/api/devicegroups/${id}`),
  listGroupMembers: (id: string) =>
    api.get<DeviceGroupMember[]>(`/api/devicegroups/${id}/members`),
  setGroupMembers: (id: string, deviceIds: string[]) =>
    api.put(`/api/devicegroups/${id}/members`, { deviceIds }),
  addGroupMember: (id: string, deviceId: string) =>
    api.post(`/api/devicegroups/${id}/members`, { deviceId }),
  removeGroupMember: (id: string, deviceId: string) =>
    api.delete(`/api/devicegroups/${id}/members/${deviceId}`),
};
