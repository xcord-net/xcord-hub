import { describe, it, expect, beforeEach } from 'vitest';
import DashboardLayout from './DashboardLayout';
import { useAuth } from '../stores/auth.store';
import { instanceStore } from '../stores/instance.store';
import { unreadStore } from '../stores/unread.store';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('DashboardLayout', () => {
  beforeEach(() => {
    useAuth().reset();
    instanceStore.reset();
    unreadStore.reset();
    localStorage.clear();
  });

  it('renders the loading state when no token is present', () => {
    const { getByText } = renderWithRouter(() => (
      <DashboardLayout><span>protected</span></DashboardLayout>
    ));
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders the AppShell with children when restoreSession resolves', async () => {
    mockFetch({
      'GET /api/v1/auth/me': () => ({
        status: 200,
        body: { userId: 1, username: 'u', displayName: 'User', email: 'u@example.com' },
      }),
    });
    localStorage.setItem('xcord_hub_token', 'tkn');
    const { findByText } = renderWithRouter(() => (
      <DashboardLayout><span>my-page</span></DashboardLayout>
    ));
    expect(await findByText('my-page')).toBeInTheDocument();
  });

  it('keeps showing loading while session restore is pending', () => {
    mockFetch({
      'GET /api/v1/auth/me': () => new Promise(() => {}) as Promise<unknown>,
    });
    localStorage.setItem('xcord_hub_token', 'tkn');
    const { getByText, queryByText } = renderWithRouter(() => (
      <DashboardLayout><span>my-page</span></DashboardLayout>
    ));
    expect(getByText('Loading...')).toBeInTheDocument();
    expect(queryByText('my-page')).toBeNull();
  });

  it('does not render protected children when restoreSession fails', async () => {
    mockFetch({
      'GET /api/v1/auth/me': () => ({ status: 401, body: { message: 'no' } }),
    });
    localStorage.setItem('xcord_hub_token', 'bad');
    const { queryByText } = renderWithRouter(() => (
      <DashboardLayout><span>my-page</span></DashboardLayout>
    ));
    // After the promise resolves, children should still not be visible
    await new Promise((r) => setTimeout(r, 10));
    expect(queryByText('my-page')).toBeNull();
  });
});
