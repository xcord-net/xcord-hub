import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import PasswordStrength from './PasswordStrength';

describe('PasswordStrength', () => {
  it('renders no label for empty password', () => {
    const { container } = render(() => <PasswordStrength password="" />);
    expect(container.querySelector('span')?.textContent).toBe('');
  });

  it('shows Weak for very short passwords', () => {
    const { container } = render(() => <PasswordStrength password="ab" />);
    expect(container.querySelector('span')?.textContent).toBe('Weak');
  });

  it('shows Fair for length+lowercase+digit only', () => {
    const { container } = render(() => <PasswordStrength password="abcd1234" />);
    expect(container.querySelector('span')?.textContent).toBe('Fair');
  });

  it('shows Good for length+upper+lower+digit', () => {
    const { container } = render(() => <PasswordStrength password="Abcd1234" />);
    expect(container.querySelector('span')?.textContent).toBe('Good');
  });

  it('shows Strong for password meeting all criteria', () => {
    const { container } = render(() => <PasswordStrength password="Abcd1234!xyz" />);
    expect(container.querySelector('span')?.textContent).toBe('Strong');
  });

  it('renders four meter segments', () => {
    const { container } = render(() => <PasswordStrength password="abc" />);
    const segments = container.querySelectorAll('div.h-1');
    expect(segments).toHaveLength(4);
  });
});
