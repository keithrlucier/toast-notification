import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { devicesApi, type Device } from '../api/devices';
import { DeviceStatus } from '../components/StatusBadge';
import { ApiError } from '../api/client';
import DeployCommand from '../components/DeployCommand';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

function formatRelative(iso: string | null): string {
  if (!iso) return '—';
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1)  return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24)   return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

export default function Devices() {
  const navigate = useNavigate();
  const [devices, setDevices]     = useState<Device[]>([]);
  const [loading, setLoading]     = useState(true);
  const [error, setError]         = useState('');
  const [search, setSearch]       = useState('');
  const [filter, setFilter]       = useState<'all' | 'online' | 'offline'>('all');
  const [removing, setRemoving]   = useState<string | null>(null);
  const [selected, setSelected]   = useState<Set<string>>(new Set());

  const load = async () => {
    setLoading(true);
    try {
      const data = await devicesApi.list();
      setDevices(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to load devices.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const handleDecommission = async (id: string, name: string) => {
    if (!confirm(`Remove device "${name}" from this tenant? This cannot be undone.`)) return;
    setRemoving(id);
    try {
      await devicesApi.decommission(id);
      setDevices(prev => prev.filter(d => d.id !== id));
      setSelected(prev => { const next = new Set(prev); next.delete(id); return next; });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to remove device.');
    } finally {
      setRemoving(null);
    }
  };

  const filtered = devices.filter(d => {
    const matchSearch = !search ||
      d.machineName.toLowerCase().includes(search.toLowerCase()) ||
      d.username.toLowerCase().includes(search.toLowerCase()) ||
      d.osVersion.toLowerCase().includes(search.toLowerCase());
    const matchFilter = filter === 'all' || (filter === 'online' ? d.isOnline : !d.isOnline);
    return matchSearch && matchFilter;
  });

  const online = devices.filter(d => d.isOnline).length;

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

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Devices</h1>
          <p className="subtitle">
            {devices.length} endpoint{devices.length !== 1 ? 's' : ''} registered
            {' · '}{online} online
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {selected.size > 0 && (
            <button
              className="btn btn-primary"
              onClick={() => navigate('/compose', {
                state: { targetType: 'Device', targetIds: Array.from(selected) },
              })}
            >
              <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                <path d="M12.5 1.5l-6 11-1.5-4.5L1 6.5l11.5-5z" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" />
              </svg>
              Send to {selected.size} device{selected.size !== 1 ? 's' : ''}
            </button>
          )}
          <Link to="/devices/install" className="btn btn-secondary" style={{ textDecoration: 'none' }}>
            Install Agent
          </Link>
          <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
              <path d="M12.5 7A5.5 5.5 0 112.3 3.8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
              <path d="M2 1v3h3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            Refresh
          </button>
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Filters */}
      <div style={{ display: 'flex', gap: 12, marginBottom: 20 }}>
        <input
          type="search"
          placeholder="Search by machine, user, or OS..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{
            flex: 1,
            background: 'var(--bg-secondary)',
            border: '1px solid rgba(15,23,42,0.12)',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--text-primary)',
            padding: '9px 12px',
            fontSize: 14,
          }}
        />
        <div style={{ display: 'flex', gap: 1, background: 'var(--bg-secondary)', borderRadius: 'var(--radius-sm)', border: '1px solid rgba(15,23,42,0.12)', overflow: 'hidden' }}>
          {(['all', 'online', 'offline'] as const).map(f => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              style={{
                background: filter === f ? 'var(--bg-tertiary)' : 'transparent',
                border: 'none',
                color: filter === f ? 'var(--text-primary)' : 'var(--text-dim)',
                padding: '9px 16px',
                cursor: 'pointer',
                fontSize: 13,
                fontWeight: filter === f ? 600 : 400,
                transition: 'background 0.15s',
                textTransform: 'capitalize',
              }}
            >
              {f}
            </button>
          ))}
        </div>
      </div>

      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
            <span className="spinner" style={{ width: 24, height: 24, borderWidth: 3 }} />
          </div>
        ) : filtered.length === 0 && (search || filter !== 'all') ? (
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
                <th>OS</th>
                <th>Agent</th>
                <th>Status</th>
                <th>Last seen</th>
                <th>Registered</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(d => (
                <tr
                  key={d.id}
                  style={{ background: selected.has(d.id) ? 'rgba(245,158,11,0.06)' : undefined, cursor: 'default' }}
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
                  <td style={{ color: 'var(--text-dim)', fontSize: 12 }}>{d.osVersion}</td>
                  <td style={{ color: 'var(--text-dim)', fontSize: 12, fontFamily: 'var(--font-mono)' }}>{d.agentVersion}</td>
                  <td><DeviceStatus online={d.isOnline} /></td>
                  <td style={{ color: 'var(--text-dim)' }}>{formatRelative(d.lastSeen)}</td>
                  <td style={{ color: 'var(--text-dim)' }}>{formatDate(d.registeredAt)}</td>
                  <td>
                    <button
                      className="btn btn-ghost"
                      style={{ fontSize: 12, padding: '6px 10px', color: 'var(--status-error)' }}
                      onClick={() => void handleDecommission(d.id, d.machineName)}
                      disabled={removing === d.id}
                    >
                      {removing === d.id ? <span className="spinner" /> : 'Remove'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && (
        <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 12, textAlign: 'right' }}>
          Showing {filtered.length} of {devices.length} device{devices.length !== 1 ? 's' : ''}
        </p>
      )}
    </div>
  );
}
