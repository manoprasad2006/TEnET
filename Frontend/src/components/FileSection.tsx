import React, { useState, useEffect } from 'react';
import { api } from '../api';
import { FileItem, InventoryItem } from '../types';
import { UploadCloud, Download, Trash2, FileText, Image as ImageIcon, AlertCircle } from 'lucide-react';

interface FileSectionProps {
  item: InventoryItem;
}

export const FileSection: React.FC<FileSectionProps> = ({ item }) => {
  const [files, setFiles] = useState<FileItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadFiles = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.getFiles(item.id);
      setFiles(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load files');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadFiles();
  }, [item.id]);

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = e.target.files?.[0];
    if (!selected) return;

    setUploading(true);
    setError(null);

    try {
      await api.uploadFile(item.id, selected);
      await loadFiles();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setUploading(false);
      e.target.value = ''; // reset input
    }
  };

  const handleDownload = async (file: FileItem) => {
    try {
      await api.downloadFile(file.id, file.originalName);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Download failed');
    }
  };

  const handleDelete = async (fileId: string) => {
    if (!confirm('Are you sure you want to delete this file?')) return;

    try {
      await api.deleteFile(fileId);
      setFiles(prev => prev.filter(f => f.id !== fileId));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div style={{ marginTop: '24px', paddingTop: '20px', borderTop: '1px solid var(--border-color)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
        <div>
          <h3 style={{ fontSize: '1rem', fontWeight: 700 }}>Item Documents & Assets</h3>
          <p style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>
            Max 10 files (PNG, JPG, PDF, CSV, TXT up to 10 MB)
          </p>
        </div>

        <label className="btn btn-secondary btn-sm" style={{ cursor: 'pointer' }}>
          <UploadCloud size={16} />
          {uploading ? 'Uploading...' : 'Upload File'}
          <input
            type="file"
            onChange={handleFileUpload}
            disabled={uploading}
            style={{ display: 'none' }}
            accept=".png,.jpg,.jpeg,.pdf,.csv,.txt"
          />
        </label>
      </div>

      {error && (
        <div className="banner banner-danger" style={{ marginBottom: '14px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        </div>
      )}

      {loading ? (
        <p style={{ color: 'var(--text-dim)', fontSize: '0.85rem' }}>Loading files...</p>
      ) : files.length === 0 ? (
        <p style={{ color: 'var(--text-dim)', fontSize: '0.85rem', fontStyle: 'italic' }}>
          No files uploaded for this item.
        </p>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
          {files.map(f => {
            const isImg = f.contentType.startsWith('image/');
            return (
              <div
                key={f.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '8px 12px',
                  background: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-sm)'
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                  {isImg ? <ImageIcon size={18} color="var(--accent-light)" /> : <FileText size={18} color="var(--accent-light)" />}
                  <div>
                    <span style={{ fontSize: '0.88rem', fontWeight: 600 }}>{f.originalName}</span>
                    <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)', marginLeft: '10px' }}>
                      {formatSize(f.size)}
                    </span>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '6px' }}>
                  <button
                    className="btn btn-secondary btn-sm"
                    onClick={() => handleDownload(f)}
                    title="Download"
                  >
                    <Download size={14} />
                  </button>
                  <button
                    className="btn btn-danger btn-sm"
                    onClick={() => handleDelete(f.id)}
                    title="Delete"
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
