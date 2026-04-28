import { describe, it, expect, afterEach } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import PrivacyPolicy from './PrivacyPolicy';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/privacy' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={PrivacyPolicy} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('PrivacyPolicy (route)', () => {
  afterEach(() => {
    document.getElementById('xcord-privacy-jsonld')?.remove();
  });

  it('renders without crashing', () => {
    const { container } = renderPage();
    expect(container.querySelector('article')).toBeInTheDocument();
  });

  it('renders the main heading', () => {
    const { getByRole } = renderPage();
    expect(getByRole('heading', { level: 1, name: 'Privacy Policy' })).toBeInTheDocument();
  });

  it('renders key section headings', () => {
    const { getByText } = renderPage();
    expect(getByText('1. What We Collect')).toBeInTheDocument();
    expect(getByText('3. Encryption')).toBeInTheDocument();
    expect(getByText('6. Your Rights')).toBeInTheDocument();
  });

  it('renders the privacy contact email link', () => {
    const { container } = renderPage();
    const mailto = container.querySelector('a[href="mailto:privacy@xcord.net"]');
    expect(mailto).toBeInTheDocument();
  });

  it('renders the back-to-registration link', () => {
    const { getByText } = renderPage();
    const link = getByText('Back to Registration') as HTMLAnchorElement;
    expect(link).toBeInTheDocument();
    expect(link.getAttribute('href')).toBe('/register');
  });

  it('injects JSON-LD structured data into the document head', () => {
    renderPage();
    const script = document.getElementById('xcord-privacy-jsonld') as HTMLScriptElement | null;
    expect(script).not.toBeNull();
    expect(script?.type).toBe('application/ld+json');
    const parsed = JSON.parse(script!.textContent ?? '{}');
    expect(parsed.name).toBe('Privacy Policy - Xcord');
  });
});
