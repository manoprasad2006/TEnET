import React, { useState, useEffect } from 'react';
import { InventoryItem } from '../types';
import { X, Save } from 'lucide-react';

interface ItemModalProps {
  item: InventoryItem | null; // null for Create, item for Edit
  isOpen: boolean;
  onClose: () => void;
  onSave: (data: { name: string; sku: string; quantity: number; price: number; rowVersion?: string }) => Promise<void>;
}

export const ItemModal: React.FC<ItemModalProps> = ({ item, isOpen, onClose, onSave }) => {
  const [name, setName] = useState('');
  const [sku, setSku] = useState('');
  const [quantity, setQuantity] = useState(0);
  const [price, setPrice] = useState('0.00');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (item) {
      setName(item.name);
      setSku(item.sku);
      setQuantity(item.quantity);
      setPrice(item.price.toFixed(2));
    } else {
      setName('');
      setSku('');
      setQuantity(0);
      setPrice('0.00');
    }
    setError(null);
  }, [item, isOpen]);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    // Frontend validation matching backend
    if (name.length < 1 || name.length > 120) {
      setError('Name must be between 1 and 120 characters.');
      return;
    }

    const skuRegex = /^[A-Za-z0-9\-_]{1,40}$/;
    if (!skuRegex.test(sku)) {
      setError('SKU must be 1-40 alphanumeric characters, hyphens or underscores.');
      return;
    }

    const numQty = parseInt(quantity.toString(), 10);
    if (isNaN(numQty) || numQty < 0 || numQty > 1000000) {
      setError('Quantity must be between 0 and 1,000,000.');
      return;
    }

    const numPrice = parseFloat(price);
    if (isNaN(numPrice) || numPrice < 0) {
      setError('Price must be a non-negative number.');
      return;
    }

    setLoading(true);
    try {
      await onSave({
        name,
        sku,
        quantity: numQty,
        price: numPrice,
        rowVersion: item?.rowVersion
      });
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to save item');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{
      position: 'fixed',
      top: 0,
      left: 0,
      right: 0,
      bottom: 0,
      background: 'rgba(0, 0, 0, 0.75)',
      backdropFilter: 'blur(8px)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      padding: '20px',
      zIndex: 1000
    }}>
      <div className="glass-panel" style={{ width: '100%', maxWidth: '480px', padding: '28px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
          <h2 style={{ fontSize: '1.25rem', fontWeight: 700 }}>
            {item ? 'Edit Inventory Item' : 'New Inventory Item'}
          </h2>
          <button onClick={onClose} style={{ background: 'transparent', color: 'var(--text-dim)' }}>
            <X size={20} />
          </button>
        </div>

        {error && (
          <div className="banner banner-danger" style={{ marginBottom: '16px' }}>
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, marginBottom: '6px', color: 'var(--text-muted)' }}>
              Item Name *
            </label>
            <input
              type="text"
              required
              maxLength={120}
              style={{ width: '100%' }}
              value={name}
              onChange={e => setName(e.target.value)}
              placeholder="e.g. Cordless Drill Pro"
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, marginBottom: '6px', color: 'var(--text-muted)' }}>
              SKU (Stock Keeping Unit) *
            </label>
            <input
              type="text"
              required
              maxLength={40}
              style={{ width: '100%' }}
              value={sku}
              onChange={e => setSku(e.target.value)}
              placeholder="e.g. ACM-DRILL-001"
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, marginBottom: '6px', color: 'var(--text-muted)' }}>
                Quantity *
              </label>
              <input
                type="number"
                min="0"
                max="1000000"
                required
                style={{ width: '100%' }}
                value={quantity}
                onChange={e => setQuantity(parseInt(e.target.value || '0', 10))}
              />
            </div>
            <div>
              <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, marginBottom: '6px', color: 'var(--text-muted)' }}>
                Unit Price ($) *
              </label>
              <input
                type="number"
                step="0.01"
                min="0"
                required
                style={{ width: '100%' }}
                value={price}
                onChange={e => setPrice(e.target.value)}
              />
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '12px' }}>
            <button type="button" className="btn btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={loading}>
              <Save size={16} />
              {loading ? 'Saving...' : 'Save Item'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
