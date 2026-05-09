import { useEffect, useState } from 'react';
import {
  LineChart, Line, BarChart, Bar, XAxis, YAxis,
  CartesianGrid, Tooltip, Legend, ResponsiveContainer, Cell,
} from 'recharts';
import { api, ApiError } from '../api/client';

interface Summary {
  sentCount: number;
  deliveryRate: number;
  interactionRate: number;
  activeDeviceCount: number;
}

interface VolumePoint {
  date: string;
  sent: number;
  delivered: number;
}

interface Breakdown {
  byStatus: Record<string, number>;
  byTemplate: Record<string, number>;
}

// Diana's custom tooltip — dark background, no default Recharts styling
function ChartTooltip({ active, payload, label }: {
  active?: boolean;
  payload?: Array<{ name: string; value: number; color: string }>;
  label?: string;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div style={{
      background: 'var(--bg-tertiary)',
      border: '1px solid rgba(255,255,255,0.08)',
      borderRadius: 6,
      padding: '8px 12px',
      fontSize: 12,
    }}>
      {label && <div style={{ color: 'var(--text-dim)', marginBottom: 4 }}>{label}</div>}
      {payload.map((entry, i) => (
        <div key={i} style={{ display: 'flex', gap: 8, alignItems: 'center', color: entry.color }}>
          <span>{entry.name}:</span>
          <span style={{ fontWeight: 600 }}>{entry.value}</span>
        </div>
      ))}
    </div>
  );
}

const STATUS_COLORS: Record<string, string> = {
  Delivered: '#4ADE80',
  Clicked:   '#00C9A7',
  Dismissed: '#7A7A92',
  Failed:    '#F87171',
  Pending:   '#FBBF24',
};

const TEMPLATE_LABELS: Record<string, string> = {
  announcement:  'Announcement',
  alert:         'Alert',
  actionrequired: 'Action Req.',
  reminder:      'Reminder',
  celebration:   'Celebration',
  maintenance:   'Maintenance',
};

