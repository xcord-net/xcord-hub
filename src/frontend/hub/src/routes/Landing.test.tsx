import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import Landing from './Landing';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={Landing} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('Landing (route)', () => {
  it('renders the hero headline', () => {
    const { getByText } = renderPage();
    expect(getByText('Your corner of')).toBeInTheDocument();
    expect(getByText('the internet.')).toBeInTheDocument();
  });

  it('renders both hero CTA buttons', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('hero-cta-get-started')).toBeInTheDocument();
    expect(getByTestId('hero-cta-pricing')).toBeInTheDocument();
  });

  it('renders the "Why Xcord?" features section', () => {
    const { getByText } = renderPage();
    expect(getByText('Why Xcord?')).toBeInTheDocument();
    expect(getByText('Everything built in')).toBeInTheDocument();
  });

  it('renders the final CTA at the bottom', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('final-cta-get-started')).toBeInTheDocument();
  });

  it('renders the three step-by-step "How It Works" entries', () => {
    const { getByText } = renderPage();
    expect(getByText('Pick a name')).toBeInTheDocument();
    expect(getByText('Make it yours')).toBeInTheDocument();
    expect(getByText('Bring your people')).toBeInTheDocument();
  });

  it('injects a JSON-LD organization script into <head> on mount', () => {
    renderPage();
    const script = document.getElementById('xcord-jsonld');
    expect(script).not.toBeNull();
    expect(script?.getAttribute('type')).toBe('application/ld+json');
  });
});
