import { createSignal, Show, onMount } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { instanceStore } from '../../stores/instance.store';
import Captcha from '../../components/Captcha';
import PasswordStrength from '../../components/PasswordStrength';
import ContactModal from '../../components/ContactModal';
import PageMeta from '../../components/PageMeta';

type Tier = 'Free' | 'Basic' | 'Pro' | 'Enterprise';

const TIER_PRICE_CENTS: Record<Tier, number> = {
  Free: 0,
  Basic: 6000,
  Pro: 15000,
  Enterprise: 30000,
};

const TIER_MEDIA_CENTS: Record<Tier, number> = {
  Free: 400,
  Basic: 300,
  Pro: 200,
  Enterprise: 100,
};

const TIER_MAX_USERS: Record<Tier, number> = {
  Free: 10,
  Basic: 50,
  Pro: 200,
  Enterprise: 500,
};

function formatPriceSummary(tier: Tier, mediaEnabled: boolean): string {
  const base = TIER_PRICE_CENTS[tier];
  const maxUsers = TIER_MAX_USERS[tier];
  const mediaCents = mediaEnabled ? TIER_MEDIA_CENTS[tier] * maxUsers : 0;
  const total = base + mediaCents;
  if (total === 0) return 'Free';
  const dollars = total / 100;
  return `$${dollars % 1 === 0 ? dollars : dollars.toFixed(2)}/mo`;
}

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
  const [captchaId, setCaptchaId] = createSignal('');
  const [captchaAnswer, setCaptchaAnswer] = createSignal('');
  const [showContact, setShowContact] = createSignal(false);
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [notifyMessage, setNotifyMessage] = createSignal('');
  const [hasExistingInstance, setHasExistingInstance] = createSignal(false);
  const [checkingInstances, setCheckingInstances] = createSignal(true);
  const [paymentsEnabled, setPaymentsEnabled] = createSignal(false);
  const [selectedTier, setSelectedTier] = createSignal<Tier>('Free');
  const [mediaEnabled, setMediaEnabled] = createSignal(false);

  onMount(async () => {
    try {
      const token = localStorage.getItem('xcord_hub_token');
      const [billingRes, featuresRes] = await Promise.all([
        fetch('/api/v1/hub/billing', {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        }),
        fetch('/api/v1/hub/features'),
      ]);
      if (billingRes.ok) {
        const data = await billingRes.json();
        if (data.instances && data.instances.length > 0) {
          setHasExistingInstance(true);
        }
      }
      if (featuresRes.ok) {
        const data = await featuresRes.json();
        setPaymentsEnabled(data.paymentsEnabled ?? false);
      }
    } catch {
      // If we can't check, allow form to show - backend will enforce
    } finally {
      setCheckingInstances(false);
    }
  });

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
      await instanceStore.createInstance(
        subdomain(),
        displayName(),
        adminPassword(),
        selectedTier(),
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

  const handleNotify = async (e: Event) => {
    e.preventDefault();
    const tier = notifyTier();
    if (!tier) return;
    setNotifyStatus('loading');
    try {
      const res = await fetch('/api/v1/mailing-list', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: notifyEmail(), tier }),
      });
      const data = await res.json();
      if (!res.ok) {
        setNotifyStatus('error');
        setNotifyMessage(data.message ?? 'Something went wrong.');
      } else {
        setNotifyStatus('success');
        setNotifyMessage(data.message);
        setTimeout(() => {
          setNotifyTier(null);
          setNotifyEmail('');
          setNotifyStatus('idle');
          setNotifyMessage('');
        }, 3000);
      }
    } catch {
      setNotifyStatus('error');
      setNotifyMessage('Network error. Please try again.');
    }
  };

  const tierButtonClass = (tier: Tier) => {
    const isSelected = selectedTier() === tier;
    return `px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition${isSelected ? ' ring-2 ring-xcord-brand' : ''}`;
  };

  return (
    <>
      <PageMeta
        title="Create Server - Xcord"
        description="Create a new Xcord server."
        path="/dashboard/create"
        noindex
      />
      <div class="p-8 max-w-xl">
      <h1 class="text-2xl font-bold text-xcord-text-primary mb-2">Create Instance</h1>

      <Show when={checkingInstances()}>
        <p class="text-sm text-xcord-text-muted">Loading...</p>
      </Show>

      <Show when={!checkingInstances() && hasExistingInstance()}>
        <div class="bg-xcord-bg-secondary rounded-lg p-6 text-center">
          <p class="text-sm text-xcord-text-muted mb-2">You already have an instance.</p>
          <a href="/dashboard" class="text-sm text-xcord-text-link hover:underline">Go to dashboard</a>
        </div>
      </Show>

      <Show when={!checkingInstances() && !hasExistingInstance()}>
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
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Plan</label>
            <p class="text-xs text-xcord-text-muted mb-2">Tier</p>
            <div class="grid grid-cols-4 gap-2 mb-4">
              {/* Free - always selectable */}
              <button
                type="button"
                disabled={loading()}
                onClick={() => setSelectedTier('Free')}
                class={tierButtonClass('Free')}
              >
                <div class="font-semibold">Free</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 10 users</div>
              </button>

              {/* Basic */}
              <Show
                when={paymentsEnabled()}
                fallback={
                  <button
                    type="button"
                    onClick={() => { setNotifyTier('Basic'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                    disabled={loading()}
                    class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition"
                  >
                    <div class="font-semibold">Basic</div>
                    <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button
                  type="button"
                  disabled={loading()}
                  onClick={() => setSelectedTier('Basic')}
                  class={tierButtonClass('Basic')}
                >
                  <div class="font-semibold">Basic</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$60/mo</div>
                </button>
              </Show>

              {/* Pro */}
              <Show
                when={paymentsEnabled()}
                fallback={
                  <button
                    type="button"
                    onClick={() => { setNotifyTier('Pro'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                    disabled={loading()}
                    class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition"
                  >
                    <div class="font-semibold">Pro</div>
                    <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button
                  type="button"
                  disabled={loading()}
                  onClick={() => setSelectedTier('Pro')}
                  class={tierButtonClass('Pro')}
                >
                  <div class="font-semibold">Pro</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$150/mo</div>
                </button>
              </Show>

              {/* Enterprise - always contact us */}
              <button
                type="button"
                onClick={() => setShowContact(true)}
                class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition"
              >
                <div class="font-semibold">Enterprise</div>
                <div class="text-xs text-xcord-text-muted mt-1">500+ users</div>
                <div class="text-xs text-xcord-brand mt-1">Contact us</div>
              </button>
            </div>

            {/* Voice & video */}
            <Show
              when={paymentsEnabled()}
              fallback={
                <div class="flex items-center gap-3 mb-4">
                  <span class="text-sm text-xcord-text-primary">Voice &amp; video</span>
                  <button
                    type="button"
                    onClick={() => { setNotifyTier('Voice & Video'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                    class="text-xs text-xcord-brand hover:underline"
                  >
                    Notify me
                  </button>
                </div>
              }
            >
              <div class="flex items-center gap-3 mb-4">
                <span class="text-sm text-xcord-text-primary">Voice &amp; video</span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={mediaEnabled()}
                  onClick={() => setMediaEnabled(v => !v)}
                  disabled={loading()}
                  class={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-xcord-brand ${mediaEnabled() ? 'bg-xcord-brand' : 'bg-xcord-bg-accent'}`}
                >
                  <span
                    class={`inline-block h-3 w-3 rounded-full bg-white transition-transform ${mediaEnabled() ? 'translate-x-5' : 'translate-x-1'}`}
                  />
                </button>
              </div>
            </Show>

            {/* Price summary */}
            <div class="px-3 py-2 bg-xcord-bg-accent rounded">
              <div class="flex items-center justify-between">
                <span class="text-xs font-medium text-xcord-text-primary">Total</span>
                <span class="text-sm font-bold text-xcord-text-primary">
                  {formatPriceSummary(selectedTier(), mediaEnabled())}
                </span>
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

          <Captcha onSolved={(id, ans) => { setCaptchaId(id); setCaptchaAnswer(ans); }} />

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
      </Show>

      {/* Notify-me modal - always rendered outside the form Show */}
      <Show when={notifyTier()}>
        <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4" onClick={(e) => { if (e.target === e.currentTarget) setNotifyTier(null); }}>
          <div class="bg-xcord-bg-primary rounded-xl w-full max-w-sm p-6">
            <div class="flex items-center justify-between mb-4">
              <h3 class="text-lg font-bold text-xcord-text-primary">{notifyTier()}</h3>
              <button onClick={() => setNotifyTier(null)} class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none">&times;</button>
            </div>
            <Show when={notifyStatus() !== 'success'} fallback={<p class="text-sm text-xcord-green py-4 text-center">{notifyMessage()}</p>}>
              <p class="text-sm text-xcord-text-muted mb-4">We'll let you know when {notifyTier()} is available.</p>
              <form onSubmit={handleNotify} class="space-y-3">
                <input type="email" required placeholder="you@example.com" value={notifyEmail()} onInput={(e) => setNotifyEmail(e.currentTarget.value)} class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand text-sm" />
                <button type="submit" disabled={notifyStatus() === 'loading'} class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition text-sm">
                  {notifyStatus() === 'loading' ? 'Submitting...' : 'Notify Me'}
                </button>
              </form>
              <Show when={notifyStatus() === 'error'}>
                <p class="text-xs text-xcord-red mt-2">{notifyMessage()}</p>
              </Show>
            </Show>
          </div>
        </div>
      </Show>

      <ContactModal open={showContact()} onClose={() => setShowContact(false)} />
    </div>
    </>
  );
}
