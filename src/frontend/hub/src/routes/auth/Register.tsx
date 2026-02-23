import { createSignal, Show } from 'solid-js';
import { A, useNavigate } from '@solidjs/router';
import { useAuth } from '../../stores/auth.store';
import PasswordStrength from '../../components/PasswordStrength';
import Logo from '../../components/Logo';

export default function Register() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = createSignal('');
  const [username, setUsername] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [agreed, setAgreed] = createSignal(false);
  const [loading, setLoading] = createSignal(false);
  const [localError, setLocalError] = createSignal('');

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setLocalError('');
    auth.clearError();

    if (password() !== confirmPassword()) {
      setLocalError('Passwords do not match');
      return;
    }
    if (password().length < 8) {
      setLocalError('Password must be at least 8 characters');
      return;
    }
    if (!agreed()) {
      setLocalError('You must agree to the terms');
      return;
    }

    setLoading(true);
    const success = await auth.signup(email(), password(), displayName() || username(), username());
    setLoading(false);
    if (success) {
      navigate('/dashboard', { replace: true });
    }
  };

  const displayError = () => localError() || auth.error;

  return (
    <div class="min-h-screen bg-xcord-bg-primary flex items-center justify-center px-4">
      <div class="w-full max-w-md bg-xcord-bg-secondary rounded-lg shadow-lg p-8">
        <div class="text-center mb-8">
          <A href="/" class="text-2xl font-bold text-white"><Logo /></A>
          <h1 class="text-xl text-xcord-text-primary mt-4">Create an account</h1>
        </div>

        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Email</label>
            <input
              id="hub-reg-email"
              type="email"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>

          <div>
            <label for="hub-reg-username" class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Username</label>
            <input
              id="hub-reg-username"
              type="text"
              value={username()}
              onInput={(e) => setUsername(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>

          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Display Name</label>
            <input
              type="text"
              value={displayName()}
              onInput={(e) => setDisplayName(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              placeholder={username() || 'Optional'}
            />
          </div>

          <div>
            <label for="hub-reg-password" class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Password</label>
            <input
              id="hub-reg-password"
              type="password"
              value={password()}
              onInput={(e) => setPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
              minLength={8}
            />
            <PasswordStrength password={password()} />
          </div>

          <div>
            <label for="hub-reg-confirm-password" class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Confirm Password</label>
            <input
              id="hub-reg-confirm-password"
              type="password"
              value={confirmPassword()}
              onInput={(e) => setConfirmPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>

          <label class="flex items-start gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={agreed()}
              onChange={(e) => setAgreed(e.currentTarget.checked)}
              class="mt-1 accent-xcord-brand"
            />
            <span class="text-xs text-xcord-text-muted">
              I agree to the <a href="#" class="text-xcord-text-link hover:underline">Terms of Service</a> and{' '}
              <a href="#" class="text-xcord-text-link hover:underline">Privacy Policy</a>
            </span>
          </label>

          <Show when={displayError()}>
            <div class="text-sm text-xcord-red">{displayError()}</div>
          </Show>

          <button
            type="submit"
            disabled={loading()}
            class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
          >
            {loading() ? 'Creating account...' : 'Continue'}
          </button>
        </form>

        <p class="text-sm text-xcord-text-muted mt-4">
          Already have an account?{' '}
          <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
        </p>
      </div>
    </div>
  );
}
