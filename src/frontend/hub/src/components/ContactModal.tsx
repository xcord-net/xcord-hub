import { createSignal, Show, onCleanup, createEffect } from 'solid-js';

interface ContactModalProps {
  open: boolean;
  onClose: () => void;
}

export default function ContactModal(props: ContactModalProps) {
  const [name, setName] = createSignal('');
  const [email, setEmail] = createSignal('');
  const [company, setCompany] = createSignal('');
  const [expectedMemberCount, setExpectedMemberCount] = createSignal('');
  const [message, setMessage] = createSignal('');
  const [status, setStatus] = createSignal<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [errorMessage, setErrorMessage] = createSignal('');

  function resetForm() {
    setName('');
    setEmail('');
    setCompany('');
    setExpectedMemberCount('');
    setMessage('');
    setStatus('idle');
    setErrorMessage('');
  }

  createEffect(() => {
    if (!props.open) {
      resetForm();
    }
  });

  function validate(): string | null {
    if (!name().trim()) return 'Name is required.';
    if (!email().trim()) return 'Email is required.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email().trim())) return 'Please enter a valid email address.';
    if (!message().trim()) return 'Message is required.';
    return null;
  }

  async function handleSubmit(e: Event) {
    e.preventDefault();
    const err = validate();
    if (err) {
      setStatus('error');
      setErrorMessage(err);
      return;
    }

    setStatus('loading');
    setErrorMessage('');

    try {
      const body: Record<string, string | number> = {
        name: name().trim(),
        email: email().trim(),
        message: message().trim(),
      };
      if (company().trim()) body.company = company().trim();
      if (expectedMemberCount()) body.expectedMemberCount = Number(expectedMemberCount());

      const res = await fetch('/api/v1/hub/contact', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        const data = await res.json().catch(() => ({ message: 'Something went wrong.' }));
        setStatus('error');
        setErrorMessage(data.message ?? 'Something went wrong.');
        return;
      }

      setStatus('success');
      const timer = setTimeout(() => {
        props.onClose();
      }, 3000);
      onCleanup(() => clearTimeout(timer));
    } catch {
      setStatus('error');
      setErrorMessage('Network error. Please try again.');
    }
  }

  const inputClass = 'w-full px-3 py-2 rounded-lg bg-xcord-bg-tertiary text-xcord-text-primary text-sm placeholder:text-xcord-text-muted/50 focus:outline-none focus:ring-2 focus:ring-xcord-brand border-none';
  const labelClass = 'block text-sm font-medium text-xcord-text-primary mb-1';

  return (
    <Show when={props.open}>
      <div
        class="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4"
        onClick={(e) => e.target === e.currentTarget && props.onClose()}
      >
        <div class="bg-xcord-bg-primary rounded-xl max-w-lg w-full max-h-[90vh] overflow-y-auto p-6">
          <div class="flex items-center justify-between mb-6">
            <h2 class="text-lg font-bold text-xcord-text-primary">Contact Us</h2>
            <button
              onClick={props.onClose}
              class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none"
            >
              &times;
            </button>
          </div>

          <Show when={status() === 'success'}>
            <p class="text-sm text-green-400 text-center py-8">
              Thanks for reaching out! We'll get back to you soon.
            </p>
          </Show>

          <Show when={status() !== 'success'}>
            <form onSubmit={handleSubmit} class="space-y-4">
              <div>
                <label class={labelClass}>Name *</label>
                <input
                  type="text"
                  required
                  value={name()}
                  onInput={(e) => setName(e.currentTarget.value)}
                  placeholder="Your name"
                  class={inputClass}
                />
              </div>

              <div>
                <label class={labelClass}>Email *</label>
                <input
                  type="email"
                  required
                  value={email()}
                  onInput={(e) => setEmail(e.currentTarget.value)}
                  placeholder="you@example.com"
                  class={inputClass}
                />
              </div>

              <div>
                <label class={labelClass}>Company / Organization</label>
                <input
                  type="text"
                  value={company()}
                  onInput={(e) => setCompany(e.currentTarget.value)}
                  placeholder="Optional"
                  class={inputClass}
                />
              </div>

              <div>
                <label class={labelClass}>Expected Member Count</label>
                <input
                  type="number"
                  value={expectedMemberCount()}
                  onInput={(e) => setExpectedMemberCount(e.currentTarget.value)}
                  placeholder="Optional"
                  min="1"
                  class={inputClass}
                />
              </div>

              <div>
                <label class={labelClass}>Message *</label>
                <textarea
                  required
                  rows={4}
                  value={message()}
                  onInput={(e) => setMessage(e.currentTarget.value)}
                  placeholder="Tell us about your needs..."
                  class={inputClass + ' resize-none'}
                />
              </div>

              <Show when={status() === 'error'}>
                <p class="text-sm text-red-400">{errorMessage()}</p>
              </Show>

              <button
                type="submit"
                disabled={status() === 'loading'}
                class="w-full py-2.5 bg-xcord-brand hover:bg-xcord-brand-hover text-white rounded-lg font-medium transition disabled:opacity-50"
              >
                {status() === 'loading' ? 'Sending...' : 'Send Message'}
              </button>
            </form>
          </Show>
        </div>
      </div>
    </Show>
  );
}
