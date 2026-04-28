import { describe, it, expect } from 'vitest';
import Footer from './Footer';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';

describe('Footer', () => {
  it('renders without crashing', () => {
    const { container } = renderWithRouter(() => <Footer />);
    expect(container.querySelector('footer')).toBeInTheDocument();
  });

  it('renders the section headings', () => {
    const { getByText } = renderWithRouter(() => <Footer />);
    expect(getByText('Product')).toBeInTheDocument();
    expect(getByText('Community')).toBeInTheDocument();
    expect(getByText('Legal')).toBeInTheDocument();
  });

  it('renders the GitHub external link with target=_blank', () => {
    const { getByText } = renderWithRouter(() => <Footer />);
    const link = getByText('GitHub') as HTMLAnchorElement;
    expect(link.tagName).toBe('A');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
  });

  it('renders product links', () => {
    const { getByText } = renderWithRouter(() => <Footer />);
    expect(getByText('Pricing')).toBeInTheDocument();
    expect(getByText('Download')).toBeInTheDocument();
    expect(getByText('Get Started')).toBeInTheDocument();
  });

  it('renders the copyright with current year', () => {
    const { container } = renderWithRouter(() => <Footer />);
    const year = new Date().getFullYear().toString();
    expect(container.textContent).toContain(year);
    expect(container.textContent).toContain('xcord.net');
  });
});
