import React, { useState, useEffect, useCallback } from 'react';
import { api } from './api';
import { Login } from './components/Login';
import { TenantSwitcher } from './components/TenantSwitcher';
import { InventoryTable } from './components/InventoryTable';
import { ItemModal } from './components/ItemModal';
import { UserSummary, TenantSummary, InventoryItem, PagedResult, LoginResponse } from './types';
import { Boxes, LogOut, AlertCircle, CheckCircle } from 'lucide-react';

export const App: React.FC = () => {
  const [user, setUser] = useState<UserSummary | null>(null);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(null);
  const [inventory, setInventory] = useState<PagedResult<InventoryItem>>({
    items: [],
    page: 1,
    pageSize: 10,
    totalCount: 0,
    totalPages: 0
  });

  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState('name');
  const [sortDescending, setSortDescending] = useState(false);
  const [page, setPage] = useState(1);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<InventoryItem | null>(null);

  const [banner, setBanner] = useState<{ type: 'danger' | 'success'; message: string } | null>(null);

  const showBanner = (type: 'danger' | 'success', message: string) => {
    setBanner({ type, message });
    setTimeout(() => setBanner(null), 5000);
  };

  const handleLogout = () => {
    api.setToken(null);
    api.setCurrentTenant(null);
    setUser(null);
    setTenants([]);
    setSelectedTenantId(null);
    setInventory({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
    setBanner(null);
  };

  useEffect(() => {
    api.onUnauthorized(handleLogout);
    api.onForbidden(message => {
      showBanner('danger', message);
      // Reset selected tenant
      setSelectedTenantId(null);
      api.setCurrentTenant(null);
      setInventory({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
    });
  }, []);

  const loadInventory = useCallback(async () => {
    if (!selectedTenantId) return;

    setLoading(true);
    try {
      const data = await api.getItems(page, 10, search, sortBy, sortDescending);
      setInventory(data);
    } catch (err: unknown) {
      if (err instanceof Error && err.message !== 'Unauthorized') {
        showBanner('danger', err.message);
      }
    } finally {
      setLoading(false);
    }
  }, [selectedTenantId, page, search, sortBy, sortDescending]);

  // Completely clear inventory state when switching tenants
  const handleSelectTenant = (newTenantId: string) => {
    setInventory({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
    setSearch('');
    setPage(1);
    setSelectedTenantId(newTenantId);
    api.setCurrentTenant(newTenantId);
  };

  useEffect(() => {
    if (selectedTenantId) {
      loadInventory();
    }
  }, [selectedTenantId, loadInventory]);

  const handleLoginSuccess = (data: LoginResponse) => {
    setUser(data.user);
    setTenants(data.tenants);

    if (data.tenants.length > 0) {
      const firstTenant = data.tenants[0];
      setSelectedTenantId(firstTenant.id);
      api.setCurrentTenant(firstTenant.id);
    }
  };

  const handleSaveItem = async (data: { name: string; sku: string; quantity: number; price: number; rowVersion?: string }) => {
    if (editingItem) {
      await api.updateItem(editingItem.id, {
        name: data.name,
        sku: data.sku,
        quantity: data.quantity,
        price: data.price,
        rowVersion: data.rowVersion!
      });
      showBanner('success', `Item '${data.name}' updated successfully.`);
    } else {
      await api.createItem(data);
      showBanner('success', `Item '${data.name}' created successfully.`);
    }
    await loadInventory();
  };

  const handleDeleteItem = async (id: string) => {
    if (!confirm('Are you sure you want to delete this inventory item?')) return;

    try {
      await api.deleteItem(id);
      showBanner('success', 'Item deleted successfully.');
      await loadInventory();
    } catch (err: unknown) {
      showBanner('danger', err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleSortChange = (field: string) => {
    if (sortBy === field) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(field);
      setSortDescending(false);
    }
  };

  if (!user) {
    return <Login onLoginSuccess={handleLoginSuccess} />;
  }

  const currentTenant = tenants.find(t => t.id === selectedTenantId);

  return (
    <div style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column' }}>
      {/* Header */}
      <header style={{
        background: 'rgba(18, 22, 34, 0.8)',
        backdropFilter: 'blur(16px)',
        borderBottom: '1px solid var(--border-color)',
        padding: '14px 24px',
        position: 'sticky',
        top: 0,
        zIndex: 100
      }}>
        <div style={{ maxWidth: '1200px', margin: '0 auto', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <div style={{
                background: 'linear-gradient(135deg, var(--accent-primary), var(--accent-hover))',
                padding: '6px',
                borderRadius: '8px',
                display: 'flex'
              }}>
                <Boxes size={20} color="#fff" />
              </div>
              <span style={{ fontSize: '1.2rem', fontWeight: 800, letterSpacing: '-0.02em' }}>
                TenNet
              </span>
            </div>

            {tenants.length > 0 && (
              <TenantSwitcher
                tenants={tenants}
                selectedTenantId={selectedTenantId}
                onSelectTenant={handleSelectTenant}
              />
            )}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            {currentTenant && (
              <span className="badge badge-tenant">
                Active: {currentTenant.name}
              </span>
            )}

            <span style={{ fontSize: '0.85rem', color: 'var(--text-muted)' }}>
              {user.email}
            </span>

            <button
              className="btn btn-secondary btn-sm"
              onClick={handleLogout}
              title="Sign Out"
            >
              <LogOut size={14} /> Sign Out
            </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main style={{ flex: 1, maxWidth: '1200px', width: '100%', margin: '0 auto', padding: '28px 24px' }}>
        {banner && (
          <div className={`banner banner-${banner.type}`}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              {banner.type === 'danger' ? <AlertCircle size={16} /> : <CheckCircle size={16} />}
              <span>{banner.message}</span>
            </div>
            <button
              onClick={() => setBanner(null)}
              style={{ background: 'transparent', color: 'inherit', fontWeight: 700 }}
            >
              ✕
            </button>
          </div>
        )}

        <div style={{ marginBottom: '24px', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end' }}>
          <div>
            <h1 style={{ fontSize: '1.5rem', fontWeight: 800, letterSpacing: '-0.02em', marginBottom: '4px' }}>
              {currentTenant ? `${currentTenant.name} Inventory` : 'Inventory Management'}
            </h1>
            <p style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>
              Isolated inventory items, pricing, quantities, and attachments for this tenant.
            </p>
          </div>
        </div>

        <InventoryTable
          data={inventory}
          loading={loading}
          search={search}
          onSearchChange={setSearch}
          sortBy={sortBy}
          sortDescending={sortDescending}
          onSortChange={handleSortChange}
          page={page}
          onPageChange={setPage}
          onAddItem={() => {
            setEditingItem(null);
            setModalOpen(true);
          }}
          onEditItem={item => {
            setEditingItem(item);
            setModalOpen(true);
          }}
          onDeleteItem={handleDeleteItem}
        />
      </main>

      <ItemModal
        item={editingItem}
        isOpen={modalOpen}
        onClose={() => setModalOpen(false)}
        onSave={handleSaveItem}
      />
    </div>
  );
};
