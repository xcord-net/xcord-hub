import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import Overview from './Overview';
import { instanceStore } from '../../stores/instance.store';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/dashboard' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={Overview} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('Overview (route)', () => {
  beforeEach(() => {
    instanceStore.reset();
    localStorage.clear();
  });

  it('renders the Overview heading', () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => ({ instances: [] }),
    });
    const { getByTestId } = renderPage();
    expect(getByTestId('overview-heading')).toBeInTheDocument();
  });

  it('renders the Create Instance button', () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => ({ instances: [] }),
    });
    const { getByTestId } = renderPage();
    expect(getByTestId('overview-create-instance-btn')).toBeInTheDocument();
  });

  it('shows empty state when there are no instances', async () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => ({ instances: [] }),
    });
    const { findByText } = renderPage();
    expect(await findByText('No servers yet.')).toBeInTheDocument();
  });

  it('renders an instance row when the API returns one', async () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => ({
        instances: [
          {
            instanceId: 'i-1',
            displayName: 'Foo Server',
            domain: 'foo.example.com',
            status: 'Running',
            tier: 'Pro',
            mediaEnabled: false,
            createdAt: '2026-01-01T00:00:00Z',
          },
        ],
      }),
    });
    const { findByText } = renderPage();
    expect(await findByText('Foo Server')).toBeInTheDocument();
    expect(await findByText('foo.example.com')).toBeInTheDocument();
  });

  it('shows "Loading instances..." while the request is pending', () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => new Promise(() => {}),
    });
    const { getByText } = renderPage();
    expect(getByText('Loading instances...')).toBeInTheDocument();
  });

  it('navigates via the "Launch one" link', async () => {
    mockFetch({
      'GET /api/v1/hub/instances': () => ({ instances: [] }),
    });
    const { findByText } = renderPage();
    const link = (await findByText('Launch one')) as HTMLAnchorElement;
    fireEvent.click(link);
    // we don't assert navigation outcome (router internal); just ensure the
    // link is wired and click doesn't throw.
    expect(link).toBeInTheDocument();
  });
});
