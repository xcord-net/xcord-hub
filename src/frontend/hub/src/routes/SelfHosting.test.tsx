import { describe, it, expect, afterEach } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import SelfHosting from './SelfHosting';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/docs/self-hosting' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={SelfHosting} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('SelfHosting (route)', () => {
  afterEach(() => {
    document.getElementById('xcord-selfhosting-jsonld')?.remove();
  });

  it('renders without crashing', () => {
    const { container } = renderPage();
    expect(container.querySelector('article')).toBeInTheDocument();
  });

  it('renders the main heading', () => {
    const { getByRole } = renderPage();
    expect(getByRole('heading', { level: 1, name: 'Self-Hosting Guide' })).toBeInTheDocument();
  });

  it('renders the prerequisites section and step headings', () => {
    const { getByText } = renderPage();
    expect(getByText('Prerequisites')).toBeInTheDocument();
    expect(getByText('1. Clone the Repositories')).toBeInTheDocument();
    expect(getByText('2. Start the Topology Designer')).toBeInTheDocument();
    expect(getByText('5. Verify Your Deployment')).toBeInTheDocument();
  });

  it('renders the deploy wizard steps', () => {
    const { getByText } = renderPage();
    expect(getByText('Select Provider')).toBeInTheDocument();
    expect(getByText('Configure')).toBeInTheDocument();
    expect(getByText('Review')).toBeInTheDocument();
    expect(getByText('Execute')).toBeInTheDocument();
  });

  it('renders the back-to-pricing link', () => {
    const { getByText } = renderPage();
    const link = getByText('Back to Pricing') as HTMLAnchorElement;
    expect(link).toBeInTheDocument();
    expect(link.getAttribute('href')).toBe('/pricing');
  });

  it('injects TechArticle JSON-LD structured data into the document head', () => {
    renderPage();
    const script = document.getElementById('xcord-selfhosting-jsonld') as HTMLScriptElement | null;
    expect(script).not.toBeNull();
    expect(script?.type).toBe('application/ld+json');
    const parsed = JSON.parse(script!.textContent ?? '{}');
    expect(parsed['@type']).toBe('TechArticle');
    expect(parsed.headline).toBe('Self-Hosting Guide - Xcord');
  });
});
