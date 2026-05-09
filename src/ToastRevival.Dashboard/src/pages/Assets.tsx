import { useState, useEffect, useRef, useCallback, DragEvent, ChangeEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { assetsApi, getModerationStatus, type AssetRecord, type ModerationStatus } from '../api/assets';
import { ApiError } from '../api/client';

type AssetType = 'HeroImage' | 'Logo' | 'Icon';

const ASSET_TYPE_OPTIONS: { value: AssetType; label: string }[] = [
  { value: 'HeroImage', label: 'Hero Image' },
  { value: 'Logo',      label: 'Logo' },
  { value: 'Icon',      label: 'Icon' },
];

const TYPE_BADGE_LABEL: Record<AssetType, string> = {
  HeroImage: 'Hero',
  Logo: 'Logo',
  Icon: 'Icon',
};

const MOD_STATUS_COLOR: Record<ModerationStatus, string> = {
  Pass:    'var(--status-success)',
  Review:  'var(--status-warning)',
  Block:   'var(--status-error)',
  Unknown: 'var(--text-dim)',
};

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  } catch {
    return iso;
  }
}

export default function Assets() {
  const navigate = useNavigate();

  const [assets, setAssets]           = useState<AssetRecord[]>([]);
  const [loading, setLoading]         = useState(true);
  const [fetchError, setFetchError]   = useState('');

  const [dragOver, setDragOver]       = useState(false);
  const [uploadType, setUploadType]   = useState<AssetType>('HeroImage');
  const [uploading, setUploading]     = useState(false);
  const [uploadError, setUploadError] = useState('');

  const fileInputRef = useRef<HTMLInputElement>(null);

  const loadAssets = useCallback(async () => {
    setLoading(true);
    setFetchError('');
    try {
      const data = await assetsApi.list();
      setAssets(data);
    } catch (err) {
      setFetchError(err instanceof ApiError ? err.message : 'Failed to load assets.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadAssets(); }, [loadAssets]);

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    const file = files[0];
    if (!file.type.startsWith('image/')) {
      setUploadError('Only image files are accepted (.jpg, .jpeg, .png, .gif, .webp).');
      return;
    }
    setUploadError('');
    setUploading(true);
    try {
      const uploaded = await assetsApi.upload(file, file.name, uploadType);
      setAssets(prev => [uploaded, ...prev]);
    } catch (err) {
      setUploadError(err instanceof ApiError ? err.message : 'Upload failed.');
    } finally {
      setUploading(false);
    }
  };

  const onDragOver = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (!uploading) setDragOver(true);
  };

  const onDragLeave = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragOver(false);
  };

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragOver(false);
    if (!uploading) void handleFiles(e.dataTransfer.files);
  };

  const onFileChange = (e: ChangeEvent<HTMLInputElement>) => {
    void handleFiles(e.target.files);
    // reset so same file can be re-selected
    e.target.value = '';
  };

  const openFilePicker = () => {
    if (!uploading) fileInputRef.current?.click();
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Assets</h1>
          <p className="subtitle">Manage hero images, logos, and icons for your notifications</p>
        </div>
      </div>

      {fetchError && <div className="error-banner">{fetchError}</div>}

      {/* Upload drop zone */}
      <div className="card" style={{ marginBottom: 24 }}>
        {/* Asset type selector */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 16 }}>
          <span style={{ fontSize: 12, fontWeight: 500, color: 'var(--text-secondary)', marginRight: 4 }}>Upload as:</span>
          <div style={{
            display: 'flex',
            gap: 1,
            background: 'var(--bg-tertiary)',
            borderRadius: 4,
            overflow: 'hidden',
            border: '1px solid rgba(15,23,42,0.12)',
          }}>
            {ASSET_TYPE_OPTIONS.map(opt => (
              <button
                key={opt.value}
                onClick={() => setUploadType(opt.value)}
                style={{
                  padding: '8px 14px',
                  border: 'none',
                  background: uploadType === opt.value ? 'var(--bg-secondary)' : 'transparent',
                  color: uploadType === opt.value ? 'var(--text-primary)' : 'var(--text-dim)',
                  fontWeight: uploadType === opt.value ? 600 : 400,
                  fontSize: 13,
                  cursor: 'pointer',
                  transition: 'background 0.15s',
                }}
              >
                {opt.label}
              </button>
            ))}
          </div>
        </div>

        {/* Drop zone */}
        <div
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
          onClick={openFilePicker}
          style={{
            border: `2px dashed ${dragOver ? 'var(--accent)' : 'rgba(15,23,42,0.18)'}`,
            borderRadius: 'var(--radius-md)',
            padding: '32px 24px',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 12,
            background: dragOver ? 'rgba(0,201,167,0.04)' : 'transparent',
            cursor: uploading ? 'default' : 'pointer',
            transition: 'border-color 0.15s, background 0.15s',
            opacity: uploading ? 0.7 : 1,
          }}
        >
          {uploading ? (
            <span className="spinner" />
          ) : (
            <>
              <UploadIcon />
              <div style={{ textAlign: 'center' }}>
                <p style={{ fontSize: 14, color: 'var(--text-primary)', fontWeight: 500, margin: 0 }}>
                  Drop images here or click to browse
                </p>
                <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 4, marginBottom: 0 }}>
                  Accepts .jpg, .jpeg, .png, .gif, .webp
                </p>
              </div>
            </>
          )}
        </div>

        <input
          ref={fileInputRef}
          type="file"
          accept=".jpg,.jpeg,.png,.gif,.webp,image/*"
          style={{ display: 'none' }}
          onChange={onFileChange}
        />

        {uploadError && (
          <div className="error-banner" style={{ marginTop: 12 }}>{uploadError}</div>
        )}
      </div>

      {/* Asset grid */}
      {loading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
          <span className="spinner" />
        </div>
      ) : assets.length === 0 ? (
        <p style={{ fontSize: 14, color: 'var(--text-dim)', textAlign: 'center', padding: 48 }}>
          No assets yet. Upload an image to get started.
        </p>
      ) : (
        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))',
          gap: 16,
        }}>
          {assets.map(asset => (
            <AssetCard
              key={asset.id}
              asset={asset}
              onDeleted={() => setAssets(prev => prev.filter(a => a.id !== asset.id))}
              navigate={navigate}
            />
          ))}
        </div>
      )}
    </div>
  );
}

