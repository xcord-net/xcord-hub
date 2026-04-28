import { describe, it, expect, beforeEach, vi } from 'vitest';
import { fireEvent } from '@solidjs/testing-library';
import DashboardSidebar, { setSidebarOpen } from './DashboardSidebar';
import { useAuth } from '../stores/auth.store';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';

describe('DashboardSidebar', () => {
  beforeEach(() => {
    useAuth().reset();
    setSidebarOpen(false);
    localStorage.clear();
  });

  it('renders without crashing', () => {
    const { container } = renderWithRouter(() => <DashboardSidebar />);
    expect(container.querySelector('aside')).toBeInTheDocument();
  });

  it('renders all primary navigation items', () => {
    const { getByTestId } = renderWithRouter(() => <DashboardSidebar />);
    expect(getByTestId('sidebar-nav-overview')).toBeInTheDocument();
    expect(getByTestId('sidebar-nav-create')).toBeInTheDocument();
    expect(getByTestId('sidebar-nav-billing')).toBeInTheDocument();
    expect(getByTestId('sidebar-nav-account')).toBeInTheDocument();
  });

  it('renders the logout button', () => {
    const { getByTestId } = renderWithRouter(() => <DashboardSidebar />);
    expect(getByTestId('hub-logout-button')).toBeInTheDocument();
  });

  it('clears auth state when logout clicked', () => {
    // Seed auth then assert clicking logout zeros it. useAuth() returns a fresh
    // object literal each call, so spying on a method instance is unreliable;
    // observe the side effect on the underlying store instead.
    const originalLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, href: '' },
    });
    localStorage.setItem('xcord_hub_token', 'tkn');
    const { getByTestId } = renderWithRouter(() => <DashboardSidebar />);
    fireEvent.click(getByTestId('hub-logout-button'));
    expect(localStorage.getItem('xcord_hub_token')).toBeNull();
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
  });

  it('marks the overview link as active on /dashboard', () => {
    const { getByTestId } = renderWithRouter(() => <DashboardSidebar />, { path: '/dashboard' });
    const overview = getByTestId('sidebar-nav-overview');
    expect(overview.className).toContain('bg-xcord-bg-accent');
  });

  it('marks the billing link as active on /dashboard/billing', () => {
    const { getByTestId } = renderWithRouter(() => <DashboardSidebar />, { path: '/dashboard/billing' });
    const billing = getByTestId('sidebar-nav-billing');
    expect(billing.className).toContain('bg-xcord-bg-accent');
  });
});