export default function Analytics() {
  const [days, setDays]         = useState(30);
  const [summary, setSummary]   = useState<Summary | null>(null);
  const [volume, setVolume]     = useState<VolumePoint[]>([]);
  const [breakdown, setBreakdown] = useState<Breakdown | null>(null);
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    Promise.all([
      api.get<Summary>(`/api/analytics/summary?days=${days}`),
      api.get<VolumePoint[]>(`/api/analytics/volume?days=${days}`),
      api.get<Breakdown>(`/api/analytics/breakdown?days=${days}`),
    ]).then(([s, v, b]) => {
      if (cancelled) return;
      setSummary(s);
      setVolume(v);
      setBreakdown(b);
      setLoading(false);
    }).catch(err => {
      if (cancelled) return;
      setError(err instanceof ApiError ? err.message : 'Failed to load analytics.');
      setLoading(false);
    });
    return () => { cancelled = true; };
  }, [days]);

  const statusData = breakdown
    ? Object.entries(breakdown.byStatus).map(([name, value]) => ({ name, value }))
    : [];

  const templateData = breakdown
    ? Object.entries(breakdown.byTemplate).map(([key, value]) => ({
        name: TEMPLATE_LABELS[key] ?? key,
        value,
      }))
    : [];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Analytics</h1>
          <p className="subtitle">Delivery performance and notification trends</p>
        </div>
        {/* Time range selector — segmented button control per Diana's spec */}
        <div style={{
          display: 'flex',
          border: '1px solid rgba(255,255,255,0.1)',
          borderRadius: 'var(--radius-sm)',
          overflow: 'hidden',
        }}>
          {([7, 30, 90] as const).map(d => (
            <button
              key={d}
              onClick={() => setDays(d)}
              style={{
                padding: '6px 14px',
                fontSize: 13,
                fontWeight: 500,
                border: 'none',
                cursor: 'pointer',
                background: days === d ? 'var(--accent)' : 'var(--bg-tertiary)',
                color: days === d ? '#0F1117' : 'var(--text-secondary)',
                transition: 'background 0.15s, color 0.15s',
              }}
            >
              {d}d
            </button>
          ))}
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Metric summary row — renders before charts load, shows — while fetching */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, marginBottom: 24 }}>
        <div className="metric-card">
          <div className="metric-label">Sent ({days}d)</div>
          <div className="metric-value">{loading ? '—' : summary?.sentCount ?? 0}</div>
          <div className="metric-sub">notifications</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Delivery Rate</div>
          <div className="metric-value">{loading ? '—' : `${summary?.deliveryRate ?? 0}%`}</div>
          <div className="metric-sub">of all deliveries</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Interaction Rate</div>
          <div className="metric-value">{loading ? '—' : `${summary?.interactionRate ?? 0}%`}</div>
          <div className="metric-sub">clicked / delivered</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Active Devices</div>
          <div className="metric-value" style={{
            color: summary?.activeDeviceCount ? 'var(--status-success)' : undefined,
          }}>
            {loading ? '—' : summary?.activeDeviceCount ?? 0}
          </div>
          <div className="metric-sub">pinged last 24h</div>
        </div>
      </div>

      {/* Notification volume — full-width line chart, 240px */}
      <div className="card" style={{ marginBottom: 24 }}>
        <h2 style={{ fontSize: 15, fontWeight: 600, marginBottom: 20 }}>Notification Volume</h2>
        <ResponsiveContainer width="100%" height={240}>
          <LineChart data={volume} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid
              strokeDasharray="3 3"
              stroke="rgba(255,255,255,0.06)"
              vertical={false}
            />
            <XAxis
              dataKey="date"
              tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
              axisLine={false}
              tickLine={false}
              interval={days <= 7 ? 0 : days <= 30 ? 4 : 14}
            />
            <YAxis
              allowDecimals={false}
              tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
              axisLine={false}
              tickLine={false}
              width={32}
            />
            <Tooltip content={<ChartTooltip />} />
            <Legend wrapperStyle={{ fontSize: 12, color: 'var(--text-secondary)', paddingTop: 16 }} />
            <Line
              type="monotone"
              dataKey="sent"
              name="Sent"
              stroke="#00C9A7"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
            <Line
              type="monotone"
              dataKey="delivered"
              name="Delivered"
              stroke="#60A5FA"
              strokeWidth={1.5}
              dot={false}
              strokeDasharray="4 2"
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>

      {/* Bottom row — two charts side by side */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
        {/* Delivery status breakdown — vertical bar, per-bar colors per spec */}
        <div className="card">
          <h2 style={{ fontSize: 15, fontWeight: 600, marginBottom: 20 }}>Delivery Status</h2>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart data={statusData} margin={{ top: 0, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid
                strokeDasharray="3 3"
                stroke="rgba(255,255,255,0.06)"
                vertical={false}
              />
              <XAxis
                dataKey="name"
                tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
                axisLine={false}
                tickLine={false}
              />
              <YAxis
                allowDecimals={false}
                tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
                axisLine={false}
                tickLine={false}
                width={32}
              />
              <Tooltip content={<ChartTooltip />} />
              <Bar dataKey="value" name="Count" radius={2} isAnimationActive={false}>
                {statusData.map((entry, i) => (
                  <Cell key={i} fill={STATUS_COLORS[entry.name] ?? '#7A7A92'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* Template usage — horizontal bar chart, #60A5FA per spec */}
        <div className="card">
          <h2 style={{ fontSize: 15, fontWeight: 600, marginBottom: 20 }}>Template Usage</h2>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart
              data={templateData}
              layout="vertical"
              margin={{ top: 0, right: 8, bottom: 0, left: 4 }}
            >
              <CartesianGrid
                strokeDasharray="3 3"
                stroke="rgba(255,255,255,0.06)"
                horizontal={false}
              />
              <XAxis
                type="number"
                allowDecimals={false}
                tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
                axisLine={false}
                tickLine={false}
              />
              <YAxis
                type="category"
                dataKey="name"
                tick={{ fontSize: 11, fill: 'var(--text-dim)' }}
                axisLine={false}
                tickLine={false}
                width={72}
              />
              <Tooltip content={<ChartTooltip />} />
              <Bar dataKey="value" name="Count" fill="#60A5FA" radius={2} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
}
