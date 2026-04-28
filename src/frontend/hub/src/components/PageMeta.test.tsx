import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import PageMeta from './PageMeta';

function renderWithMeta(ui: () => any) {
  return render(() => <MetaProvider>{ui()}</MetaProvider>);
}

describe('PageMeta', () => {
  it('renders without crashing', () => {
    expect(() =>
      renderWithMeta(() => <PageMeta title="T" description="D" path="/x" />)
    ).not.toThrow();
  });

  it('sets the document title', () => {
    renderWithMeta(() => <PageMeta title="My Title" description="D" path="/x" />);
    expect(document.title).toBe('My Title');
  });

  it('writes a meta description tag', () => {
    renderWithMeta(() => <PageMeta title="T" description="A nice description" path="/x" />);
    const desc = document.head.querySelector('meta[name="description"]');
    expect(desc?.getAttribute('content')).toBe('A nice description');
  });

  it('writes a canonical link with origin + path', () => {
    renderWithMeta(() => <PageMeta title="T" description="D" path="/about" />);
    const canonical = document.head.querySelector('link[rel="canonical"]');
    expect(canonical?.getAttribute('href')).toBe(`${window.location.origin}/about`);
  });

  it('emits the noindex robots meta only when requested', () => {
    renderWithMeta(() => <PageMeta title="T" description="D" path="/x" noindex={true} />);
    const robots = document.head.querySelector('meta[name="robots"]');
    expect(robots?.getAttribute('content')).toBe('noindex, nofollow');
  });

  it('uses the provided ogImage when given', () => {
    renderWithMeta(() => (
      <PageMeta title="T" description="D" path="/x" ogImage="/custom.png" />
    ));
    const ogImage = document.head.querySelector('meta[property="og:image"]');
    expect(ogImage?.getAttribute('content')).toBe(`${window.location.origin}/custom.png`);
  });
});
