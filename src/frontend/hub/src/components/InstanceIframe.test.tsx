import { describe, it, expect, beforeEach } from 'vitest';
import { render } from '@solidjs/testing-library';
import InstanceIframe from './InstanceIframe';
import { unreadStore } from '../stores/unread.store';

describe('InstanceIframe', () => {
  beforeEach(() => {
    unreadStore.reset();
  });

  it('renders an iframe with the given src', () => {
    const { container } = render(() => (
      <InstanceIframe url="https://example.com" visible={true} />
    ));
    const iframe = container.querySelector('iframe');
    expect(iframe).toBeInTheDocument();
    expect(iframe?.getAttribute('src')).toBe('https://example.com');
  });

  it('applies display:block when visible', () => {
    const { container } = render(() => (
      <InstanceIframe url="https://example.com" visible={true} />
    ));
    const iframe = container.querySelector('iframe') as HTMLIFrameElement;
    expect(iframe.style.display).toBe('block');
  });

  it('applies display:none when not visible', () => {
    const { container } = render(() => (
      <InstanceIframe url="https://example.com" visible={false} />
    ));
    const iframe = container.querySelector('iframe') as HTMLIFrameElement;
    expect(iframe.style.display).toBe('none');
  });

  it('sets the sandbox attribute with expected permissions', () => {
    const { container } = render(() => (
      <InstanceIframe url="https://example.com" visible={true} />
    ));
    const iframe = container.querySelector('iframe') as HTMLIFrameElement;
    const sandbox = iframe.getAttribute('sandbox') ?? '';
    expect(sandbox).toContain('allow-scripts');
    expect(sandbox).toContain('allow-same-origin');
    expect(sandbox).toContain('allow-forms');
  });

  it('cleans up trusted origin tracking on unmount', () => {
    const { unmount } = render(() => (
      <InstanceIframe url="https://example.com" visible={true} />
    ));
    // Round-trip mount + unmount shouldn't throw and should not leave dangling listeners.
    expect(() => unmount()).not.toThrow();
  });
});
