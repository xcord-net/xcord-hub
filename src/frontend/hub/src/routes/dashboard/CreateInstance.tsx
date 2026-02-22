import { createSignal, Show } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { instanceStore } from '../../stores/instance.store';

export default function CreateInstance() {
  const navigate = useNavigate();
  const [subdomain, setSubdomain] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [adminPassword, setAdminPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [subdomainStatus, setSubdomainStatus] = createSignal<'idle' | 'checking' | 'available' | 'taken'>('idle');

  let checkTimer: ReturnType<typeof setTimeout>;

  const handleSubdomainInput = (value: string) => {
    const clean = value.toLowerCase().replace(/[^a-z0-9-]/g, '');
    setSubdomain(clean);
    setSubdomainStatus('idle');

    clearTimeout(checkTimer);
    if (clean.length >= 3) {
      setSubdomainStatus('checking');
      checkTimer = setTimeout(async () => {
        try {
          const response = await fetch(`/api/v1/hub/check-subdomain?subdomain=${encodeURIComponent(clean)}`);
          if (response.ok) {
            const data = await response.json();
            setSubdomainStatus(data.available ? 'available' : 'taken');
          } else {
            setSubdomainStatus('idle');
          }
        } catch {
          setSubdomainStatus('idle');
        }
      }, 500);
    }
  };

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');

    if (adminPassword() !== confirmPassword()) {
      setError('Passwords do not match');
      return;
    }
    if (adminPassword().length < 8) {
      setError('Admin password must be at least 8 characters');
      return;
    }

    setLoading(true);
    try {
      const result = await instanceStore.createInstance(
        subdomain(),
        displayName(),
        adminPassword()
      );
      navigate('/dashboard');
    } catch (err: any) {
      setError(err?.message || 'Failed to create instance');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div class="p-8 max-w-xl">
      <h1 class="text-2xl font-bold text-xcord-text-primary mb-2">Create Instance</h1>
      <p class="text-sm text-xcord-text-muted mb-8">
        Launch a new Xcord instance. Choose a subdomain and set up your admin account.
      </p>

      <form onSubmit={handleSubmit} class="space-y-5">
        {/* Subdomain */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Subdomain
          </label>
          <div class="flex items-center">
            <input
              type="text"
              value={subdomain()}
              onInput={(e) => handleSubdomainInput(e.currentTarget.value)}
              class="flex-1 px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded-l border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              placeholder="my-server"
              required
              pattern="[a-z0-9-]+"
              minLength={3}
              disabled={loading()}
            />
            <span class="px-3 py-2 bg-xcord-bg-accent text-xcord-text-muted text-sm rounded-r">
              .xcord-dev.net
            </span>
          </div>
          <Show when={subdomainStatus() === 'checking'}>
            <span class="text-xs text-xcord-text-muted mt-1 block">Checking availability...</span>
          </Show>
          <Show when={subdomainStatus() === 'available'}>
            <span class="text-xs text-xcord-green mt-1 block">Available!</span>
          </Show>
          <Show when={subdomainStatus() === 'taken'}>
            <span class="text-xs text-xcord-red mt-1 block">Already taken</span>
          </Show>
        </div>

        {/* Display Name */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Display Name
          </label>
          <input
            type="text"
            value={displayName()}
            onInput={(e) => setDisplayName(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder="My Awesome Server"
            required
            disabled={loading()}
          />
        </div>

        {/* Admin Password */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Admin Password
          </label>
          <input
            type="password"
            value={adminPassword()}
            onInput={(e) => setAdminPassword(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder="At least 8 characters"
            required
            minLength={8}
            disabled={loading()}
          />
        </div>

        {/* Confirm Password */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Confirm Admin Password
          </label>
          <input
            type="password"
            value={confirmPassword()}
            onInput={(e) => setConfirmPassword(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
            required
            disabled={loading()}
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
          {loading() ? 'Creating...' : 'Create Instance'}
        </button>
      </form>
    </div>
  );
}
