import { render } from '@solidjs/testing-library';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import type { JSX } from 'solid-js';

export interface RenderWithRouterOptions {
  path?: string;
  routePath?: string;
}

export function renderWithRouter(
  ui: () => JSX.Element,
  opts: RenderWithRouterOptions = {},
) {
  const path = opts.path ?? '/';
  const routePath = opts.routePath ?? '*';
  const history = createMemoryHistory();
  history.set({ value: path });

  return render(() => (
    <MemoryRouter history={history}>
      <Route path={routePath} component={ui} />
    </MemoryRouter>
  ));
}
