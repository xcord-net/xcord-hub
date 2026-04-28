import { vi } from 'vitest';

export type MockResponse =
  | { status?: number; body?: unknown; headers?: Record<string, string> }
  | unknown;

export type RouteHandler = (req: {
  method: string;
  url: string;
  init?: RequestInit;
  body?: unknown;
}) => MockResponse | Promise<MockResponse>;

export interface MockFetchOptions {
  baseUrl?: string;
}

export function mockFetch(
  routes: Record<string, RouteHandler>,
  opts: MockFetchOptions = {},
) {
  const baseUrl = opts.baseUrl ?? 'http://xcord-dev.net';
  const calls: { method: string; url: string; body: unknown }[] = [];

  globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const rawUrl = typeof input === 'string' ? input : input.toString();
    const url = new URL(rawUrl, baseUrl);
    const method = (init?.method ?? 'GET').toUpperCase();
    let body: unknown = undefined;
    if (init?.body && typeof init.body === 'string') {
      try { body = JSON.parse(init.body); } catch { body = init.body; }
    }
    calls.push({ method, url: url.pathname + url.search, body });

    const matchKey = `${method} ${url.pathname}`;
    const handler =
      routes[matchKey] ??
      routes[url.pathname] ??
      Object.entries(routes).find(([pattern]) => {
        const [m, p] = pattern.includes(' ') ? pattern.split(' ') : ['*', pattern];
        if (m !== '*' && m !== method) return false;
        return new RegExp(`^${p.replace(/:\w+/g, '[^/]+').replace(/\*/g, '.*')}$`).test(url.pathname);
      })?.[1];

    if (!handler) {
      throw new Error(`mockFetch: no handler for ${method} ${url.pathname}`);
    }

    const result = await handler({ method, url: url.pathname + url.search, init, body });
    // Treat the handler return as a Response wrapper only when it is a tight
    // {status?, body?, headers?} object — not a domain payload that happens to
    // include a `status` field (e.g. an instance whose state is "running").
    const isResponseShape =
      result !== null &&
      typeof result === 'object' &&
      (
        ('status' in result && typeof (result as { status: unknown }).status === 'number') ||
        ('headers' in result && typeof (result as { headers: unknown }).headers === 'object') ||
        // explicit body key with no other domain keys
        (Object.keys(result as object).every(k => k === 'status' || k === 'body' || k === 'headers') && 'body' in result)
      );

    if (isResponseShape) {
      const r = result as { status?: number; body?: unknown; headers?: Record<string, string> };
      const status = r.status ?? 200;
      const noBody = status === 204 || status === 205 || status === 304;
      return new Response(!noBody && r.body !== undefined ? JSON.stringify(r.body) : null, {
        status,
        headers: { 'content-type': 'application/json', ...(r.headers ?? {}) },
      });
    }

    return new Response(JSON.stringify(result ?? null), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
  }) as typeof globalThis.fetch;

  return { calls };
}
