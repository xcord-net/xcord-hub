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
    const { getByText, getByTestId } = renderPage();
    expect(getByText('Log in')).toBeInTheDocument();
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
