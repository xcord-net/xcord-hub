import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import Logo from './Logo';

describe('Logo', () => {
  it('renders the brand text', () => {
    const { container } = render(() => <Logo />);
    expect(container.textContent).toContain('ord');
    expect(container.textContent).toContain('beta');
  });

  it('renders an SVG mark', () => {
    const { container } = render(() => <Logo />);
    expect(container.querySelector('svg')).not.toBeNull();
  });

  it('applies extra class when provided', () => {
    const { container } = render(() => <Logo class="text-red-500" />);
    expect(container.querySelector('span')?.className).toContain('text-red-500');
  });
});
