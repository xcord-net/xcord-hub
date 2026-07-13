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

  it('renders the captcha image once loaded', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: {
          captchaId: 'abc',
          imageUrl: '/api/v1/auth/captcha/abc.gif',
          audioUrl: '/api/v1/auth/captcha/abc.wav',
        },
      }),
    });
    const { findByTestId } = render(() => <Captcha onSolved={() => {}} />);
    const img = (await findByTestId('captcha-image')) as HTMLImageElement;
    expect(img).toBeInTheDocument();
    expect(img.src).toContain('/api/v1/auth/captcha/abc.gif');
  });

  it('swaps to audio when the audio toggle is clicked', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: {
          captchaId: 'abc',
          imageUrl: '/api/v1/auth/captcha/abc.gif',
          audioUrl: '/api/v1/auth/captcha/abc.wav',
        },
      }),
    });
    const { findByTestId, getByTestId, queryByTestId } = render(() => <Captcha onSolved={() => {}} />);
    await findByTestId('captcha-image');
    fireEvent.click(getByTestId('captcha-audio-toggle'));
    const audio = (await findByTestId('captcha-audio')) as HTMLAudioElement;
    expect(audio.src).toContain('/api/v1/auth/captcha/abc.wav');
    expect(queryByTestId('captcha-image')).not.toBeInTheDocument();
  });

  it('reports captchaId="disabled" via onSolved when challenge is disabled', async () => {
    mockFetch({
      'GET /api/v1/auth/captcha': () => ({
        status: 200,
        body: { captchaId: 'disabled', imageUrl: '', audioUrl: '' },
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
        body: {
          captchaId: 'cid-1',
          imageUrl: '/api/v1/auth/captcha/cid-1.gif',
          audioUrl: '/api/v1/auth/captcha/cid-1.wav',
        },
      }),
    });
    const onSolved = vi.fn();
    const { findByTestId } = render(() => <Captcha onSolved={onSolved} />);
    const input = (await findByTestId('captcha-input')) as HTMLInputElement;
    fireEvent.input(input, { target: { value: 'gxqt' } });
    expect(onSolved).toHaveBeenCalledWith('cid-1', 'gxqt');
    expect(input.value).toBe('gxqt');
  });

  it('refetches when "New" button is clicked', async () => {
    let callCount = 0;
    mockFetch({
      'GET /api/v1/auth/captcha': () => {
        callCount++;
        return {
          status: 200,
          body: {
            captchaId: `id-${callCount}`,
            imageUrl: `/api/v1/auth/captcha/id-${callCount}.gif`,
            audioUrl: `/api/v1/auth/captcha/id-${callCount}.wav`,
          },
        };
      },
    });
    const { findByTestId, getByTestId } = render(() => <Captcha onSolved={() => {}} />);
    const firstImg = (await findByTestId('captcha-image')) as HTMLImageElement;
    expect(firstImg.src).toContain('id-1.gif');
    fireEvent.click(getByTestId('captcha-new'));
    await waitFor(async () => {
      const img = (await findByTestId('captcha-image')) as HTMLImageElement;
      expect(img.src).toContain('id-2.gif');
    });
  });
});
