import { describe, it, expect } from 'vitest';
import { render } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import { fireEvent, waitFor } from '@solidjs/testing-library';
import ForgotPassword from './ForgotPassword';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/forgot-password' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={ForgotPassword} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

describe('ForgotPassword (route)', () => {
  it('renders heading and email input', () => {
    const { getByText, getByTestId } = renderPage();
    expect(getByText('Forgot your password?')).toBeInTheDocument();
    expect(getByTestId('forgot-password-email')).toBeInTheDocument();
  });

  it('renders the submit button', () => {
    const { getByTestId } = renderPage();
    expect(getByTestId('forgot-password-submit-button')).toBeInTheDocument();
  });

  it('shows success confirmation after a successful submit', async () => {
    mockFetch({
      'POST /api/v1/auth/forgot-password': () => ({ status: 204 }),
    });
    const { getByTestId, findByTestId } = renderPage();
    const email = getByTestId('forgot-password-email') as HTMLInputElement;
    fireEvent.input(email, { target: { value: 'a@example.com' } });
    fireEvent.click(getByTestId('forgot-password-submit-button'));
    expect(await findByTestId('forgot-password-success')).toBeInTheDocument();
  });

  it('disables the submit button while loading', async () => {
    mockFetch({
      'POST /api/v1/auth/forgot-password': () => new Promise(() => {}),
    });
    const { getByTestId } = renderPage();
    const email = getByTestId('forgot-password-email') as HTMLInputElement;
    fireEvent.input(email, { target: { value: 'a@example.com' } });
    const btn = getByTestId('forgot-password-submit-button') as HTMLButtonElement;
    fireEvent.click(btn);
    await waitFor(() => expect(btn.disabled).toBe(true));
  });

  it('renders the back-to-login link', () => {
    const { getByText } = renderPage();
    expect(getByText('Back to login')).toBeInTheDocument();
  });
});
