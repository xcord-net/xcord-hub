import { createSignal, createRoot } from 'solid-js';
import { api } from '../api/client';
import type { LoginRequest, AuthTokens } from '../types/auth';

const store = createRoot(() => {
  const [userId, setUserId] = createSignal<string | null>(null);
  const [username, setUsername] = createSignal<string | null>(null);
  const [isAuthenticated, setIsAuthenticated] = createSignal(false);
  const [isAdmin, setIsAdmin] = createSignal(false);
  const [isLoading, setIsLoading] = createSignal(true);

  return { userId, setUserId, username, setUsername, isAuthenticated, setIsAuthenticated, isAdmin, setIsAdmin, isLoading, setIsLoading };
});

function parseJwt(token: string): Record<string, unknown> | null {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch {
    return null;
  }
}

export function useAuth() {
  return {
    get userId() { return store.userId(); },
    get username() { return store.username(); },
    get isAuthenticated() { return store.isAuthenticated(); },
    get isAdmin() { return store.isAdmin(); },
    get isLoading() { return store.isLoading(); },

    async login(request: LoginRequest): Promise<void> {
      const response = await api.post<AuthTokens>('/api/v1/auth/login', request);

      const payload = parseJwt(response.accessToken);
      const adminClaim = payload?.admin === 'true' || payload?.admin === true;

      if (!adminClaim) {
        throw new Error('Access denied: Admin privileges required');
      }

      api.setToken(response.accessToken);
      localStorage.setItem('accessToken', response.accessToken);
      store.setUserId(response.userId);
      store.setUsername(response.username);
      store.setIsAdmin(true);
      store.setIsAuthenticated(true);
    },

    async logout(): Promise<void> {
      try {
        await api.post('/api/v1/auth/logout');
      } finally {
        api.setToken(null);
        localStorage.removeItem('accessToken');
        store.setUserId(null);
        store.setUsername(null);
        store.setIsAdmin(false);
        store.setIsAuthenticated(false);
      }
    },

    async validateAuth(): Promise<boolean> {
      store.setIsLoading(true);
      try {
        const token = localStorage.getItem('accessToken');
        if (!token) {
          store.setIsAuthenticated(false);
          store.setIsAdmin(false);
          return false;
        }

        const payload = parseJwt(token);
        const adminClaim = payload?.admin === 'true' || payload?.admin === true;

        if (!adminClaim) {
          api.setToken(null);
          localStorage.removeItem('accessToken');
          store.setIsAuthenticated(false);
          store.setIsAdmin(false);
          return false;
        }

        api.setToken(token);

        const response = await api.post<AuthTokens>('/api/v1/auth/refresh');
        const refreshPayload = parseJwt(response.accessToken);
        const refreshAdminClaim = refreshPayload?.admin === 'true' || refreshPayload?.admin === true;

        if (!refreshAdminClaim) {
          throw new Error('Admin privileges revoked');
        }

        api.setToken(response.accessToken);
        localStorage.setItem('accessToken', response.accessToken);
        store.setUserId(response.userId);
        store.setUsername(response.username);
        store.setIsAdmin(true);
        store.setIsAuthenticated(true);
        return true;
      } catch {
        api.setToken(null);
        localStorage.removeItem('accessToken');
        store.setUserId(null);
        store.setUsername(null);
        store.setIsAdmin(false);
        store.setIsAuthenticated(false);
        return false;
      } finally {
        store.setIsLoading(false);
      }
    },
  };
}
