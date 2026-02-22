import { createSignal, Show } from 'solid-js';
import { A, useNavigate, useSearchParams } from '@solidjs/router';
import PasswordStrength from '../../components/PasswordStrength';

export default function ResetPassword() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [password, setPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [success, setSuccess] = createSignal(false);

  const token = () => searchParams.token ?? '';

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');

    if (!token()) {
      setError('Invalid or missing reset token. Please request a new password reset.');
      return;
    }

    if (password() !== confirmPassword()) {
      setError('Passwords do not match');
      return;
    }

    if (password().length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    setLoading(true);
    try {
      const response = await fetch('/api/v1/auth/reset-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: token(), newPassword: password() }),
      });

      if (!response.ok) {
        const data = await response.json().catch(() => null);
        setError(data?.detail || data?.message || 'Failed to reset password. The link may have expired.');
      } else {
        setSuccess(true);
        setTimeout(() => navigate('/login', { replace: true }), 3000);
      }
    } catch {
      setError('Network error. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div class="min-h-screen bg-xcord-bg-primary flex items-center justify-center px-4">
      <div class="w-full max-w-md bg-xcord-bg-secondary rounded-lg shadow-lg p-8">
        <div class="text-center mb-8">
          <A href="/" class="text-2xl font-bold text-white">xcord</A>
          <h1 class="text-xl text-xcord-text-primary mt-4">Set a new password</h1>
          <p class="text-sm text-xcord-text-muted mt-1">Enter a new password for your account.</p>
        </div>

        <Show when={success()}>
          <div class="text-sm text-green-400 bg-green-900/20 border border-green-800 rounded p-3 mb-4">
            Password reset successfully! Redirecting to login...
          </div>
        </Show>

        <Show when={!success()}>
          <form onSubmit={handleSubmit} class="space-y-4">
            <div>
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
                New Password
              </label>
              <input
                id="reset-new-password"
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
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
                Confirm New Password
              </label>
              <input
                id="reset-confirm-password"
                type="password"
                value={confirmPassword()}
                onInput={(e) => setConfirmPassword(e.currentTarget.value)}
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
              {loading() ? 'Resetting...' : 'Reset Password'}
            </button>
          </form>
        </Show>

        <p class="text-sm text-xcord-text-muted mt-4">
          Remember your password?{' '}
          <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
        </p>
      </div>
    </div>
  );
}
