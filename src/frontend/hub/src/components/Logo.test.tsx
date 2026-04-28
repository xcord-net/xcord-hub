import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import Logo from './Logo';

describe('Logo', () => {
  it('renders without crashing', () => {
    const { container } = render(() => <Logo />);
    expect(container.querySelector('span')).toBeInTheDocument();
  });

  it('renders the SVG mark', () => {
    const { container } = render(() => <Logo />);
    const svg = container.querySelector('svg');
    expect(svg).toBeInTheDocument();
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders the "ord" wordmark text', () => {
    const { container } = render(() => <Logo />);
    expect(container.textContent).toContain('ord');
  });

  it('renders the beta badge', () => {
    const { getByText } = render(() => <Logo />);
    expect(getByText('beta')).toBeInTheDocument();
  });

  it('applies custom class when provided', () => {
    const { container } = render(() => <Logo class="custom-class" />);
    expect(container.firstElementChild?.className).toContain('custom-class');
  });

  it('renders six brand path segments in the SVG', () => {
    const { container } = render(() => <Logo />);
    const paths = container.querySelectorAll('svg path');
    expect(paths).toHaveLength(6);
  });
});
