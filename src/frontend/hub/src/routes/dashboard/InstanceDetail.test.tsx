import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import InstanceDetail from './InstanceDetail';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage(id = 'i-1') {
  const history = createMemoryHistory();
  history.set({ value: `/dashboard/instances/${id}` });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="/dashboard/instances/:id" component={InstanceDetail} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

const sampleInstance = {
  id: 'i-1',
  subdomain: 'foo',
  displayName: 'Foo Server',
  domain: 'foo.example.com',
  status: 'running',
  tier: 'pro',
  memberCount: 7,
  storageUsedMb: 100,
  createdAt: '2026-01-01T00:00:00Z',
};

describe('InstanceDetail (route)', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('renders Loading state initially', () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => new Promise(() => {}),
    });
    const { getByText } = renderPage();
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders "Instance not found" when API returns 404', async () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => ({ status: 404, body: {} }),
    });
    const { findByText } = renderPage();
    expect(await findByText('Instance not found')).toBeInTheDocument();
  });

  it('renders instance display name and domain when loaded', async () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => sampleInstance,
    });
    const { findByText, findAllByText } = renderPage();
    expect(await findByText('Foo Server')).toBeInTheDocument();
    // domain appears in both the subtitle and the info grid
    const matches = await findAllByText('foo.example.com');
    expect(matches.length).toBeGreaterThan(0);
  });

  it('shows the Suspend button only when the instance is running', async () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => sampleInstance,
    });
    const { findByText } = renderPage();
    expect(await findByText('Suspend Instance')).toBeInTheDocument();
  });

  it('reveals the confirmation row after Delete Instance is clicked', async () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => sampleInstance,
    });
    const { findByText, getByText } = renderPage();
    const del = await findByText('Delete Instance');
    fireEvent.click(del);
    expect(getByText('Confirm Delete')).toBeInTheDocument();
    expect(getByText(/Are you sure/)).toBeInTheDocument();
  });

  it('hides the confirmation row when Cancel is clicked', async () => {
    mockFetch({
      'GET /api/v1/hub/instances/i-1': () => sampleInstance,
    });
    const { findByText, queryByText, getByText } = renderPage();
    fireEvent.click(await findByText('Delete Instance'));
    fireEvent.click(getByText('Cancel'));
    expect(queryByText('Confirm Delete')).toBeNull();
  });
});
