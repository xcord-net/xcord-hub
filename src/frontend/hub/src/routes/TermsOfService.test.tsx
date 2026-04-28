import { describe, it, expect, afterEach } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import TermsOfService from './TermsOfService';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/terms' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={TermsOfService} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('TermsOfService (route)', () => {
  afterEach(() => {
    document.getElementById('xcord-terms-jsonld')?.remove();
  });

  it('renders without crashing', () => {
    const { container } = renderPage();
    expect(container.querySelector('article')).toBeInTheDocument();
  });

  it('renders the main heading', () => {
    const { getByRole } = renderPage();
    expect(getByRole('heading', { level: 1, name: 'Terms of Service' })).toBeInTheDocument();
  });

  it('renders the last-updated subtitle', () => {
    const { getByText } = renderPage();
    expect(getByText(/Last updated:/)).toBeInTheDocument();
  });

  it('renders all numbered section headings', () => {
    const { getByText } = renderPage();
    expect(getByText('1. Acceptance of Terms')).toBeInTheDocument();
    expect(getByText('5. Acceptable Use')).toBeInTheDocument();
    expect(getByText('10. Changes to Terms')).toBeInTheDocument();
  });

  it('renders the back-to-registration link', () => {
    const { getByText } = renderPage();
    const link = getByText('Back to Registration') as HTMLAnchorElement;
    expect(link).toBeInTheDocument();
    expect(link.getAttribute('href')).toBe('/register');
  });

  it('injects JSON-LD structured data into the document head', () => {
    renderPage();
    const script = document.getElementById('xcord-terms-jsonld') as HTMLScriptElement | null;
    expect(script).not.toBeNull();
    expect(script?.type).toBe('application/ld+json');
    const parsed = JSON.parse(script!.textContent ?? '{}');
    expect(parsed.name).toBe('Terms of Service - Xcord');
  });
});
