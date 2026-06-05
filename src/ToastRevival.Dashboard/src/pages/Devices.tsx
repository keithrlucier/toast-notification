import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { devicesApi, type Device, type DeviceGroup } from '../api/devices';
import { DeviceStatus } from '../components/StatusBadge';
import { ApiError } from '../api/client';
import DeployCommand from '../components/DeployCommand';
import RemoveAgentModal from '../components/RemoveAgentModal';

type StatusFilter = 'all' | 'online' | 'offline';
type GroupFilter = 'all' | 'ungrouped' | string;
type SortKey = 'machine' | 'user' | 'group' | 'status' | 'lastSeen' | 'registered';
type SortDir = 'asc' | 'desc';

interface GroupModalState {
  group: DeviceGroup | null;
  initialDeviceIds: string[];
}

interface SaveGroupArgs {
  groupId?: string;
  name: string;
  description?: string;
  deviceIds: string[];
}

const SORT_OPTIONS: { value: SortKey; label: string }[] = [
  { value: 'machine', label: 'Machine' },
  { value: 'user', label: 'User' },
  { value: 'group', label: 'Group' },
  { value: 'status', label: 'Status' },
  { value: 'lastSeen', label: 'Last seen' },
  { value: 'registered', label: 'Registered' },
];

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

function formatRelative(iso: string | null): string {
  if (!iso) return '-';
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1)  return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24)   return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

function groupNames(device: Device, groupById: Map<string, DeviceGroup>): string[] {
  return device.groupIds
    .map(id => groupById.get(id)?.name)
    .filter((name): name is string => Boolean(name));
}

function groupLabel(device: Device, groupById: Map<string, DeviceGroup>): string {
  const names = groupNames(device, groupById);
  return names.length > 0 ? names.join(', ') : 'Ungrouped';
}

function compareText(a: string, b: string) {
  return a.localeCompare(b, undefined, { sensitivity: 'base' });
}

function sortDevices(a: Device, b: Device, sortKey: SortKey, groupById: Map<string, DeviceGroup>) {
  switch (sortKey) {
    case 'user':
      return compareText(a.username, b.username) || compareText(a.machineName, b.machineName);
    case 'group':
      return compareText(groupLabel(a, groupById), groupLabel(b, groupById)) || compareText(a.machineName, b.machineName);
    case 'status':
      return Number(b.isOnline) - Number(a.isOnline) || compareText(a.machineName, b.machineName);
    case 'lastSeen':
      return (new Date(a.lastSeen ?? 0).getTime() - new Date(b.lastSeen ?? 0).getTime())
        || compareText(a.machineName, b.machineName);
    case 'registered':
      return (new Date(a.registeredAt).getTime() - new Date(b.registeredAt).getTime())
        || compareText(a.machineName, b.machineName);
    case 'machine':
    default:
      return compareText(a.machineName, b.machineName);
  }
}

