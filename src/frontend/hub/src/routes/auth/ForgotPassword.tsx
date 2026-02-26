import { createSignal, Show } from 'solid-js';
import { A } from '@solidjs/router';
import PageMeta from '../../components/PageMeta';

export default function ForgotPassword() {
  const [email, setEmail] = createSignal('');
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [submitted, setSubmitted] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      // Always returns 204 — no information leakage about whether the email exists
      await fetch('/api/v1/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: email() }),
      });
      setSubmitted(true);
    } catch {
      setError('Network error. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div class="min-h-screen bg-xcord-bg-primary flex items-center justify-center px-4">
      <PageMeta
        title="Forgot Password - Xcord Hub"
        description="Reset your Xcord Hub account password."
        path="/forgot-password"
        noindex
      />
      <div class="w-full max-w-md bg-xcord-bg-secondary rounded-lg shadow-lg p-8">
        <div class="text-center mb-8">
          <A href="/" class="text-2xl font-bold text-white">xcord</A>
          <h1 class="text-xl text-xcord-text-primary mt-4">Forgot your password?</h1>
          <p class="text-sm text-xcord-text-muted mt-1">
            Enter your email and we'll send you a reset link.
          </p>
        </div>

        <Show when={submitted()}>
          <div class="text-sm text-xcord-text-primary bg-xcord-bg-tertiary border border-xcord-brand/30 rounded p-3 mb-4">
            If an account with that email exists, you'll receive a reset link shortly.
          </div>
          <p class="text-sm text-xcord-text-muted mt-4">
            <A href="/login" class="text-xcord-text-link hover:underline">Back to login</A>
          </p>
        </Show>

        <Show when={!submitted()}>
          <form onSubmit={handleSubmit} class="space-y-4">
            <div>
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
                Email
              </label>
              <input
                id="forgot-email"
                type="email"
                value={email()}
                onInput={(e) => setEmail(e.currentTarget.value)}
                class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
                required
              />
            </div>

            <Show when={error()}>
              <div class="text-sm text-xcord-red">{error()}</div>
            </Show>

            <button
              type="submit"
              disabled={loading()}
              class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
            >
              {loading() ? 'Sending...' : 'Send Reset Link'}
            </button>
          </form>

          <p class="text-sm text-xcord-text-muted mt-4">
            <A href="/login" class="text-xcord-text-link hover:underline">Back to login</A>
          </p>
        </Show>
      </div>
    </div>
  );
}
