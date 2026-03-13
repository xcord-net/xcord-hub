import { createSignal } from 'solid-js';
import Logo from './Logo';

interface Props {
  onComplete: () => void;
}

export function SetupWizard(props: Props) {
  const [username, setUsername] = createSignal('');
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [error, setError] = createSignal('');
  const [isLoading, setIsLoading] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');

    if (!username().trim()) {
      setError('Username is required');
      return;
    }

    if (!email().trim()) {
      setError('Email is required');
      return;
    }

    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(email())) {
      setError('Invalid email format');
      return;
    }

    if (password().length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    if (password() !== confirmPassword()) {
      setError('Passwords do not match');
      return;
    }

    setIsLoading(true);

    try {
      const res = await fetch('/api/v1/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: username().trim(),
          email: email().trim(),
          password: password(),
        }),
      });

      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        throw new Error(data?.detail || 'Setup failed');
      }

      const data = await res.json();

      // Store the access token and mark as authenticated
      if (data.accessToken) {
        localStorage.setItem('accessToken', data.accessToken);
      }

      props.onComplete();
    } catch (err: any) {
      setError(err?.message || 'Setup failed');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="bg-white p-8 rounded-lg shadow-md w-full max-w-md">
        <h1 class="text-2xl font-bold mb-2 text-center"><Logo /> Hub Admin</h1>
        <p class="text-sm text-gray-500 text-center mb-6">Create your admin account to get started</p>

        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1" for="username">
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username()}
              onInput={(e) => setUsername(e.currentTarget.value)}
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              disabled={isLoading()}
              placeholder="admin"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1" for="email">
              Email
            </label>
            <input
              id="email"
              type="email"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              disabled={isLoading()}
              placeholder="admin@example.com"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1" for="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              value={password()}
              onInput={(e) => setPassword(e.currentTarget.value)}
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              disabled={isLoading()}
              placeholder="At least 8 characters"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1" for="confirm-password">
              Confirm Password
            </label>
            <input
              id="confirm-password"
              type="password"
              value={confirmPassword()}
              onInput={(e) => setConfirmPassword(e.currentTarget.value)}
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              disabled={isLoading()}
            />
          </div>

          {error() && (
            <div class="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
              {error()}
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading()}
            class="w-full bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed"
          >
            {isLoading() ? 'Creating account...' : 'Create Admin Account'}
          </button>
        </form>

        <p class="mt-4 text-xs text-gray-500 text-center">
          First-time setup — this will be the only admin account
        </p>
      </div>
    </div>
  );
}
