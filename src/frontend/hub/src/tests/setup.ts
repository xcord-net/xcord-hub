import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@solidjs/testing-library';

if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

// Reset fetch + cleanup mounted DOM between tests so component tests are isolated.
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  if (typeof globalThis.fetch === 'function' && 'mockReset' in globalThis.fetch) {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockReset();
  }
});

// Default fetch stub - tests should override per-suite via vi.stubGlobal('fetch', ...).
globalThis.fetch = vi.fn(async () => {
  throw new Error('fetch was called without being mocked - stub it with mockFetch() in your test');
}) as typeof globalThis.fetch;
