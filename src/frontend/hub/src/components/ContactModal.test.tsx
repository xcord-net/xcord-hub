import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent } from '@solidjs/testing-library';
import ContactModal from './ContactModal';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('ContactModal', () => {
  function fill(container: HTMLElement, overrides: Partial<{ name: string; email: string; message: string }> = {}) {
    const inputs = container.querySelectorAll('input');
    const textarea = container.querySelector('textarea')!;
    fireEvent.input(inputs[0], { target: { value: overrides.name ?? 'Pat' } });
    fireEvent.input(inputs[1], { target: { value: overrides.email ?? 'pat@example.com' } });
    fireEvent.input(textarea, { target: { value: overrides.message ?? 'Hi there' } });
  }

  it('renders nothing when closed', () => {
    const { container } = render(() => <ContactModal open={false} onClose={() => {}} />);
    expect(container.querySelector('h2')).toBeNull();
  });

  it('renders form when open', () => {
    const { getByText } = render(() => <ContactModal open={true} onClose={() => {}} />);
    expect(getByText('Contact Us')).toBeInTheDocument();
    expect(getByText('Send Message')).toBeInTheDocument();
  });

  it('calls onClose when × button clicked', () => {
    const onClose = vi.fn();
    const { getByText } = render(() => <ContactModal open={true} onClose={onClose} />);
    fireEvent.click(getByText('×'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('shows validation error for invalid email', async () => {
    const { container, findByText } = render(() => (
      <ContactModal open={true} onClose={() => {}} />
    ));
    fill(container, { email: 'no-at-sign' });
    // submit form directly to bypass jsdom's HTML5 validity check on input[type=email]
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText('Please enter a valid email address.')).toBeInTheDocument();
  });

  it('submits to API and shows success message', async () => {
    mockFetch({
      'POST /api/v1/hub/contact': () => ({ status: 200, body: {} }),
    });
    const { container, getByText, findByText } = render(() => (
      <ContactModal open={true} onClose={() => {}} />
    ));
    fill(container);
    fireEvent.click(getByText('Send Message'));
    expect(await findByText(/Thanks for reaching out/)).toBeInTheDocument();
  });

  it('shows API error message when submission fails', async () => {
    mockFetch({
      'POST /api/v1/hub/contact': () => ({ status: 500, body: { message: 'Server is down' } }),
    });
    const { container, getByText, findByText } = render(() => (
      <ContactModal open={true} onClose={() => {}} />
    ));
    fill(container);
    fireEvent.click(getByText('Send Message'));
    expect(await findByText('Server is down')).toBeInTheDocument();
  });
});
