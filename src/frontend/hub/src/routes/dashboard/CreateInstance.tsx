import { createSignal, Show, For } from 'solid-js';
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
  const [featureTier, setFeatureTier] = createSignal('Chat');
  const [userCountTier, setUserCountTier] = createSignal('Tier10');

  // Prices in cents — must match backend TierDefaults.GetPriceCents
  const priceMatrixCents: Record<string, Record<string, number>> = {
    'Chat':  { 'Tier10': 0,    'Tier50': 2000, 'Tier100': 6000,  'Tier500': 20000 },
    'Audio': { 'Tier10': 2000, 'Tier50': 4500, 'Tier100': 11000, 'Tier500': 40000 },
    'Video': { 'Tier10': 4000, 'Tier50': 7000, 'Tier100': 16000, 'Tier500': 55000 },
    'HD':    { 'Tier10': 6500, 'Tier50': 12000, 'Tier100': 23500, 'Tier500': 70000 },
  };
  const formatPrice = (cents: number) => {
    if (cents === 0) return 'Free';
    const dollars = cents / 100;
    return dollars % 1 === 0 ? `$${dollars}/mo` : `$${dollars.toFixed(2)}/mo`;
  };
  const selectedPrice = () => formatPrice(priceMatrixCents[featureTier()]?.[userCountTier()] ?? 0);

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
      const isHd = featureTier() === 'HD';
      const result = await instanceStore.createInstance(
        subdomain(),
        displayName(),
        adminPassword(),
        isHd ? 'Video' : featureTier(),
        userCountTier(),
        isHd
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

        {/* Plan */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Plan
          </label>

          {/* Feature tier */}
          <p class="text-xs text-xcord-text-muted mb-2">Features</p>
          <div class="grid grid-cols-4 gap-2 mb-4">
            <For each={[
              { key: 'Chat', label: 'Chat', desc: 'Text only' },
              { key: 'Audio', label: '+Audio', desc: 'Text + voice' },
              { key: 'Video', label: '+Video', desc: 'Text + voice + video' },
              { key: 'HD', label: '+HD', desc: '1080p + recording' },
            ]}>
              {(tier) => (
                <button
                  type="button"
                  onClick={() => setFeatureTier(tier.key)}
                  disabled={loading()}
                  class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center transition ${featureTier() === tier.key ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'}`}
                >
                  <div class="font-semibold">{tier.label}</div>
                  <div class="text-xs text-xcord-text-muted mt-1">{tier.desc}</div>
                </button>
              )}
            </For>
          </div>

          {/* User count tier */}
          <p class="text-xs text-xcord-text-muted mb-2">Users</p>
          <div class="flex gap-2 mb-4">
            <For each={[['Tier10', '10'], ['Tier50', '50'], ['Tier100', '100'], ['Tier500', '500']]}>
              {([value, label]) => (
                <button
                  type="button"
                  onClick={() => setUserCountTier(value)}
                  disabled={loading()}
                  class={`px-4 py-1.5 rounded-full text-sm font-medium transition ${userCountTier() === value ? 'ring-2 ring-xcord-brand bg-xcord-bg-tertiary text-xcord-text-primary' : 'bg-xcord-bg-tertiary text-xcord-text-muted hover:text-xcord-text-primary'}`}
                >
                  {label}
                </button>
              )}
            </For>
          </div>

          {/* Price display */}
          <div class="flex items-center justify-between px-3 py-2 bg-xcord-bg-accent rounded">
            <span class="text-xs text-xcord-text-muted">Selected plan</span>
            <span class="text-sm font-bold text-xcord-text-primary">{selectedPrice()}</span>
          </div>
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
