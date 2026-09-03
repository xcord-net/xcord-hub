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
  it('leads with the ownership thesis', () => {
    const { getByTestId } = renderPage();
    const h1 = getByTestId('landing-hero-heading');
    expect(h1.textContent).toContain('Four tools in one');
    expect(h1.textContent).toContain('you own.');
  });

  it('renders both hero CTA buttons', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('hero-cta-get-started')).toBeInTheDocument();
    expect(getByTestId('hero-cta-pricing')).toBeInTheDocument();
  });

  it('names the four pillars by the product each one replaces', () => {
    const { getAllByTestId } = renderPage();
    const labels = getAllByTestId('landing-pillar-replaces').map((el) => el.textContent);
    expect(labels).toEqual([
      'INSTEAD OF DISCORD',
      'INSTEAD OF LOCALS',
      'INSTEAD OF PATREON',
      'INSTEAD OF STREAMYARD',
    ]);
  });

  it('states the problem the four pillars answer', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('landing-problem-heading')).toBeInTheDocument();
  });

  it('keeps the membership availability caveat visible', () => {
    const { getByTestId } = renderPage();
    // The caveat's wording is a compliance detail, so this asserts the text
    // itself rather than only that the node exists.
    expect(getByTestId('landing-membership-caveat').textContent).toContain('Pro tier or higher');
  });

  it('renders the final CTA at the bottom', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('final-cta-get-started')).toBeInTheDocument();
  });

  it('does not use the retired feature-card grid', () => {
    const { container } = renderPage();
    // The rebuilt page states pillars as typographic rows. Emoji tiles were the
    // template look the redesign removed; catching them here keeps them gone.
    expect(container.textContent).not.toContain('Why Xcord?');
    expect(container.textContent).not.toMatch(/[\u{1F300}-\u{1FAFF}]/u);
  });

  it('injects a JSON-LD organization script into <head> on mount', () => {
    renderPage();
    const script = document.getElementById('xcord-jsonld');
    expect(script).not.toBeNull();
    expect(script?.getAttribute('type')).toBe('application/ld+json');
  });
});
