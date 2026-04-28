import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { Layout } from './Layout';
import { useAuth } from '../stores/auth.store';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('Layout', () => {
  beforeEach(() => {
    useAuth().reset();
    Object.defineProperty(window, 'location', {
      value: { ...window.location, href: '' },
      writable: true,
    });
  });

  it('renders the Hub Admin heading', () => {
    const { getByText } = render(() => (
      <Layout currentPage="instances" onNavigate={() => {}}>
        <div>child</div>
      </Layout>
    ));
    expect(getByText(/Hub Admin/)).toBeInTheDocument();
  });

  it('renders all navigation buttons', () => {
    const { getByText } = render(() => (
      <Layout currentPage="instances" onNavigate={() => {}}>
        <div>x</div>
      </Layout>
    ));
    expect(getByText('Instances')).toBeInTheDocument();
    expect(getByText('Mailing List')).toBeInTheDocument();
    expect(getByText('Settings')).toBeInTheDocument();
  });

  it('renders provided children in main', () => {
    const { getByText } = render(() => (
      <Layout currentPage="instances" onNavigate={() => {}}>
        <div>hello-child</div>
      </Layout>
    ));
    expect(getByText('hello-child')).toBeInTheDocument();
  });

  it('calls onNavigate with the page name when nav button clicked', () => {
    const onNav = vi.fn();
    const { getByText } = render(() => (
      <Layout currentPage="instances" onNavigate={onNav}>
        <div />
      </Layout>
    ));
    fireEvent.click(getByText('Mailing List'));
    expect(onNav).toHaveBeenCalledWith('mailing-list');
  });

  it('calls auth.logout when Logout clicked', async () => {
    mockFetch({ 'POST /api/v1/auth/logout': () => ({ status: 200, body: {} }) });
    const { getByText } = render(() => (
      <Layout currentPage="instances" onNavigate={() => {}}>
        <div />
      </Layout>
    ));
    fireEvent.click(getByText('Logout'));
    await waitFor(() => expect(window.location.href).toBe('/login'));
  });
});
