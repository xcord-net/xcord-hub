import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { Login } from './Login';
import { useAuth } from '../stores/auth.store';
import { mockFetch } from '../tests/helpers/mockFetch';

// admin-claim payload (header.payload.signature). { admin: 'true' }.
const ADMIN_JWT = 'h.' + btoa('{"admin":"true"}') + '.s';
const NONADMIN_JWT = 'h.' + btoa('{"admin":"false"}') + '.s';

describe('Login', () => {
  beforeEach(() => {
    useAuth().reset();
    localStorage.clear();
    // Stub window.location.reload so successful login doesn't crash jsdom.
    Object.defineProperty(window, 'location', {
      value: { ...window.location, reload: vi.fn() },
      writable: true,
    });
  });

  function fill(container: HTMLElement, email = 'admin@example.com', password = 'pw') {
    const inputs = container.querySelectorAll('input');
    fireEvent.input(inputs[0], { target: { value: email } });
    fireEvent.input(inputs[1], { target: { value: password } });
  }

  it('renders email and password fields plus Sign In button', () => {
    const { container, getByText } = render(() => <Login />);
    expect(container.querySelectorAll('input')).toHaveLength(2);
    expect(getByText('Sign In')).toBeInTheDocument();
  });

  it('submits to login endpoint and sets auth on success', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => ({
        status: 200,
        body: { accessToken: ADMIN_JWT, userId: '42', username: 'admin' },
      }),
    });
    const { container, getByText } = render(() => <Login />);
    fill(container);
    fireEvent.click(getByText('Sign In'));
    await waitFor(() => expect(useAuth().isAuthenticated).toBe(true));
    expect(useAuth().isAdmin).toBe(true);
  });

  it('shows error when admin claim is missing', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => ({
        status: 200,
        body: { accessToken: NONADMIN_JWT, userId: '42', username: 'user' },
      }),
    });
    const { container, getByText, findByText } = render(() => <Login />);
    fill(container);
    fireEvent.click(getByText('Sign In'));
    expect(await findByText(/Access denied/)).toBeInTheDocument();
  });

  it('shows error message when login API fails', async () => {
    mockFetch({
      'POST /api/v1/auth/login': () => ({ status: 400, body: { message: 'Bad credentials' } }),
    });
    const { container, findByText } = render(() => <Login />);
    fill(container);
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/Bad credentials|Login failed/)).toBeInTheDocument();
  });

  it('disables Sign In button while loading', async () => {
    let resolve!: (v: unknown) => void;
    mockFetch({
      'POST /api/v1/auth/login': () => new Promise(r => { resolve = r; }) as Promise<{ status: number; body: object }>,
    });
    const { container, getByText } = render(() => <Login />);
    fill(container);
    const btn = getByText('Sign In') as HTMLButtonElement;
    fireEvent.click(btn);
    await waitFor(() => expect(btn).toBeDisabled());
    resolve({ status: 401, body: {} });
  });
});
