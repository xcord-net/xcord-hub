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
  const [totpCode, setTotpCode] = createSignal('');
  const [loading, setLoading] = createSignal(false);
  const [needs2FA, setNeeds2FA] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    auth.clearError();
    setLoading(true);

    if (needs2FA()) {
      const success = await auth.loginWith2FA(email(), password(), totpCode());
      setLoading(false);
      if (success) {
        navigate('/dashboard', { replace: true });
      }
    } else {
      const result = await auth.login(email(), password());
      setLoading(false);
      if (result === '2fa_required') {
        setNeeds2FA(true);
      } else if (result === 'success') {
        navigate('/dashboard', { replace: true });
      }
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
          <p class="text-sm text-xcord-text-muted mt-1">
            {needs2FA() ? 'Enter the 6-digit code from your authenticator app.' : "We're so excited to see you again!"}
          </p>
        </div>

        <form onSubmit={handleSubmit} class="space-y-4">
          <Show when={!needs2FA()}>
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
          </Show>

          <Show when={needs2FA()}>
            <div>
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
                Authentication Code
              </label>
              <input
                id="hub-login-totp"
                type="text"
                inputMode="numeric"
                autocomplete="one-time-code"
                maxLength={6}
                value={totpCode()}
                onInput={(e) => setTotpCode(e.currentTarget.value.replace(/\D/g, '').slice(0, 6))}
                class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand text-center text-lg tracking-widest"
                placeholder="000000"
                required
              />
            </div>
          </Show>

          <Show when={auth.error}>
            <div class="text-sm text-xcord-red">{auth.error}</div>
          </Show>

          <button
            type="submit"
            disabled={loading()}
            class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
          >
            {loading() ? 'Logging in...' : needs2FA() ? 'Verify' : 'Log In'}
          </button>

          <Show when={needs2FA()}>
            <button
              type="button"
              onClick={() => {
                setNeeds2FA(false);
                setTotpCode('');
                auth.clearError();
              }}
              class="w-full py-2 text-xcord-text-muted hover:text-xcord-text-primary text-sm transition"
            >
              Back to login
            </button>
          </Show>
        </form>

        <Show when={!needs2FA()}>
          <p class="text-sm text-xcord-text-muted mt-4">
            Need an account?{' '}
            <A href="/register" class="text-xcord-text-link hover:underline">Register</A>
          </p>
        </Show>
      </div>
    </div>
  );
}
