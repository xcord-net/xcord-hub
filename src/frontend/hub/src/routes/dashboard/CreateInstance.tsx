import { createSignal, Show, For } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { instanceStore } from '../../stores/instance.store';
import Captcha from '../../components/Captcha';
import PasswordStrength from '../../components/PasswordStrength';

export default function CreateInstance() {
  const navigate = useNavigate();
  const [subdomain, setSubdomain] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [adminPassword, setAdminPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [subdomainStatus, setSubdomainStatus] = createSignal<'idle' | 'checking' | 'available' | 'taken'>('idle');
  const [subdomainReason, setSubdomainReason] = createSignal('');
  const [tier, setTier] = createSignal('Free');
  const [mediaEnabled, setMediaEnabled] = createSignal(false);
  const [captchaId, setCaptchaId] = createSignal('');
  const [captchaAnswer, setCaptchaAnswer] = createSignal('');

  // Prices in cents - must match backend TierDefaults
  const TIER_CONFIG: Record<string, { baseCents: number; mediaPerUserCents: number; maxUsers: number }> = {
    Free:       { baseCents: 0,     mediaPerUserCents: 400, maxUsers: 10 },
    Basic:      { baseCents: 6000,  mediaPerUserCents: 300, maxUsers: 50 },
    Pro:        { baseCents: 15000, mediaPerUserCents: 200, maxUsers: 200 },
    Enterprise: { baseCents: 30000, mediaPerUserCents: 100, maxUsers: 500 },
  };
  const formatPrice = (cents: number) => {
    if (cents === 0) return 'Free';
    const dollars = cents / 100;
    return dollars % 1 === 0 ? `$${dollars}/mo` : `$${dollars.toFixed(2)}/mo`;
  };
  const selectedPrice = () => {
    const config = TIER_CONFIG[tier()];
    if (!config) return 'Free';
    return formatPrice(config.baseCents);
  };
  const mediaPrice = () => {
    const config = TIER_CONFIG[tier()];
    if (!config) return '';
    const dollars = config.mediaPerUserCents / 100;
    return dollars % 1 === 0 ? `$${dollars}` : `$${dollars.toFixed(2)}`;
  };
  const totalPriceCents = () => {
    const config = TIER_CONFIG[tier()];
    if (!config) return 0;
    return config.baseCents + (mediaEnabled() ? config.mediaPerUserCents * config.maxUsers : 0);
  };
  const totalPrice = () => formatPrice(totalPriceCents());

  // Must match backend ValidationHelpers.ReservedSubdomains
  const RESERVED = new Set([
    'www', 'mail', 'smtp', 'imap', 'pop', 'ftp',
    'docker', 'registry',
    'api', 'admin', 'hub', 'auth',
    'ns1', 'ns2', 'ns3', 'ns4',
    'caddy', 'proxy', 'lb',
    'pg', 'postgres', 'redis', 'minio', 's3',
    'livekit', 'rtc', 'turn', 'stun',
    'status', 'monitor', 'grafana', 'prometheus',
    '_dmarc', 'autoconfig', 'autodiscover',
  ]);

  const subdomainError = () => {
    const s = subdomain();
    if (!s) return '';
    if (s.length < 6) return 'Must be at least 6 characters';
    if (s.startsWith('-') || s.endsWith('-')) return 'Cannot start or end with a hyphen';
    if (s.includes('--')) return 'Cannot contain consecutive hyphens';
    if (RESERVED.has(s)) return `'${s}' is reserved for infrastructure use`;
    if (subdomainStatus() === 'taken') return subdomainReason() || 'Already taken';
    return '';
  };

  const subdomainValid = () => subdomain().length >= 6 && !subdomainError();

  let checkTimer: ReturnType<typeof setTimeout>;

  const handleSubdomainInput = (value: string) => {
    const clean = value.toLowerCase().replace(/[^a-z0-9-]/g, '');
    setSubdomain(clean);
    setSubdomainStatus('idle');
    setSubdomainReason('');

    clearTimeout(checkTimer);
    // Only call API if client-side checks pass
    if (clean.length >= 6 && !RESERVED.has(clean) && !clean.startsWith('-') && !clean.endsWith('-') && !clean.includes('--')) {
      setSubdomainStatus('checking');
      checkTimer = setTimeout(async () => {
        try {
          const token = localStorage.getItem('xcord_hub_token');
          const response = await fetch(`/api/v1/hub/check-subdomain?subdomain=${encodeURIComponent(clean)}`, {
            headers: token ? { Authorization: `Bearer ${token}` } : {},
          });
          if (response.ok) {
            const data = await response.json();
            setSubdomainStatus(data.available ? 'available' : 'taken');
            setSubdomainReason(data.reason ?? '');
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
        adminPassword(),
        tier(),
        mediaEnabled(),
        captchaId(),
        captchaAnswer()
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
              class={`flex-1 px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded-l border-none outline-none focus:ring-2 ${
                subdomainError() ? 'ring-2 ring-xcord-red focus:ring-xcord-red' : 'focus:ring-xcord-brand'
              }`}
              placeholder="my-server"
              autocomplete="off"
              required
              pattern="[a-z0-9\-]+"
              minLength={6}
              disabled={loading()}
            />
            <span class="px-3 py-2 bg-xcord-bg-accent text-xcord-text-muted text-sm rounded-r">
              .xcord-dev.net
            </span>
          </div>
          <Show when={subdomainStatus() === 'checking' && !subdomainError()}>
            <span class="text-xs text-xcord-text-muted mt-1 block">Checking availability...</span>
          </Show>
          <Show when={subdomain().length > 0 && subdomainError()}>
            <span class="text-xs text-xcord-red mt-1 block">{subdomainError()}</span>
          </Show>
          <Show when={subdomainValid() && subdomainStatus() === 'available'}>
            <span class="text-xs text-xcord-green mt-1 block">Available!</span>
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
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder="My Awesome Server"
            autocomplete="off"
            required
            disabled={loading()}
          />
        </div>

        {/* Plan */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
            Plan
          </label>

          {/* Tier selector */}
          <p class="text-xs text-xcord-text-muted mb-2">Tier</p>
          <div class="grid grid-cols-4 gap-2 mb-4">
            <For each={[
              { key: 'Free',       label: 'Free',       desc: 'Up to 10 users' },
              { key: 'Basic',      label: 'Basic',      desc: 'Up to 50 users' },
              { key: 'Pro',        label: 'Pro',        desc: 'Up to 200 users' },
              { key: 'Enterprise', label: 'Enterprise', desc: 'Up to 500 users' },
            ]}>
              {(t) => (
                <button
                  type="button"
                  onClick={() => setTier(t.key)}
                  disabled={loading()}
                  class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center transition ${tier() === t.key ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'}`}
                >
                  <div class="font-semibold">{t.label}</div>
                  <div class="text-xs text-xcord-text-muted mt-1">{t.desc}</div>
                </button>
              )}
            </For>
          </div>

          {/* Media toggle */}
          <div class="flex items-center gap-3 mb-4">
            <input
              id="mediaEnabled"
              type="checkbox"
              checked={mediaEnabled()}
              onChange={(e) => setMediaEnabled(e.currentTarget.checked)}
              disabled={loading()}
              class="h-4 w-4 rounded border-xcord-bg-accent text-xcord-brand focus:ring-xcord-brand"
            />
            <label for="mediaEnabled" class="text-sm text-xcord-text-primary cursor-pointer">
              Enable voice & video ({mediaPrice()} per user)
            </label>
          </div>

          {/* Price summary */}
          <div class="px-3 py-2 bg-xcord-bg-accent rounded space-y-1">
            <div class="flex items-center justify-between">
              <span class="text-xs text-xcord-text-muted">Base ({tier()})</span>
              <span class="text-xs text-xcord-text-secondary">{selectedPrice()}</span>
            </div>
            <Show when={mediaEnabled()}>
              <div class="flex items-center justify-between">
                <span class="text-xs text-xcord-text-muted">Voice & video ({mediaPrice()} x {TIER_CONFIG[tier()].maxUsers} users)</span>
                <span class="text-xs text-xcord-text-secondary">{formatPrice(TIER_CONFIG[tier()].mediaPerUserCents * TIER_CONFIG[tier()].maxUsers)}</span>
              </div>
            </Show>
            <div class="flex items-center justify-between border-t border-xcord-bg-tertiary pt-1">
              <span class="text-xs font-medium text-xcord-text-primary">Total</span>
              <span class="text-sm font-bold text-xcord-text-primary">{totalPrice()}</span>
            </div>
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
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder="At least 8 characters"
            autocomplete="new-password"
            required
            minLength={8}
            disabled={loading()}
          />
          <PasswordStrength password={adminPassword()} />
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
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            autocomplete="new-password"
            required
            disabled={loading()}
          />
        </div>

        <Show when={tier() === 'Free' && !mediaEnabled()}>
          <Captcha onSolved={(id, ans) => { setCaptchaId(id); setCaptchaAnswer(ans); }} />
        </Show>

        <Show when={error()}>
          <div class="text-sm text-xcord-red">{error()}</div>
        </Show>

        <button
          type="submit"
          disabled={loading() || !!subdomainError() || !subdomain()}
          class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
        >
          {loading() ? 'Creating...' : 'Create Instance'}
        </button>
      </form>
    </div>
  );
}
