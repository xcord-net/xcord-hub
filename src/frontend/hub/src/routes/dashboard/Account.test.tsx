import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { MetaProvider } from '@solidjs/meta';
import { MemoryRouter, Route, createMemoryHistory } from '@solidjs/router';
import Account from './Account';
import { useAuth } from '../../stores/auth.store';
import { mockFetch } from '../../tests/helpers/mockFetch';

function renderPage() {
  const history = createMemoryHistory();
  history.set({ value: '/dashboard/account' });
  return render(() => (
    <MetaProvider>
      <MemoryRouter history={history}>
        <Route path="*" component={Account} />
      </MemoryRouter>
    </MetaProvider>
  ));
}

// Seed the auth store with a logged-in user by going through the login() flow
// against a mocked endpoint. The Account page reads auth.user fields directly.
async function seedLoggedInUser() {
  mockFetch({
    'POST /api/v1/auth/login': () => ({
      status: 200,
      body: {
        accessToken: 'test-token',
        userId: '1',
        username: 'alice',
        displayName: 'Alice',
        email: 'alice@example.com',
      },
    }),
  });
  const result = await useAuth().login('alice@example.com', 'pw');
  expect(result).toBe('success');
}

describe('Account (route)', () => {
  beforeEach(() => {
    useAuth().reset();
    localStorage.clear();
  });

  it('renders the Account heading', async () => {
    await seedLoggedInUser();
    const { getByTestId } = renderPage();
    expect(getByTestId('account-heading')).toBeInTheDocument();
  });

  it('renders Profile, Change Password, and Danger Zone sections', async () => {
    await seedLoggedInUser();
    const { getByText, getAllByText } = renderPage();
    expect(getByText('Profile')).toBeInTheDocument();
    // 'Change Password' appears as both <h2> heading and submit <button>.
    expect(getAllByText('Change Password').length).toBeGreaterThan(1);
    expect(getByText('Danger Zone')).toBeInTheDocument();
  });

  it('pre-populates the display name and email from the auth store', async () => {
    await seedLoggedInUser();
    const { container } = renderPage();
    const inputs = container.querySelectorAll<HTMLInputElement>('input');
    const displayNameInput = Array.from(inputs).find((i) => i.value === 'Alice');
    const emailInput = Array.from(inputs).find((i) => i.value === 'alice@example.com');
    expect(displayNameInput).toBeTruthy();
    expect(emailInput).toBeTruthy();
  });

  it('shows password mismatch error when changing password with non-matching values', async () => {
    await seedLoggedInUser();
    const { container, getAllByText, findByText } = renderPage();
    // The Change Password form is the second form on the page.
    const forms = container.querySelectorAll('form');
    const changePasswordForm = forms[1];
    const pwInputs = changePasswordForm.querySelectorAll<HTMLInputElement>('input[type="password"]');
    fireEvent.input(pwInputs[0], { target: { value: 'currentpw' } });
    fireEvent.input(pwInputs[1], { target: { value: 'newpassword1' } });
    fireEvent.input(pwInputs[2], { target: { value: 'differentpw' } });
    fireEvent.submit(changePasswordForm);
    expect(await findByText('Passwords do not match')).toBeInTheDocument();
    // sanity: the heading "Change Password" still exists alongside the button text
    expect(getAllByText('Change Password').length).toBeGreaterThan(0);
  });

  it('reveals the delete-account confirmation row when Delete Account is clicked', async () => {
    await seedLoggedInUser();
    const { getByText, queryByText } = renderPage();
    expect(queryByText('Permanently Delete')).toBeNull();
    fireEvent.click(getByText('Delete Account'));
    expect(getByText('Permanently Delete')).toBeInTheDocument();
    expect(getByText('Cancel')).toBeInTheDocument();
  });

  it('shows a username-mismatch error when confirmation text does not match', async () => {
    await seedLoggedInUser();
    const { getByText, getByPlaceholderText, findByText } = renderPage();
    fireEvent.click(getByText('Delete Account'));
    // Wrong confirmation username + a password (so the password gate passes and we hit username check).
    const usernameConfirm = getByPlaceholderText('alice') as HTMLInputElement;
    const pwConfirm = getByPlaceholderText('Enter your password') as HTMLInputElement;
    fireEvent.input(usernameConfirm, { target: { value: 'not-alice' } });
    fireEvent.input(pwConfirm, { target: { value: 'pw' } });
    fireEvent.click(getByText('Permanently Delete'));
    expect(await findByText(/Username does not match/)).toBeInTheDocument();
  });

  it('hides the delete confirmation when Cancel is clicked', async () => {
    await seedLoggedInUser();
    const { getByText, queryByText } = renderPage();
    fireEvent.click(getByText('Delete Account'));
    expect(getByText('Permanently Delete')).toBeInTheDocument();
    fireEvent.click(getByText('Cancel'));
    await waitFor(() => expect(queryByText('Permanently Delete')).toBeNull());
  });
});
