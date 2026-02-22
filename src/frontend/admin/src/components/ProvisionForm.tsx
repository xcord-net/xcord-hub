import { createSignal } from 'solid-js';
import { useInstances } from '../stores/instance.store';

interface ProvisionFormProps {
  onCancel: () => void;
  onSuccess: () => void;
}

export function ProvisionForm(props: ProvisionFormProps) {
  const instanceStore = useInstances();
  const [subdomain, setSubdomain] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [adminPassword, setAdminPassword] = createSignal('');
  const [error, setError] = createSignal('');
  const [isLoading, setIsLoading] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      await instanceStore.provisionInstance({
        ownerId: 0,
        domain: `${subdomain()}.xcord-dev.net`,
        displayName: displayName(),
        adminPassword: adminPassword(),
      });
      props.onSuccess();
    } catch (err: any) {
      setError(err?.message || 'Failed to provision instance');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <h2 class="text-xl font-semibold mb-4">Provision New Instance</h2>

      <form onSubmit={handleSubmit} class="space-y-4">
        <div>
          <label class="block text-sm font-medium mb-1" for="subdomain">
            Subdomain
          </label>
          <input
            id="subdomain"
            type="text"
            value={subdomain()}
            onInput={(e) => setSubdomain(e.currentTarget.value)}
            required
            pattern="[a-z0-9-]+"
            class="w-full px-3 py-2 border border-gray-300 rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
            disabled={isLoading()}
            placeholder="my-instance"
          />
          <p class="text-xs text-gray-500 mt-1">Lowercase letters, numbers, and hyphens only. Full domain will be: {subdomain() || 'my-instance'}.xcord-dev.net</p>
        </div>

        <div>
          <label class="block text-sm font-medium mb-1" for="displayName">
            Display Name
          </label>
          <input
            id="displayName"
            type="text"
            value={displayName()}
            onInput={(e) => setDisplayName(e.currentTarget.value)}
            required
            class="w-full px-3 py-2 border border-gray-300 rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
            disabled={isLoading()}
            placeholder="My Awesome Server"
          />
        </div>

        <div>
          <label class="block text-sm font-medium mb-1" for="adminPassword">
            Admin Password
          </label>
          <input
            id="adminPassword"
            type="password"
            value={adminPassword()}
            onInput={(e) => setAdminPassword(e.currentTarget.value)}
            required
            class="w-full px-3 py-2 border border-gray-300 rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
            disabled={isLoading()}
            placeholder="Enter admin password"
          />
        </div>

        {error() && (
          <div class="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded text-sm">
            {error()}
          </div>
        )}

        <div class="flex gap-3 pt-2">
          <button
            type="submit"
            disabled={isLoading()}
            class="flex-1 bg-blue-600 text-white py-2 px-4 rounded hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed"
          >
            {isLoading() ? 'Provisioning...' : 'Provision Instance'}
          </button>
          <button
            type="button"
            onClick={props.onCancel}
            disabled={isLoading()}
            class="flex-1 bg-gray-200 text-gray-700 py-2 px-4 rounded hover:bg-gray-300 disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
