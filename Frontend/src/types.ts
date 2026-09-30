export interface UserSummary {
  id: string;
  email: string;
  userName: string;
}

export interface TenantSummary {
  id: string;
  name: string;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  user: UserSummary;
  tenants: TenantSummary[];
}

export interface MeResponse {
  user: UserSummary;
  tenants: TenantSummary[];
}

export interface InventoryItem {
  id: string;
  name: string;
  sku: string;
  quantity: number;
  price: number;
  createdAt: string;
  updatedAt: string;
  rowVersion: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface FileItem {
  id: string;
  itemId: string;
  originalName: string;
  contentType: string;
  size: number;
  createdAt: string;
}
