import React from 'react';
import { TenantSummary } from '../types';
import { Building2 } from 'lucide-react';

interface TenantSwitcherProps {
  tenants: TenantSummary[];
  selectedTenantId: string | null;
  onSelectTenant: (tenantId: string) => void;
}

export const TenantSwitcher: React.FC<TenantSwitcherProps> = ({
  tenants,
  selectedTenantId,
  onSelectTenant
}) => {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
      <Building2 size={18} color="var(--accent-light)" />
      <span style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--text-muted)' }}>
        Tenant:
      </span>
      <select
        value={selectedTenantId || ''}
        onChange={e => onSelectTenant(e.target.value)}
        style={{
          padding: '6px 12px',
          fontWeight: 700,
          background: 'rgba(99, 102, 241, 0.1)',
          borderColor: 'rgba(99, 102, 241, 0.3)',
          color: 'var(--accent-light)',
          cursor: 'pointer'
        }}
      >
        {tenants.map(t => (
          <option key={t.id} value={t.id} style={{ background: '#121622', color: '#fff' }}>
            {t.name}
          </option>
        ))}
      </select>
    </div>
  );
};
