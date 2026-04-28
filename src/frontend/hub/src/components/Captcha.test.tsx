import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import Captcha from './Captcha';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('Captcha', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows a loading state before the challenge resolves', () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => new Promise(() => {}) as Promise<unknown>,
    });
    const { getByText } = render(() => <Captcha onSolved={() => {}} />);
    expect(getByText('Loading challenge...')).toBeInTheDocument();
  });

  it('renders the challenge question once loaded', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: { captchaId: 'abc', question: '2 + 2' },
      }),
    });
    const { findByText } = render(() => <Captcha onSolved={() => {}} />);
    expect(await findByText(/2 \+ 2/)).toBeInTheDocument();
  });

  it('reports captchaId="disabled" via onSolved when challenge is disabled', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: { captchaId: 'disabled', question: '' },
      }),
    });
    const onSolved = vi.fn();
    render(() => <Captcha onSolved={onSolved} />);
    await waitFor(() => expect(onSolved).toHaveBeenCalledWith('disabled', ''));
  });

  it('calls onSolved with the typed answer', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: { captchaId: 'cid-1', question: '1 + 1' },
      }),
    });
    const onSolved = vi.fn();
    const { container, findByPlaceholderText } = render(() => <Captcha onSolved={onSolved} />);
    const input = (await findByPlaceholderText('Your answer')) as HTMLInputElement;
    fireEvent.input(input, { target: { value: '2' } });
    expect(onSolved).toHaveBeenCalledWith('cid-1', '2');
    expect(container.querySelector('input')?.value).toBe('2');
  });

  it('refetches when "New" button is clicked', async () => {
    let callCount = 0;
    mockFetch({
      'GET /api/v1/auth/captcha': () => {
        callCount++;
        return { status: 200, body: { captchaId: `id-${callCount}`, question: `q-${callCount}` } };
      },
    });
    const { findByText, getByText } = render(() => <Captcha onSolved={() => {}} />);
    expect(await findByText(/q-1/)).toBeInTheDocument();
    fireEvent.click(getByText('New'));
    expect(await findByText(/q-2/)).toBeInTheDocument();
  });
});