/* Asset card */

interface AssetCardProps {
  asset: AssetRecord;
  onDeleted: () => void;
  navigate: ReturnType<typeof useNavigate>;
}

function AssetCard({ asset, onDeleted, navigate }: AssetCardProps) {
  const [deleteArmed, setDeleteArmed] = useState(false);
  const [deleting, setDeleting]       = useState(false);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const modStatus = getModerationStatus(asset.moderationResultJson);

  const armDelete = () => {
    setDeleteArmed(true);
    // Focus the confirm button so onBlur disarms when clicking elsewhere
    setTimeout(() => confirmRef.current?.focus(), 0);
  };

  const disarm = () => setDeleteArmed(false);

  const confirmDelete = async () => {
    setDeleting(true);
    try {
      await assetsApi.delete(asset.id);
      onDeleted();
    } catch {
      setDeleting(false);
      setDeleteArmed(false);
    }
  };

  return (
    <div className="card" style={{ padding: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      {/* Image preview */}
      <div style={{ background: 'var(--bg-tertiary)', width: '100%', height: 120, overflow: 'hidden', flexShrink: 0 }}>
        <img
          src={asset.url}
          alt={asset.name}
          style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
          onError={e => { (e.target as HTMLImageElement).style.display = 'none'; }}
        />
      </div>

      {/* Card body */}
      <div style={{ padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: 8, flex: 1 }}>
        {/* Name + badges row */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, overflow: 'hidden' }}>
          <span style={{
            fontSize: 14,
            fontWeight: 600,
            color: 'var(--text-primary)',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            flex: 1,
            minWidth: 0,
          }}>
            {asset.name}
          </span>
          <TypeBadge type={asset.type} />
        </div>

        {/* Moderation status + date row */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{
            fontSize: 11,
            fontWeight: 600,
            color: MOD_STATUS_COLOR[modStatus],
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
          }}>
            {modStatus}
          </span>
          <span style={{ fontSize: 12, color: 'var(--text-dim)', marginLeft: 'auto' }}>
            {formatDate(asset.uploadedAt)}
          </span>
        </div>

        {/* Action buttons */}
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 4 }}>
          <button
            className="btn btn-ghost"
            style={{ fontSize: 12, padding: '5px 10px' }}
            onClick={() => navigate('/compose', { state: { heroImageUrl: asset.url } })}
          >
            Use as Hero
          </button>
          <button
            className="btn btn-ghost"
            style={{ fontSize: 12, padding: '5px 10px' }}
            onClick={() => navigate('/compose', { state: { logoUrl: asset.url } })}
          >
            Use as Logo
          </button>
          <div style={{ marginLeft: 'auto' }}>
            {deleteArmed ? (
              <button
                ref={confirmRef}
                className="btn btn-ghost"
                style={{ fontSize: 12, padding: '5px 10px', color: 'var(--status-error)' }}
                onClick={() => void confirmDelete()}
                onBlur={disarm}
                disabled={deleting}
              >
                {deleting ? <span className="spinner" /> : 'Confirm Delete'}
              </button>
            ) : (
              <button
                className="btn btn-ghost"
                style={{ fontSize: 12, padding: '5px 10px', color: 'var(--status-error)' }}
                onClick={armDelete}
              >
                Delete
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function TypeBadge({ type }: { type: AssetType }) {
  return (
    <span style={{
      fontSize: 10,
      fontWeight: 700,
      textTransform: 'uppercase',
      letterSpacing: '0.06em',
      padding: '2px 6px',
      borderRadius: 'var(--radius-sm)',
      background: 'rgba(0,201,167,0.12)',
      color: 'var(--accent)',
      flexShrink: 0,
    }}>
      {TYPE_BADGE_LABEL[type]}
    </span>
  );
}

function UploadIcon() {
  return (
    <svg width="32" height="32" viewBox="0 0 32 32" fill="none" style={{ color: 'var(--text-dim)' }}>
      <path d="M16 22V10M10 16l6-6 6 6" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M6 26h20" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}
