/**
 * CSRF defense: install a fetch interceptor that adds the X-Xcord-Request
 * header on all same-origin state-changing requests.
 *
 * The backend requires this header on cookie-authenticated POST/PUT/PATCH/DELETE
 * requests. Browsers will not send custom headers on cross-origin form
 * submissions, image loads, or simple navigations, so an attacker page cannot
 * forge state-changing requests against a victim's session.
 *
 * Same-origin only: cross-origin fetches (e.g. third-party APIs) are left
 * untouched so we do not leak the marker or trigger unrelated CORS preflights.
 */
const CSRF_HEADER = 'X-Xcord-Request';
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

let installed = false;

export function installCsrfFetchInterceptor(): void {
  if (installed) return;
  if (typeof window === 'undefined' || typeof window.fetch !== 'function') return;

  installed = true;
  const originalFetch = window.fetch.bind(window);

  window.fetch = (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    try {
      const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
      if (SAFE_METHODS.has(method)) {
        return originalFetch(input, init);
      }

      // Resolve the request URL to determine same-origin.
      let url: string;
      if (typeof input === 'string') {
        url = input;
      } else if (input instanceof URL) {
        url = input.toString();
      } else {
        url = input.url;
      }

      const target = new URL(url, window.location.href);
      if (target.origin !== window.location.origin) {
        return originalFetch(input, init);
      }

      // Merge our header into whatever the caller passed.
      const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
      if (!headers.has(CSRF_HEADER)) {
        headers.set(CSRF_HEADER, '1');
      }

      const nextInit: RequestInit = { ...(init ?? {}), headers };
      return originalFetch(input, nextInit);
    } catch {
      // If anything goes wrong figuring out the URL, fall back to the original
      // request rather than breaking the call entirely.
      return originalFetch(input, init);
    }
  };
}
