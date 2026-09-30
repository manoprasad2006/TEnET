import React, { useState } from 'react';
import { InventoryItem, PagedResult } from '../types';
import { FileSection } from './FileSection';
import { Search, ChevronDown, ChevronUp, Plus, Edit2, Trash2, ArrowUpDown, ChevronLeft, ChevronRight } from 'lucide-react';

interface InventoryTableProps {
  data: PagedResult<InventoryItem> | null;
  loading: boolean;
  search: string;
  onSearchChange: (search: string) => void;
  sortBy: string;
  sortDescending: boolean;
  onSortChange: (field: string) => void;
  page: number;
  onPageChange: (newPage: number) => void;
  onAddItem: () => void;
  onEditItem: (item: InventoryItem) => void;
  onDeleteItem: (id: string) => void;
}

export const InventoryTable: React.FC<InventoryTableProps> = ({
  data,
  loading,
  search,
  onSearchChange,
  sortBy,
  sortDescending,
  onSortChange,
  page,
  onPageChange,
  onAddItem,
  onEditItem,
  onDeleteItem
}) => {
  const [expandedItemId, setExpandedItemId] = useState<string | null>(null);

  const toggleExpand = (id: string) => {
    setExpandedItemId(prev => prev === id ? null : id);
  };

  const renderSortIndicator = (field: string) => {
    if (sortBy !== field) return <ArrowUpDown size={14} color="var(--text-dim)" />;
    return sortDescending ? <ChevronDown size={14} color="var(--accent-light)" /> : <ChevronUp size={14} color="var(--accent-light)" />;
  };

  return (
    <div className="glass-panel" style={{ padding: '24px' }}>
      {/* Controls Bar */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px', gap: '16px', flexWrap: 'wrap' }}>
        <div style={{ position: 'relative', flex: '1', minWidth: '240px', maxWidth: '400px' }}>
          <input
            type="text"
            placeholder="Search items by name or SKU..."
            value={search}
            onChange={e => onSearchChange(e.target.value)}
            style={{ width: '100%', paddingLeft: '38px' }}
          />
          <Search size={16} color="var(--text-dim)" style={{ position: 'absolute', left: '12px', top: '13px' }} />
        </div>

        <button className="btn btn-primary" onClick={onAddItem}>
          <Plus size={16} /> Add Item
        </button>
      </div>

      {/* Table */}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid var(--border-color)', textAlign: 'left', color: 'var(--text-muted)' }}>
              <th style={{ padding: '12px 16px', cursor: 'pointer' }} onClick={() => onSortChange('name')}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  Name {renderSortIndicator('name')}
                </div>
              </th>
              <th style={{ padding: '12px 16px', cursor: 'pointer' }} onClick={() => onSortChange('sku')}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  SKU {renderSortIndicator('sku')}
                </div>
              </th>
              <th style={{ padding: '12px 16px', cursor: 'pointer' }} onClick={() => onSortChange('quantity')}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  Quantity {renderSortIndicator('quantity')}
                </div>
              </th>
              <th style={{ padding: '12px 16px', cursor: 'pointer' }} onClick={() => onSortChange('price')}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  Price {renderSortIndicator('price')}
                </div>
              </th>
              <th style={{ padding: '12px 16px', textAlign: 'right' }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={5} style={{ padding: '36px', textAlign: 'center', color: 'var(--text-dim)' }}>
                  Loading inventory...
                </td>
              </tr>
            ) : !data || data.items.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ padding: '36px', textAlign: 'center', color: 'var(--text-dim)' }}>
                  No inventory items found. Click "Add Item" to create one.
                </td>
              </tr>
            ) : (
              data.items.map(item => {
                const isExpanded = expandedItemId === item.id;
                return (
                  <React.Fragment key={item.id}>
                    <tr
                      style={{
                        borderBottom: isExpanded ? 'none' : '1px solid var(--border-color)',
                        background: isExpanded ? 'rgba(255, 255, 255, 0.02)' : 'transparent',
                        transition: 'background 0.2s ease'
                      }}
                    >
                      <td style={{ padding: '14px 16px', fontWeight: 600 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <button
                            onClick={() => toggleExpand(item.id)}
                            style={{ background: 'transparent', color: 'var(--accent-light)', padding: '2px' }}
                            title="View files and details"
                          >
                            {isExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
                          </button>
                          <span>{item.name}</span>
                        </div>
                      </td>
                      <td style={{ padding: '14px 16px' }}>
                        <span style={{ fontFamily: 'monospace', color: 'var(--text-muted)' }}>
                          {item.sku}
                        </span>
                      </td>
                      <td style={{ padding: '14px 16px' }}>
                        <span className={`badge ${item.quantity > 10 ? 'badge-success' : 'badge-tenant'}`}>
                          {item.quantity} units
                        </span>
                      </td>
                      <td style={{ padding: '14px 16px', fontWeight: 600 }}>
                        ${item.price.toFixed(2)}
                      </td>
                      <td style={{ padding: '14px 16px', textAlign: 'right' }}>
                        <div style={{ display: 'inline-flex', gap: '8px' }}>
                          <button
                            className="btn btn-secondary btn-sm"
                            onClick={() => onEditItem(item)}
                            title="Edit Item"
                          >
                            <Edit2 size={14} />
                          </button>
                          <button
                            className="btn btn-danger btn-sm"
                            onClick={() => onDeleteItem(item.id)}
                            title="Delete Item"
                          >
                            <Trash2 size={14} />
                          </button>
                        </div>
                      </td>
                    </tr>

                    {isExpanded && (
                      <tr style={{ borderBottom: '1px solid var(--border-color)', background: 'rgba(255, 255, 255, 0.02)' }}>
                        <td colSpan={5} style={{ padding: '0 24px 20px 48px' }}>
                          <FileSection item={item} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {data && data.totalPages > 1 && (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '20px', paddingTop: '16px', borderTop: '1px solid var(--border-color)' }}>
          <span style={{ fontSize: '0.85rem', color: 'var(--text-muted)' }}>
            Showing page {data.page} of {data.totalPages} ({data.totalCount} items total)
          </span>

          <div style={{ display: 'flex', gap: '8px' }}>
            <button
              className="btn btn-secondary btn-sm"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
            >
              <ChevronLeft size={16} /> Prev
            </button>
            <button
              className="btn btn-secondary btn-sm"
              disabled={page >= data.totalPages}
              onClick={() => onPageChange(page + 1)}
            >
              Next <ChevronRight size={16} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
