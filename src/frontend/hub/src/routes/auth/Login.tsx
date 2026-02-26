import { createSignal, Show } from 'solid-js';
import { A, useNavigate } from '@solidjs/router';
import { useAuth } from '../../stores/auth.store';
import Logo from '../../components/Logo';
import PageMeta from '../../components/PageMeta';

export default function Login() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [loading, setLoading] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    auth.clearError();
    setLoading(true);
    const success = await auth.login(email(), password());
    setLoading(false);
    if (success) {
      navigate('/dashboard', { replace: true });
    }
  };

  return (
    <div class="min-h-screen bg-xcord-bg-primary flex items-center justify-center px-4">
      <PageMeta
        title="Log In - Xcord Hub"
        description="Log in to your Xcord Hub account."
        path="/login"
        noindex
      />
      <div class="w-full max-w-md bg-xcord-bg-secondary rounded-lg shadow-lg p-8">
        <div class="text-center mb-8">
          <A href="/" class="text-2xl font-bold text-white"><Logo /></A>
          <h1 class="text-xl text-xcord-text-primary mt-4">Welcome back!</h1>
          <p class="text-sm text-xcord-text-muted mt-1">We're so excited to see you again!</p>
        </div>

        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
              Email
            </label>
            <input
              id="hub-login-email"
              type="email"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>

          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
              Password
            </label>
            <input
              id="hub-login-password"
              type="password"
              value={password()}
              onInput={(e) => setPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
            <A href="/forgot-password" class="text-xs text-xcord-text-link hover:underline mt-1 inline-block">
              Forgot your password?
            </A>
          </div>

          <Show when={auth.error}>
            <div class="text-sm text-xcord-red">{auth.error}</div>
          </Show>

          <button
            type="submit"
            disabled={loading()}
            class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
          >
            {loading() ? 'Logging in...' : 'Log In'}
          </button>
        </form>

        <p class="text-sm text-xcord-text-muted mt-4">
          Need an account?{' '}
          <A href="/register" class="text-xcord-text-link hover:underline">Register</A>
        </p>
      </div>
    </div>
  );
}
