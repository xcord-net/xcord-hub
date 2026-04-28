import { describe, it, expect } from 'vitest';
import { render, fireEvent } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import ResetPassword from './ResetPassword';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage(path = '/reset-password?token=abc123') {
  const history = createMemoryHistory();
  history.set({ value: path });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={ResetPassword} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('ResetPassword (route)', () => {
  it('renders the heading and password fields', () => {
    const { getByText, container } = renderPage();
    expect(getByText('Set a new password')).toBeInTheDocument();
    const inputs = container.querySelectorAll('input[type="password"]');
    expect(inputs.length).toBe(2);
  });

  it('renders the submit button', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('reset-password-submit-button')).toBeInTheDocument();
  });

  it('shows an error when token is missing', async () => {
    const { container, findByText } = renderPage('/reset-password');
    const inputs = container.querySelectorAll<HTMLInputElement>('input[type="password"]');
    fireEvent.input(inputs[0], { target: { value: 'longenough' } });
    fireEvent.input(inputs[1], { target: { value: 'longenough' } });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/Invalid or missing reset token/)).toBeInTheDocument();
  });

  it('shows mismatch error when passwords do not match', async () => {
    const { container, findByText } = renderPage();
    const inputs = container.querySelectorAll<HTMLInputElement>('input[type="password"]');
    fireEvent.input(inputs[0], { target: { value: 'password1' } });
    fireEvent.input(inputs[1], { target: { value: 'password2' } });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText('Passwords do not match')).toBeInTheDocument();
  });

  it('shows success state when reset succeeds', async () => {
    mockFetch({
      'POST /api/v1/auth/reset-password': () => ({ status: 204 }),
    });
    const { container, findByTestId } = renderPage();
    const inputs = container.querySelectorAll<HTMLInputElement>('input[type="password"]');
    fireEvent.input(inputs[0], { target: { value: 'longenough' } });
    fireEvent.input(inputs[1], { target: { value: 'longenough' } });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByTestId('reset-password-success')).toBeInTheDocument();
  });

  it('shows API error message when reset endpoint returns failure', async () => {
    mockFetch({
      'POST /api/v1/auth/reset-password': () => ({
        status: 400,
        body: { detail: 'Token expired' },
      }),
    });
    const { container, findByText } = renderPage();
    const inputs = container.querySelectorAll<HTMLInputElement>('input[type="password"]');
    fireEvent.input(inputs[0], { target: { value: 'longenough' } });
    fireEvent.input(inputs[1], { target: { value: 'longenough' } });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText('Token expired')).toBeInTheDocument();
  });
});
