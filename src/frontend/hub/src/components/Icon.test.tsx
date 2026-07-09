import { describe, expect, it } from 'vitest';
import { render } from '@solidjs/testing-library';
import { House } from 'lucide-solid';
import { Icon } from './Icon';

describe('Icon', () => {
  it('renders a 16px icon with the 1.5px Xcord stroke by default', () => {
    const { container } = render(() => <Icon icon={House} />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg!.getAttribute('stroke-width')).toBe('1.5');
    expect(svg!.getAttribute('width')).toBe('16');
    expect(svg!.getAttribute('aria-hidden')).toBe('true');
  });

  it('is exposed to assistive tech only when a label is provided', () => {
    const { container } = render(() => <Icon icon={House} label="Home" />);
    const svg = container.querySelector('svg');
    expect(svg!.getAttribute('aria-label')).toBe('Home');
    expect(svg!.getAttribute('role')).toBe('img');
    expect(svg!.getAttribute('aria-hidden')).toBeNull();
  });
});
