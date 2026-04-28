import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import Register from './Register';
import { useAuth } from '../../stores/auth.store';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/register' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={Register} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

// Default captcha mock so the component's onMount fetch doesn't blow up.
function defaultCaptchaRoute() {
  return {
    'GET /api/v1/auth/captcha': () => ({ captchaId: 'disabled', question: '' }),
  };
}

describe('Register (route)', () => {
  beforeEach(() => {
    useAuth().reset();
    localStorage.clear();
  });

  it('renders the heading and core form fields', () => {
    mockFetch(defaultCaptchaRoute());
    const { getByText, getByTestId } = renderPage();
    expect(getByText('Create an account')).toBeInTheDocument();
    expect(getByTestId('hub-reg-email')).toBeInTheDocument();
    expect(getByTestId('hub-reg-username')).toBeInTheDocument();
    expect(getByTestId('hub-reg-password')).toBeInTheDocument();
    expect(getByTestId('hub-reg-submit')).toBeInTheDocument();
  });

  it('renders the login link', () => {
    mockFetch(defaultCaptchaRoute());
    const { getByTestId } = renderPage();
    const link = getByTestId('hub-reg-login-link') as HTMLAnchorElement;
    expect(link).toBeInTheDocument();
    expect(link.getAttribute('href')).toBe('/login');
  });

  it('shows local error when passwords do not match', async () => {
    mockFetch(defaultCaptchaRoute());
    const { getByTestId, container, findByTestId } = renderPage();
    fireEvent.input(getByTestId('hub-reg-email'), { target: { value: 'a@example.com' } });
    fireEvent.input(getByTestId('hub-reg-username'), { target: { value: 'alice' } });
    fireEvent.input(getByTestId('hub-reg-password'), { target: { value: 'longenough' } });
    const confirm = container.querySelector('#hub-reg-confirm-password') as HTMLInputElement;
    fireEvent.input(confirm, { target: { value: 'different' } });
    fireEvent.submit(container.querySelector('form')!);
    const err = await findByTestId('hub-reg-error');
    expect(err.textContent).toBe('Passwords do not match');
  });

  it('shows local error when terms are not all agreed', async () => {
    mockFetch(defaultCaptchaRoute());
    const { getByTestId, container, findByTestId } = renderPage();
    fireEvent.input(getByTestId('hub-reg-email'), { target: { value: 'a@example.com' } });
    fireEvent.input(getByTestId('hub-reg-username'), { target: { value: 'alice' } });
    fireEvent.input(getByTestId('hub-reg-password'), { target: { value: 'longenough' } });
    const confirm = container.querySelector('#hub-reg-confirm-password') as HTMLInputElement;
    fireEvent.input(confirm, { target: { value: 'longenough' } });
    fireEvent.submit(container.querySelector('form')!);
    const err = await findByTestId('hub-reg-error');
    expect(err.textContent).toBe('You must agree to all terms');
  });

  it('shows API error when signup endpoint returns failure', async () => {
    mockFetch({
      ...defaultCaptchaRoute(),
      'POST /api/v1/auth/register': () => ({
        status: 400,
        body: { detail: 'Email already in use' },
      }),
    });
    const { getByTestId, container, findByTestId } = renderPage();
    fireEvent.input(getByTestId('hub-reg-email'), { target: { value: 'a@example.com' } });
    fireEvent.input(getByTestId('hub-reg-username'), { target: { value: 'alice' } });
    fireEvent.input(getByTestId('hub-reg-password'), { target: { value: 'longenough' } });
    const confirm = container.querySelector('#hub-reg-confirm-password') as HTMLInputElement;
    fireEvent.input(confirm, { target: { value: 'longenough' } });
    // Tick all three required checkboxes.
    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    checkboxes.forEach((cb) => fireEvent.click(cb));
    fireEvent.submit(container.querySelector('form')!);
    const err = await findByTestId('hub-reg-error');
    expect(err.textContent).toBe('Email already in use');
  });

  it('disables the submit button while a signup request is in-flight', async () => {
    mockFetch({
      ...defaultCaptchaRoute(),
      'POST /api/v1/auth/register': () => new Promise(() => {}),
    });
    const { getByTestId, container } = renderPage();
    fireEvent.input(getByTestId('hub-reg-email'), { target: { value: 'a@example.com' } });
    fireEvent.input(getByTestId('hub-reg-username'), { target: { value: 'alice' } });
    fireEvent.input(getByTestId('hub-reg-password'), { target: { value: 'longenough' } });
    const confirm = container.querySelector('#hub-reg-confirm-password') as HTMLInputElement;
    fireEvent.input(confirm, { target: { value: 'longenough' } });
    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    checkboxes.forEach((cb) => fireEvent.click(cb));
    fireEvent.submit(container.querySelector('form')!);
    const submit = getByTestId('hub-reg-submit') as HTMLButtonElement;
    await waitFor(() => expect(submit.disabled).toBe(true));
    expect(submit.textContent).toBe('Creating account...');
  });
});
