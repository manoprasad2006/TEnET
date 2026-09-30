import { LoginResponse, MeResponse, InventoryItem, PagedResult, FileItem } from './types';

const API_BASE = import.meta.env.VITE_API_URL || 'http://127.0.0.1:5184';

class ApiClient {
  private token: string | null = null;
  private currentTenantId: string | null = null;
  private onUnauthorizedCallback: (() => void) | null = null;
  private onForbiddenCallback: ((message: string) => void) | null = null;

  public setToken(token: string | null) {
    this.token = token;
  }

  public setCurrentTenant(tenantId: string | null) {
    this.currentTenantId = tenantId;
  }

  public getTenantId(): string | null {
    return this.currentTenantId;
  }

  public onUnauthorized(callback: () => void) {
    this.onUnauthorizedCallback = callback;
  }

  public onForbidden(callback: (message: string) => void) {
    this.onForbiddenCallback = callback;
  }

  private async request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
    const headers = new Headers(options.headers || {});

    if (this.token) {
      headers.set('Authorization', `Bearer ${this.token}`);
    }

    if (this.currentTenantId && !endpoint.includes('/api/auth/')) {
      headers.set('X-Tenant-Id', this.currentTenantId);
    }

    const response = await fetch(`${API_BASE}${endpoint}`, {
      ...options,
      headers
    });

    if (response.status === 401) {
      this.token = null;
      if (this.onUnauthorizedCallback) this.onUnauthorizedCallback();
      throw new Error('Unauthorized');
    }

    if (response.status === 403) {
      let detail = 'Access denied for the requested tenant.';
      try {
        const problem = await response.json();
        detail = problem.detail || problem.title || detail;
      } catch {
        // ignore JSON parse
      }
      if (this.onForbiddenCallback) this.onForbiddenCallback(detail);
      throw new Error(detail);
    }

    if (!response.ok) {
      let errorMsg = `Error ${response.status}: ${response.statusText}`;
      try {
        const problem = await response.json();
        errorMsg = problem.detail || problem.title || errorMsg;
      } catch {
        // ignore
      }
      throw new Error(errorMsg);
    }

    if (response.status === 204) {
      return {} as T;
    }

    return response.json();
  }

  // Auth Endpoints
  public async login(email: string, password: string): Promise<LoginResponse> {
    const res = await this.request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    this.token = res.accessToken;
    return res;
  }

  public async getMe(): Promise<MeResponse> {
    return this.request<MeResponse>('/api/auth/me');
  }

  // Tenant whoami
  public async getTenantWhoAmI(): Promise<{ tenantId: string; tenantName: string; email: string }> {
    return this.request<{ tenantId: string; tenantName: string; email: string }>('/api/tenant/whoami');
  }

  // Items Endpoints
  public async getItems(
    page: number = 1,
    pageSize: number = 10,
    search?: string,
    sortBy?: string,
    sortDescending: boolean = false
  ): Promise<PagedResult<InventoryItem>> {
    const params = new URLSearchParams({
      page: page.toString(),
      pageSize: pageSize.toString(),
      sortDescending: sortDescending.toString()
    });
    if (search) params.set('search', search);
    if (sortBy) params.set('sortBy', sortBy);

    return this.request<PagedResult<InventoryItem>>(`/api/items?${params.toString()}`);
  }

  public async getItem(id: string): Promise<InventoryItem> {
    return this.request<InventoryItem>(`/api/items/${id}`);
  }

  public async createItem(data: { name: string; sku: string; quantity: number; price: number }): Promise<InventoryItem> {
    return this.request<InventoryItem>('/api/items', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
  }

  public async updateItem(id: string, data: { name: string; sku: string; quantity: number; price: number; rowVersion: string }): Promise<InventoryItem> {
    return this.request<InventoryItem>(`/api/items/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
  }

  public async deleteItem(id: string): Promise<void> {
    return this.request<void>(`/api/items/${id}`, {
      method: 'DELETE'
    });
  }

  // Files Endpoints
  public async getFiles(itemId: string): Promise<FileItem[]> {
    return this.request<FileItem[]>(`/api/items/${itemId}/files`);
  }

  public async uploadFile(itemId: string, file: File): Promise<FileItem> {
    const formData = new FormData();
    formData.append('file', file);

    const headers = new Headers();
    if (this.token) headers.set('Authorization', `Bearer ${this.token}`);
    if (this.currentTenantId) headers.set('X-Tenant-Id', this.currentTenantId);

    const response = await fetch(`${API_BASE}/api/items/${itemId}/files`, {
      method: 'POST',
      headers,
      body: formData
    });

    if (!response.ok) {
      const problem = await response.json();
      throw new Error(problem.detail || problem.title || 'Upload failed');
    }

    return response.json();
  }

  public async downloadFile(fileId: string, filename: string): Promise<void> {
    const headers = new Headers();
    if (this.token) headers.set('Authorization', `Bearer ${this.token}`);
    if (this.currentTenantId) headers.set('X-Tenant-Id', this.currentTenantId);

    const response = await fetch(`${API_BASE}/api/files/${fileId}/download`, { headers });
    if (!response.ok) {
      throw new Error('Download failed');
    }

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  }

  public async deleteFile(fileId: string): Promise<void> {
    return this.request<void>(`/api/files/${fileId}`, {
      method: 'DELETE'
    });
  }
}

export const api = new ApiClient();
