/**
 * Hub frontend API client.
 *
 * Mirrors the shape of `xcord-fed/src/frontend/src/api/client.ts` and the
 * sibling admin client at `xcord-hub/src/frontend/admin/src/api/client.ts`,
 * with the auth model adjusted for the hub: a Bearer access token persisted in
 * `localStorage` under `xcord_hub_token`, and a transparent refresh flow that
 * retries the original request after a successful refresh.
 *
 * Centralizes:
 *  - base URL handling
 *  - Bearer auth header injection
 *  - CSRF defense header on state-changing methods
 *  - error normalization (JSON body thrown as the error)
 *  - 401 -> refresh -> retry -> redirect-to-login fallback
 */
class ApiClient {
  private baseUrl = '';
  private readonly tokenStorageKey = 'xcord_hub_token';
  private refreshPromise: Promise<boolean> | null = null;
  private onSessionExpired: () => void = () => { window.location.href = '/login'; };

  setBaseUrl(url: string) {
    this.baseUrl = url;
  }

  getBaseUrl(): string {
    return this.baseUrl;
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenStorageKey);
  }

  setToken(token: string | null) {
    if (token === null) {
      localStorage.removeItem(this.tokenStorageKey);
    } else {
      localStorage.setItem(this.tokenStorageKey, token);
    }
  }

  setOnSessionExpired(handler: () => void) {
    this.onSessionExpired = handler;
  }

  private buildHeaders(method: string): Record<string, string> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    };
    const token = this.getToken();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
    // CSRF defense: custom header browsers will not send on cross-origin
    // form submissions. Required by the backend for cookie-authenticated
    // state-changing requests (POST/PUT/PATCH/DELETE).
    if (method !== 'GET' && method !== 'HEAD') {
      headers['X-Xcord-Request'] = '1';
    }
    return headers;
  }

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers: this.buildHeaders(method),
      body: body !== undefined ? JSON.stringify(body) : undefined,
      credentials: 'include',
      cache: 'no-store',
    });

    if (response.status === 401 && this.getToken()) {
      const refreshed = await this.tryRefresh();
      if (refreshed) {
        const retryResponse = await fetch(`${this.baseUrl}${path}`, {
          method,
          headers: this.buildHeaders(method),
          body: body !== undefined ? JSON.stringify(body) : undefined,
          credentials: 'include',
        });

        if (retryResponse.status === 401) {
          this.setToken(null);
          this.onSessionExpired();
          throw new Error('Session expired');
        }

        if (!retryResponse.ok) {
          const error = await retryResponse.json().catch(() => ({ error: 'Something went wrong on our end. Try again.' }));
          throw error;
        }

        if (retryResponse.status === 204) return undefined as T;
        return retryResponse.json() as Promise<T>;
      }

      this.setToken(null);
      this.onSessionExpired();
      throw new Error('Session expired');
    }

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: 'Something went wrong on our end. Try again.' }));
      throw error;
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return response.json() as Promise<T>;
  }

  private async tryRefresh(): Promise<boolean> {
    if (this.refreshPromise) {
      return this.refreshPromise;
    }

    this.refreshPromise = (async () => {
      try {
        const response = await fetch(`${this.baseUrl}/api/v1/auth/refresh`, {
          method: 'POST',
          headers: { 'X-Xcord-Request': '1' },
          credentials: 'include',
        });

        if (!response.ok) return false;

        const data = await response.json().catch(() => null);
        if (data?.accessToken) {
          this.setToken(data.accessToken);
        }
        return true;
      } catch {
        return false;
      } finally {
        this.refreshPromise = null;
      }
    })();

    return this.refreshPromise;
  }

  async get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }

  async put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }

  async patch<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PATCH', path, body);
  }

  async delete<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path);
  }
}

export const api = new ApiClient();