export default function Devices() {
  const navigate = useNavigate();
  const [devices, setDevices]       = useState<Device[]>([]);
  const [groups, setGroups]         = useState<DeviceGroup[]>([]);
  const [loading, setLoading]       = useState(true);
  const [error, setError]           = useState('');
  const [search, setSearch]         = useState('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [groupFilter, setGroupFilter]   = useState<GroupFilter>('all');
  const [sortKey, setSortKey]       = useState<SortKey>('machine');
  const [sortDir, setSortDir]       = useState<SortDir>('asc');
  const [removing, setRemoving]     = useState<string | null>(null);
  const [uninstalling, setUninstalling] = useState<string | null>(null);
  const [checking, setChecking]     = useState<string | null>(null);
  const [pushingFleet, setPushingFleet] = useState(false);
  const [notice, setNotice]         = useState('');
  // The agent runs unprivileged and can't reliably self-remove, so the button
  // opens a modal with the manual removal command + a best-effort remote attempt.
  const [removeTarget, setRemoveTarget] = useState<Device | null>(null);
  const [selected, setSelected]     = useState<Set<string>>(new Set());
  const [bulkGroupId, setBulkGroupId] = useState('');
  const [groupModal, setGroupModal] = useState<GroupModalState | null>(null);
  const [savingGroup, setSavingGroup] = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const [deviceData, groupData] = await Promise.all([
        devicesApi.list(),
        devicesApi.listGroups(),
      ]);
      setDevices(deviceData);
      setGroups(groupData);
      setSelected(prev => new Set([...prev].filter(id => deviceData.some(d => d.id === id))));
      setBulkGroupId(current => current && groupData.some(g => g.id === current) ? current : '');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load devices.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const groupById = useMemo(() => new Map(groups.map(g => [g.id, g])), [groups]);
  const activeGroup = groupFilter !== 'all' && groupFilter !== 'ungrouped'
    ? groupById.get(groupFilter)
    : null;

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const rows = devices.filter(d => {
      const names = groupNames(d, groupById);
      const matchSearch = !q ||
        d.machineName.toLowerCase().includes(q) ||
        d.username.toLowerCase().includes(q) ||
        d.osVersion.toLowerCase().includes(q) ||
        names.some(name => name.toLowerCase().includes(q));

      const matchStatus =
        statusFilter === 'all' ||
        (statusFilter === 'online' ? d.isOnline : !d.isOnline);

      const matchGroup =
        groupFilter === 'all' ||
        (groupFilter === 'ungrouped' ? d.groupIds.length === 0 : d.groupIds.includes(groupFilter));

      return matchSearch && matchStatus && matchGroup;
    });

    rows.sort((a, b) => {
      const result = sortDevices(a, b, sortKey, groupById);
      return sortDir === 'asc' ? result : -result;
    });

    return rows;
  }, [devices, groupById, groupFilter, search, sortDir, sortKey, statusFilter]);

  const online = devices.filter(d => d.isOnline).length;

  const handleDecommission = async (id: string, name: string) => {
    if (!confirm(`Remove device "${name}" from this tenant? This cannot be undone.`)) return;
    setRemoving(id);
    try {
      await devicesApi.decommission(id);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to remove device.');
    } finally {
      setRemoving(null);
    }
  };

  // Best-effort remote removal, invoked from the modal's "Attempt remote removal"
  // button. Decommissions the device (frees the license) and pushes the uninstall
  // command to the agent if it's online and on a build that supports it. Throws on
  // failure so the modal can surface the fallback guidance.
  const handleRemoteUninstall = async (id: string) => {
    setUninstalling(id);
    try {
      await devicesApi.uninstall(id);
      await load();
    } finally {
      setUninstalling(null);
    }
  };

  // Tell a single online agent to run its self-update check now instead of
  // waiting for the 24h poll. Offline devices can't be reached and update on
  // their next check-in; the button is disabled for them.
  const handleCheckUpdate = async (id: string, name: string) => {
    setChecking(id);
    setNotice('');
    setError('');
    try {
      const { pushed } = await devicesApi.checkUpdate(id);
      setNotice(pushed
        ? `Update check pushed to "${name}". If a newer version is published it'll pull it now.`
        : `"${name}" is offline — it'll check for updates on its next check-in.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to push update check.');
    } finally {
      setChecking(null);
    }
  };

  // Fleet-wide: push the update check to every online device in this tenant so
  // the whole fleet rolls forward at once instead of waiting on per-device timers.
  const handlePushFleetUpdate = async () => {
    if (!confirm('Push an update check to every online device in this tenant now? Online agents will check for the latest published version immediately.')) return;
    setPushingFleet(true);
    setNotice('');
    setError('');
    try {
      const { pushed, total } = await devicesApi.checkUpdateAll();
      setNotice(`Update check pushed to ${pushed} online device${pushed !== 1 ? 's' : ''} of ${total}. Offline devices update on their next check-in.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to push fleet update.');
    } finally {
      setPushingFleet(false);
    }
  };

  const handleSaveGroup = async ({ groupId, name, description, deviceIds }: SaveGroupArgs) => {
    setSavingGroup(true);
    setError('');
    try {
      const group = groupId
        ? await devicesApi.updateGroup(groupId, { name, description })
        : await devicesApi.createGroup({ name, description });

      await devicesApi.setGroupMembers(group.id, deviceIds);
      setGroupModal(null);
      setSelected(new Set());
      setGroupFilter(group.id);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save device group.');
    } finally {
      setSavingGroup(false);
    }
  };

  const handleDeleteGroup = async (group: DeviceGroup) => {
    if (!confirm(`Delete device group "${group.name}"? Devices will not be removed from the tenant.`)) return;
    setError('');
    try {
      await devicesApi.deleteGroup(group.id);
      if (groupFilter === group.id) setGroupFilter('all');
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to delete device group.');
    }
  };

  const handleBulkAddToGroup = async () => {
    if (!bulkGroupId || selected.size === 0) return;
    setError('');
    try {
      const existing = devices
        .filter(d => d.groupIds.includes(bulkGroupId))
        .map(d => d.id);
      const next = Array.from(new Set([...existing, ...selected]));
      await devicesApi.setGroupMembers(bulkGroupId, next);
      setSelected(new Set());
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to add devices to group.');
    }
  };

  const toggleSelect = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    const allSelected = filtered.length > 0 && filtered.every(d => selected.has(d.id));
    setSelected(prev => {
      const next = new Set(prev);
      filtered.forEach(d => allSelected ? next.delete(d.id) : next.add(d.id));
      return next;
    });
  };

  const openCreateGroup = (deviceIds: string[] = []) => {
    setGroupModal({ group: null, initialDeviceIds: deviceIds });
  };

  const openEditGroup = (group: DeviceGroup) => {
    setGroupModal({
      group,
      initialDeviceIds: devices.filter(d => d.groupIds.includes(group.id)).map(d => d.id),
    });
  };

  const messageGroup = (group: DeviceGroup) => {
    navigate('/compose', {
      state: { targetType: 'Group', targetIds: [group.id] },
    });
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Devices</h1>
          <p className="subtitle">
            {devices.length} endpoint{devices.length !== 1 ? 's' : ''} registered
            {' - '}{online} online
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'flex-end', flexWrap: 'wrap' }}>
          {activeGroup && (
            <button className="btn btn-primary" onClick={() => messageGroup(activeGroup)}>
              <SendIcon />
              Message {activeGroup.name}
            </button>
          )}
          {selected.size > 0 && (
            <button
              className="btn btn-secondary"
              onClick={() => navigate('/compose', {
                state: { targetType: 'Device', targetIds: Array.from(selected) },
              })}
            >
              <SendIcon />
              Send to {selected.size}
            </button>
          )}
          <button className="btn btn-primary" onClick={() => openCreateGroup(Array.from(selected))}>
            New Group
          </button>
          <Link to="/devices/install" className="btn btn-secondary" style={{ textDecoration: 'none' }}>
            Install Agent
          </Link>
          <button
            className="btn btn-secondary"
            onClick={() => void handlePushFleetUpdate()}
            disabled={pushingFleet || loading || devices.length === 0}
            title="Tell every online agent in this tenant to check for the latest version now"
          >
            {pushingFleet ? <span className="spinner" /> : 'Push Update'}
          </button>
          <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
            <RefreshIcon />
            Refresh
          </button>
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {notice && (
        <div
          style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12,
            padding: '10px 14px', marginBottom: 16, borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--accent)', background: 'rgba(31,111,189,0.06)',
            color: 'var(--text-secondary)', fontSize: 13,
          }}
        >
          <span>{notice}</span>
          <button
            className="btn btn-ghost"
            style={{ fontSize: 12, padding: '2px 8px' }}
            onClick={() => setNotice('')}
          >
            Dismiss
          </button>
        </div>
      )}

      <div className="card" style={{ padding: 0, marginBottom: 16, overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '14px 16px', borderBottom: '1px solid var(--border-subtle)' }}>
          <div>
            <h2 style={{ fontSize: 14, margin: 0 }}>Device groups</h2>
            <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 2 }}>
              Create tenant groups for targeting and recurring filters.
            </p>
          </div>
          <button className="btn btn-secondary" onClick={() => openCreateGroup(Array.from(selected))}>
            Create Group
          </button>
        </div>

        {groups.length === 0 ? (
          <div className="empty-state" style={{ padding: 28 }}>
            <p>No device groups yet.</p>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            {groups.map(group => (
              <div
                key={group.id}
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'minmax(180px, 1fr) minmax(120px, 180px) auto',
                  gap: 12,
                  alignItems: 'center',
                  padding: '12px 16px',
                  borderTop: '1px solid #E5EAF0',
                }}
              >
                <div style={{ minWidth: 0 }}>
                  <div style={{ display: 'flex', gap: 8, alignItems: 'center', minWidth: 0 }}>
                    <strong style={{ color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{group.name}</strong>
                    {groupFilter === group.id && (
                      <span style={{ fontSize: 11, color: 'var(--accent)', fontWeight: 700 }}>Filtered</span>
                    )}
                  </div>
                  {group.description && (
                    <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {group.description}
                    </p>
                  )}
                </div>
                <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>
                  {group.deviceCount} active device{group.deviceCount !== 1 ? 's' : ''}
                </div>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                  <button className="btn btn-ghost" style={{ fontSize: 12, padding: '6px 10px' }} onClick={() => setGroupFilter(group.id)}>
                    Filter
                  </button>
                  <button className="btn btn-ghost" style={{ fontSize: 12, padding: '6px 10px' }} onClick={() => messageGroup(group)}>
                    Message
                  </button>
                  <button className="btn btn-ghost" style={{ fontSize: 12, padding: '6px 10px' }} onClick={() => openEditGroup(group)}>
                    Manage
                  </button>
                  <button
                    className="btn btn-ghost"
                    style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-error)' }}
                    onClick={() => void handleDeleteGroup(group)}
                  >
                    Delete
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {selected.size > 0 && (
        <div className="card" style={{ padding: 14, marginBottom: 16, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
            {selected.size} selected
          </span>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <select
              value={bulkGroupId}
              onChange={e => setBulkGroupId(e.target.value)}
              style={{
                minWidth: 180,
                background: '#FFFFFF',
                border: '1px solid #C8D3DF',
                borderRadius: 'var(--radius-sm)',
                color: 'var(--text-primary)',
                padding: '8px 10px',
              }}
            >
              <option value="">Choose group...</option>
              {groups.map(group => <option key={group.id} value={group.id}>{group.name}</option>)}
            </select>
            <button className="btn btn-secondary" disabled={!bulkGroupId} onClick={() => void handleBulkAddToGroup()}>
              Add to Group
            </button>
            <button className="btn btn-ghost" onClick={() => openCreateGroup(Array.from(selected))}>
              New from Selection
            </button>
          </div>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(220px, 1fr) 170px 190px 170px 44px', gap: 12, marginBottom: 16 }}>
        <input
          type="search"
          placeholder="Search by machine, user, OS, or group..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.12)',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--text-primary)',
            padding: '9px 12px',
            fontSize: 14,
            minWidth: 0,
          }}
        />
        <select
          value={groupFilter}
          onChange={e => setGroupFilter(e.target.value)}
          style={{
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.12)',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--text-primary)',
            padding: '9px 10px',
          }}
        >
          <option value="all">All groups</option>
          <option value="ungrouped">Ungrouped</option>
          {groups.map(group => <option key={group.id} value={group.id}>{group.name}</option>)}
        </select>
        <div style={{ display: 'flex', gap: 1, background: 'var(--bg-secondary)', borderRadius: 'var(--radius-sm)', border: '1px solid rgba(15,23,42,0.12)', overflow: 'hidden' }}>
          {(['all', 'online', 'offline'] as const).map(f => (
            <button
              key={f}
              onClick={() => setStatusFilter(f)}
              style={{
                flex: 1,
                background: statusFilter === f ? 'var(--bg-tertiary)' : 'transparent',
                border: 'none',
                color: statusFilter === f ? 'var(--text-primary)' : 'var(--text-dim)',
                padding: '9px 10px',
                cursor: 'pointer',
                fontSize: 13,
                fontWeight: statusFilter === f ? 600 : 400,
                textTransform: 'capitalize',
              }}
            >
              {f}
            </button>
          ))}
        </div>
        <select
          value={sortKey}
          onChange={e => setSortKey(e.target.value as SortKey)}
          aria-label="Sort devices"
          style={{
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.12)',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--text-primary)',
            padding: '9px 10px',
          }}
        >
          {SORT_OPTIONS.map(option => <option key={option.value} value={option.value}>Sort: {option.label}</option>)}
        </select>
        <button
          className="btn btn-secondary"
          style={{ minHeight: 38, padding: 0, justifyContent: 'center' }}
          onClick={() => setSortDir(dir => dir === 'asc' ? 'desc' : 'asc')}
          aria-label={`Sort ${sortDir === 'asc' ? 'descending' : 'ascending'}`}
          title={`Sort ${sortDir === 'asc' ? 'descending' : 'ascending'}`}
        >
          {sortDir === 'asc' ? 'A-Z' : 'Z-A'}
        </button>
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
            <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
          </div>
        ) : filtered.length === 0 && (search || statusFilter !== 'all' || groupFilter !== 'all') ? (
          <div className="empty-state">
            <p>No devices match your filters.</p>
          </div>
        ) : filtered.length === 0 ? (
          <div style={{ padding: 24 }}>
            <DeployCommand />
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th style={{ width: 40, paddingLeft: 16 }}>
                  <input
                    type="checkbox"
                    checked={filtered.length > 0 && filtered.every(d => selected.has(d.id))}
                    ref={el => { if (el) el.indeterminate = filtered.some(d => selected.has(d.id)) && !filtered.every(d => selected.has(d.id)); }}
                    onChange={toggleSelectAll}
                    aria-label="Select all"
                  />
                </th>
                <th>Machine</th>
                <th>User</th>
                <th>IP</th>
                <th>Groups</th>
                <th>OS</th>
                <th>Agent</th>
                <th>Status</th>
                <th>Last seen</th>
                <th>Registered</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(d => {
                const names = groupNames(d, groupById);
                return (
                  <tr
                    key={d.id}
                    style={{ background: selected.has(d.id) ? 'rgba(31,111,189,0.06)' : undefined, cursor: 'default' }}
                  >
                    <td style={{ paddingLeft: 16 }}>
                      <input
                        type="checkbox"
                        checked={selected.has(d.id)}
                        onChange={() => toggleSelect(d.id)}
                        aria-label={`Select ${d.machineName}`}
                      />
                    </td>
                    <td style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>{d.machineName}</td>
                    <td>{d.username}</td>
                    <td
                      style={{ color: 'var(--text-dim)', fontSize: 12, fontFamily: 'var(--font-mono)' }}
                      title={[
                        d.wanIpAddress ? `WAN: ${d.wanIpAddress}` : null,
                        d.lanIpAddress ? `LAN: ${d.lanIpAddress}` : null,
                      ].filter(Boolean).join('\n') || undefined}
                    >
                      {d.wanIpAddress
                        ? d.wanIpAddress.length > 20 ? `${d.wanIpAddress.slice(0, 20)}…` : d.wanIpAddress
                        : '—'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap', maxWidth: 220 }}>
                        {names.length === 0 ? (
                          <span style={{ color: 'var(--text-dim)', fontSize: 12 }}>Ungrouped</span>
                        ) : names.map(name => (
                          <span
                            key={name}
                            style={{
                              maxWidth: 120,
                              overflow: 'hidden',
                              textOverflow: 'ellipsis',
                              whiteSpace: 'nowrap',
                              padding: '2px 6px',
                              borderRadius: 'var(--radius-sm)',
                              background: '#EEF3F8',
                              color: 'var(--text-secondary)',
                              fontSize: 11,
                              fontWeight: 700,
                            }}
                            title={name}
                          >
                            {name}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td style={{ color: 'var(--text-dim)', fontSize: 12 }}>{d.osVersion}</td>
                    <td style={{ color: 'var(--text-dim)', fontSize: 12, fontFamily: 'var(--font-mono)' }}>{d.agentVersion}</td>
                    <td><DeviceStatus online={d.isOnline} /></td>
                    <td style={{ color: 'var(--text-dim)' }}>{formatRelative(d.lastSeen)}</td>
                    <td style={{ color: 'var(--text-dim)' }}>{formatDate(d.registeredAt)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '6px 10px', color: 'var(--accent)' }}
                          onClick={() => void handleCheckUpdate(d.id, d.machineName)}
                          disabled={!d.isOnline || checking === d.id || removing === d.id || uninstalling === d.id}
                          title={d.isOnline ? 'Tell this agent to check for the latest version now' : 'Offline — updates on next check-in'}
                        >
                          {checking === d.id ? <span className="spinner" /> : 'Update'}
                        </button>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-error)' }}
                          onClick={() => void handleDecommission(d.id, d.machineName)}
                          disabled={removing === d.id || uninstalling === d.id}
                        >
                          {removing === d.id ? <span className="spinner" /> : 'Remove'}
                        </button>
                        <button
                          className="btn btn-ghost"
                          style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-warning)' }}
                          onClick={() => setRemoveTarget(d)}
                          disabled={removing === d.id || uninstalling === d.id}
                          title="Show how to uninstall the agent software from this device"
                        >
                          {uninstalling === d.id ? <span className="spinner" /> : 'Uninstall'}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {!loading && (
        <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 12, textAlign: 'right' }}>
          Showing {filtered.length} of {devices.length} device{devices.length !== 1 ? 's' : ''}
        </p>
      )}

      {groupModal && (
        <DeviceGroupModal
          key={groupModal.group?.id ?? 'new'}
          devices={devices}
          group={groupModal.group}
          initialDeviceIds={groupModal.initialDeviceIds}
          saving={savingGroup}
          onSave={args => void handleSaveGroup(args)}
          onClose={() => setGroupModal(null)}
        />
      )}

      {removeTarget && (
        <RemoveAgentModal
          machineName={removeTarget.machineName}
          isOnline={removeTarget.isOnline}
          onRemoteUninstall={() => handleRemoteUninstall(removeTarget.id)}
          onClose={() => setRemoveTarget(null)}
        />
      )}
    </div>
  );
}

interface DeviceGroupModalProps {
  devices: Device[];
  group: DeviceGroup | null;
  initialDeviceIds: string[];
  saving: boolean;
  onSave: (args: SaveGroupArgs) => void;
  onClose: () => void;
}

function DeviceGroupModal({ devices, group, initialDeviceIds, saving, onSave, onClose }: DeviceGroupModalProps) {
  const [name, setName] = useState(group?.name ?? '');
  const [description, setDescription] = useState(group?.description ?? '');
  const [memberIds, setMemberIds] = useState<Set<string>>(() => new Set(initialDeviceIds));
  const [search, setSearch] = useState('');

  const visibleDevices = devices.filter(device => {
    const q = search.trim().toLowerCase();
    return !q ||
      device.machineName.toLowerCase().includes(q) ||
      device.username.toLowerCase().includes(q) ||
      device.osVersion.toLowerCase().includes(q);
  });

  const allVisibleSelected = visibleDevices.length > 0 && visibleDevices.every(device => memberIds.has(device.id));

  const toggle = (id: string) => {
    setMemberIds(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  };

  const toggleVisible = () => {
    setMemberIds(prev => {
      const next = new Set(prev);
      visibleDevices.forEach(device => allVisibleSelected ? next.delete(device.id) : next.add(device.id));
      return next;
    });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    onSave({
      groupId: group?.id,
      name: trimmed,
      description: description.trim() || undefined,
      deviceIds: Array.from(memberIds),
    });
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <form
        className="modal"
        style={{ maxWidth: 760, width: '92vw', maxHeight: '84vh', display: 'flex', flexDirection: 'column', gap: 16 }}
        onClick={event => event.stopPropagation()}
        onSubmit={submit}
      >
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12 }}>
          <div>
            <h2 style={{ margin: 0 }}>{group ? 'Manage Device Group' : 'Create Device Group'}</h2>
            <p style={{ margin: '4px 0 0', fontSize: 13 }}>
              {memberIds.size} device{memberIds.size !== 1 ? 's' : ''} selected
            </p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={onClose} style={{ padding: '6px 10px' }}>
            Close
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(180px, 1fr) minmax(220px, 2fr)', gap: 12 }}>
          <div className="field">
            <label htmlFor="groupName">Name</label>
            <input
              id="groupName"
              value={name}
              maxLength={100}
              onChange={event => setName(event.target.value)}
              placeholder="Servers, Finance, Pilot users"
              autoFocus
            />
          </div>
          <div className="field">
            <label htmlFor="groupDescription">Description</label>
            <input
              id="groupDescription"
              value={description}
              maxLength={500}
              onChange={event => setDescription(event.target.value)}
              placeholder="Optional operational context"
            />
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <input
            type="search"
            value={search}
            onChange={event => setSearch(event.target.value)}
            placeholder="Search devices..."
            style={{
              flex: 1,
              minWidth: 0,
              background: '#FFFFFF',
              border: '1px solid #C8D3DF',
              borderRadius: 'var(--radius-sm)',
              color: 'var(--text-primary)',
              padding: '9px 12px',
            }}
          />
          <button type="button" className="btn btn-secondary" onClick={toggleVisible} disabled={visibleDevices.length === 0}>
            {allVisibleSelected ? 'Clear Visible' : 'Select Visible'}
          </button>
        </div>

        <div style={{ overflowY: 'auto', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-sm)', minHeight: 220 }}>
          {visibleDevices.length === 0 ? (
            <div className="empty-state" style={{ padding: 32 }}>
              <p>No devices found.</p>
            </div>
          ) : (
            visibleDevices.map(device => (
              <label
                key={device.id}
                style={{
                  display: 'grid',
                  gridTemplateColumns: '24px minmax(160px, 1fr) minmax(120px, 180px) 90px',
                  gap: 10,
                  alignItems: 'center',
                  padding: '10px 12px',
                  borderBottom: '1px solid #E5EAF0',
                  cursor: 'pointer',
                  background: memberIds.has(device.id) ? 'rgba(31,111,189,0.06)' : '#FFFFFF',
                }}
              >
                <input
                  type="checkbox"
                  checked={memberIds.has(device.id)}
                  onChange={() => toggle(device.id)}
                  style={{ accentColor: 'var(--accent)' }}
                />
                <span style={{ minWidth: 0 }}>
                  <span style={{ display: 'block', color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', fontSize: 12, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {device.machineName}
                  </span>
                  <span style={{ display: 'block', color: 'var(--text-dim)', fontSize: 12, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {device.osVersion}
                  </span>
                </span>
                <span style={{ color: 'var(--text-secondary)', fontSize: 13, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{device.username}</span>
                <DeviceStatus online={device.isOnline} />
              </label>
            ))
          )}
        </div>

        <div className="modal-actions" style={{ paddingTop: 0 }}>
          <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn btn-primary" disabled={saving || !name.trim()}>
            {saving ? <span className="spinner" /> : group ? 'Save Group' : 'Create Group'}
          </button>
        </div>
      </form>
    </div>
  );
}

function SendIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
      <path d="M12.5 1.5l-6 11-1.5-4.5L1 6.5l11.5-5z" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" />
    </svg>
  );
}

function RefreshIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
      <path d="M12.5 7A5.5 5.5 0 112.3 3.8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M2 1v3h3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
