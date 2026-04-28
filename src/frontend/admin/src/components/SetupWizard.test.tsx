import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { SetupWizard } from './SetupWizard';
import { mockFetch } from '../tests/helpers/mockFetch';

function fill(container: HTMLElement, opts: Partial<{ username: string; email: string; password: string; confirm: string }> = {}) {
  const u = opts.username ?? 'admin';
  const e = opts.email ?? 'admin@example.com';
  const p = opts.password ?? 'password1';
  const c = opts.confirm ?? p;
  fireEvent.input(container.querySelector('#username')!, { target: { value: u } });
  fireEvent.input(container.querySelector('#email')!, { target: { value: e } });
  fireEvent.input(container.querySelector('#password')!, { target: { value: p } });
  fireEvent.input(container.querySelector('#confirm-password')!, { target: { value: c } });
}

describe('SetupWizard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('renders heading and all fields', () => {
    const { getByText, container } = render(() => <SetupWizard onComplete={() => {}} />);
    expect(getByText(/Hub Admin/)).toBeInTheDocument();
    expect(container.querySelector('#username')).not.toBeNull();
    expect(container.querySelector('#email')).not.toBeNull();
    expect(container.querySelector('#password')).not.toBeNull();
    expect(container.querySelector('#confirm-password')).not.toBeNull();
  });

  it('shows validation error when password is too short', async () => {
    const { container, findByText } = render(() => <SetupWizard onComplete={() => {}} />);
    fill(container, { password: 'short', confirm: 'short' });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/at least 8 characters/)).toBeInTheDocument();
  });

  it('shows error when passwords do not match', async () => {
    const { container, findByText } = render(() => <SetupWizard onComplete={() => {}} />);
    fill(container, { password: 'password1', confirm: 'different1' });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/Passwords do not match/)).toBeInTheDocument();
  });

  it('calls onComplete on successful setup', async () => {
    mockFetch({
      'POST /api/v1/setup': () => ({ status: 200, body: { accessToken: 'tok' } }),
    });
    const onComplete = vi.fn();
    const { container } = render(() => <SetupWizard onComplete={onComplete} />);
    fill(container);
    fireEvent.submit(container.querySelector('form')!);
    await waitFor(() => expect(onComplete).toHaveBeenCalled());
    expect(localStorage.getItem('accessToken')).toBe('tok');
  });

  it('shows API error message when setup fails', async () => {
    mockFetch({
      'POST /api/v1/setup': () => ({ status: 400, body: { detail: 'Already initialized' } }),
    });
    const { container, findByText } = render(() => <SetupWizard onComplete={() => {}} />);
    fill(container);
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/Already initialized|Setup failed/)).toBeInTheDocument();
  });
});
