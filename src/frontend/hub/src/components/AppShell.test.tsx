import { describe, it, expect, beforeEach } from 'vitest';
import { fireEvent } from '@solidjs/testing-library';
import AppShell from './AppShell';
import { useAuth } from '../stores/auth.store';
import { instanceStore } from '../stores/instance.store';
import { unreadStore } from '../stores/unread.store';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';

describe('AppShell', () => {
  beforeEach(() => {
    useAuth().reset();
    instanceStore.reset();
    unreadStore.reset();
    localStorage.clear();
  });

  it('renders without crashing', () => {
    const { container } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    expect(container.querySelector('header')).toBeInTheDocument();
  });

  it('renders children inside the dashboard main slot', () => {
    const { getByText } = renderWithRouter(
      () => <AppShell><span>my-content</span></AppShell>,
      { path: '/dashboard' },
    );
    expect(getByText('my-content')).toBeInTheDocument();
  });

  it('shows the hub tab as active when no instance is selected', () => {
    const { container } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    // The hub tab is the first <button> after the mobile menu button containing the Logo svg.
    const buttons = Array.from(container.querySelectorAll('header button'));
    const hubTab = buttons.find((b) => b.querySelector('svg[viewBox="41 45 430 422"]'));
    expect(hubTab?.className).toContain('bg-xcord-bg-accent');
  });

  it('renders connected instance tabs in the header', () => {
    instanceStore.addInstance({ url: 'https://a.example.com', name: 'AlphaServer' });
    const { getByText } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    expect(getByText('AlphaServer')).toBeInTheDocument();
  });

  it('switches selected instance when an instance tab is clicked', () => {
    instanceStore.addInstance({ url: 'https://a.example.com', name: 'AlphaServer' });
    instanceStore.selectInstance(null);
    const { getByText } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    fireEvent.click(getByText('AlphaServer'));
    expect(instanceStore.selectedInstanceUrl()).toBe('https://a.example.com');
  });

  it('shows an unread badge when an unselected instance has unread messages', () => {
    instanceStore.addInstance({ url: 'https://a.example.com', name: 'AlphaServer' });
    instanceStore.selectInstance(null);
    unreadStore.setUnreadCount('https://a.example.com', 5);
    const { getByText } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    expect(getByText('5')).toBeInTheDocument();
  });

  it('opens the AddServerPopover when the + button is clicked', () => {
    const { getByText, getByPlaceholderText } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    fireEvent.click(getByText('+'));
    expect(getByPlaceholderText('Enter server address or search...')).toBeInTheDocument();
  });

  it('shows "?" in the avatar fallback when no user is logged in', () => {
    const { getByTitle } = renderWithRouter(
      () => <AppShell><span>page</span></AppShell>,
      { path: '/dashboard' },
    );
    expect(getByTitle('Account').textContent?.trim()).toBe('?');
  });
});
