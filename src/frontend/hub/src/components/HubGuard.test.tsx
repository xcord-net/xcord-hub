import { describe, it, expect, beforeEach } from 'vitest';
import { waitFor } from '@solidjs/testing-library';
import HubGuard from './HubGuard';
import { useAuth } from '../stores/auth.store';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('HubGuard', () => {
  beforeEach(() => {
    useAuth().reset();
    localStorage.clear();
  });

  it('shows loading spinner while session restore is pending', () => {
    mockFetch({
      'GET /api/v1/auth/me': () => new Promise(() => {}) as Promise<unknown>,
    });
    localStorage.setItem('xcord_hub_token', 'tkn');
    const { getByText } = renderWithRouter(() => (
      <HubGuard><span>protected</span></HubGuard>
    ));
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders children when restoreSession succeeds', async () => {
    mockFetch({
      'GET /api/v1/auth/me': () => ({ status: 200, body: { userId: 1, username: 'a', displayName: 'A', email: 'a@x' } }),
    });
    localStorage.setItem('xcord_hub_token', 'tkn');
    const { findByText } = renderWithRouter(() => (
      <HubGuard><span>protected</span></HubGuard>
    ));
    expect(await findByText('protected')).toBeInTheDocument();
  });

  it('shows loading state when no token is stored (then would navigate)', async () => {
    const { getByText } = renderWithRouter(() => (
      <HubGuard><span>protected</span></HubGuard>
    ));
    expect(getByText('Loading...')).toBeInTheDocument();
    await waitFor(() => {
      expect(useAuth().isAuthenticated).toBe(false);
    });
  });
});
