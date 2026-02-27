import { createSignal } from 'solid-js';
import type { components } from '@generated/api-types';

export type HubUser = components['schemas']['GetMeResponse'];

const [user, setUser] = createSignal<HubUser | null>(null);
const [token, setToken] = createSignal<string | null>(null);
const [isLoading, setIsLoading] = createSignal(true);
const [error, setError] = createSignal<string | null>(null);

async function login(email: string, password: string): Promise<'success' | '2fa_required' | false> {
  setError(null);
  try {
    const response = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      if (data?.title === '2FA_REQUIRED') {
        return '2fa_required';
      }
      setError(data?.detail || data?.message || 'Invalid email or password');
      return false;
    }

    const data = await response.json();
    setToken(data.accessToken);
    setUser({ userId: String(data.userId), username: data.username, displayName: data.displayName, email: data.email });
    localStorage.setItem('xcord_hub_token', data.accessToken);
    return 'success';
  } catch {
    setError('Network error. Please try again.');
    return false;
  }
}

async function loginWith2FA(email: string, password: string, code: string): Promise<boolean> {
  setError(null);
  try {
    const response = await fetch('/api/v1/auth/2fa/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, code }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.detail || data?.message || 'Invalid verification code');
      return false;
    }

    const data = await response.json();
    setToken(data.accessToken);
    setUser({ userId: String(data.userId), username: data.username, displayName: data.displayName, email: data.email });
    localStorage.setItem('xcord_hub_token', data.accessToken);
    return true;
  } catch {
    setError('Network error. Please try again.');
    return false;
  }
}

async function signup(email: string, password: string, displayName: string, username: string, captchaId?: string, captchaAnswer?: string): Promise<boolean> {
  setError(null);
  try {
    const response = await fetch('/api/v1/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, displayName, username, captchaId, captchaAnswer }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.detail || data?.message || 'Failed to create account');
      return false;
    }

    const data = await response.json();
    setToken(data.accessToken);
    setUser({ userId: String(data.userId), username: data.username, displayName: data.displayName, email: data.email });
    localStorage.setItem('xcord_hub_token', data.accessToken);
    return true;
  } catch {
    setError('Network error. Please try again.');
    return false;
  }
}

function logout() {
  setUser(null);
  setToken(null);
  setError(null);
  localStorage.removeItem('xcord_hub_token');
}

async function restoreSession(): Promise<boolean> {
  setIsLoading(true);
  const savedToken = localStorage.getItem('xcord_hub_token');
  if (!savedToken) {
    setIsLoading(false);
    return false;
  }

  try {
    const response = await fetch('/api/v1/auth/me', {
      headers: { Authorization: `Bearer ${savedToken}` },
    });

    if (!response.ok) {
      localStorage.removeItem('xcord_hub_token');
      setIsLoading(false);
      return false;
    }

    const data = await response.json();
    setToken(savedToken);
    setUser({ userId: String(data.userId), username: data.username, displayName: data.displayName, email: data.email });
    setIsLoading(false);
    return true;
  } catch {
    localStorage.removeItem('xcord_hub_token');
    setIsLoading(false);
    return false;
  }
}

async function changePassword(currentPassword: string, newPassword: string): Promise<boolean> {
  setError(null);
  try {
    const response = await fetch('/api/v1/auth/change-password', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token()}`,
      },
      body: JSON.stringify({ currentPassword, newPassword }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.detail || data?.message || 'Failed to change password');
      return false;
    }

    return true;
  } catch {
    setError('Network error. Please try again.');
    return false;
  }
}

async function deleteAccount(password: string): Promise<boolean> {
  setError(null);
  try {
    const response = await fetch('/api/v1/users/@me', {
      method: 'DELETE',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token()}`,
      },
      body: JSON.stringify({ password }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.detail || data?.message || 'Failed to delete account');
      return false;
    }

    // Clear local state after successful deletion
    setUser(null);
    setToken(null);
    setError(null);
    localStorage.removeItem('xcord_hub_token');
    return true;
  } catch {
    setError('Network error. Please try again.');
    return false;
  }
}

export function useAuth() {
  return {
    get user() { return user(); },
    get isAuthenticated() { return user() !== null; },
    get isLoading() { return isLoading(); },
    get error() { return error(); },
    login,
    loginWith2FA,
    signup,
    logout,
    restoreSession,
    changePassword,
    deleteAccount,
    clearError: () => setError(null),
  };
}

// Module-level export used by consumers that import authStore directly
export const authStore = {
  user,
  token,
  isAuthenticated: () => user() !== null,
  isLoading,
  error,
  login,
  loginWith2FA,
  signup,
  logout,
  restoreSession,
  changePassword,
};
