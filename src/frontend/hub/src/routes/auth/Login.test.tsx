import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import Login from './Login';
import { useAuth } from '../../stores/auth.store';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/login' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={Login} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('Login (route)', () => {
  beforeEach(() => {
    useAuth().reset();
    localStorage.clear();
  });

  it('renders heading and email/password fields', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('auth-heading').textContent).toBe('Log in');
    expect(getByTestId('hub-login-email')).toBeInTheDocument();
    expect(getByTestId('hub-login-password')).toBeInTheDocument();
  });

  it('renders the register link', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('hub-login-register-link')).toBeInTheDocument();
  });

  it('renders the forgot-password link', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('hub-login-forgot-password')).toBeInTheDocument();
  });

  it('shows the auth error from the store when login fails', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => ({
        status: 400,
        body: { message: 'Bad credentials' },
      }),
    });
    const { getByTestId, findByTestId } = renderPage();
    fireEvent.input(getByTestId('hub-login-email'), {
      target: { value: 'a@example.com' },
    });
    fireEvent.input(getByTestId('hub-login-password'), {
      target: { value: 'wrong' },
    });
    fireEvent.click(getByTestId('login-submit-button'));
    expect(await findByTestId('hub-login-error')).toBeInTheDocument();
  });

  it('switches to the 2FA prompt when login returns 2fa_required', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => ({
        status: 401,
        body: { title: '2FA_REQUIRED' },
      }),
    });
    const { getByTestId, findByText } = renderPage();
    fireEvent.input(getByTestId('hub-login-email'), {
      target: { value: 'a@example.com' },
    });
    fireEvent.input(getByTestId('hub-login-password'), {
      target: { value: 'pw' },
    });
    fireEvent.click(getByTestId('login-submit-button'));
    expect(await findByText(/Enter the 6-digit code/)).toBeInTheDocument();
  });

  it('hides the dev login button when the hub does not offer it', async () => {
    mockFetch({
      'GET /api/v1/hub/features': () => ({ status: 200, body: { devLoginEnabled: false } }),
    });
    const { getByTestId, queryByTestId } = renderPage();
    // Wait for the features probe to settle before asserting the absence.
    await waitFor(() => expect(getByTestId('login-submit-button')).toBeInTheDocument());
    await waitFor(() => expect(queryByTestId('dev-login-button')).not.toBeInTheDocument());
  });

  it('shows the dev login button and signs in through it when enabled', async () => {
    const { calls } = mockFetch({
      'GET /api/v1/hub/features': () => ({ status: 200, body: { devLoginEnabled: true } }),
      'POST /api/v1/test/dev-login': () => ({
        status: 200,
        body: {
          userId: '1',
          username: 'e2e-admin',
          displayName: 'E2E Admin',
          email: 'admin@e2e.test',
          accessToken: 'dev-token',
        },
      }),
    });
    const { findByTestId } = renderPage();
    fireEvent.click(await findByTestId('dev-login-button'));
    await waitFor(() =>
      expect(calls).toContainEqual(
        expect.objectContaining({ method: 'POST', url: '/api/v1/test/dev-login' }),
      ),
    );
    // The hub keeps its access token in localStorage, so a dev login has to
    // land there for restoreSession to pick the session back up on reload.
    await waitFor(() => expect(localStorage.getItem('xcord_hub_token')).toBe('dev-token'));
  });

  it('disables the submit button while loading', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => new Promise(() => {}),
    });
    const { getByTestId } = renderPage();
    fireEvent.input(getByTestId('hub-login-email'), {
      target: { value: 'a@example.com' },
    });
    fireEvent.input(getByTestId('hub-login-password'), {
      target: { value: 'pw' },
    });
    const btn = getByTestId('login-submit-button') as HTMLButtonElement;
    fireEvent.click(btn);
    await waitFor(() => expect(btn.disabled).toBe(true));
  });
});
