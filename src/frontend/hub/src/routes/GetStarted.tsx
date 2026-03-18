import { createSignal, Show, onMount } from 'solid-js';
import { A, useNavigate, useSearchParams } from '@solidjs/router';
import { useAuth } from '../stores/auth.store';
import { instanceStore } from '../stores/instance.store';
import Captcha from '../components/Captcha';
import PasswordStrength from '../components/PasswordStrength';
import ContactModal from '../components/ContactModal';
import Logo from '../components/Logo';
import PageMeta from '../components/PageMeta';

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

export default function GetStarted() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  // Preselect tier from query param (e.g. /get-started?tier=Basic)
  const initialTier = (['Free', 'Basic', 'Pro'] as const).includes(searchParams.tier as any)
    ? (searchParams.tier as 'Free' | 'Basic' | 'Pro')
    : 'Free';

  // Wizard step (1 = config, 2 = account)
  const [step, setStep] = createSignal(1);
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [ready, setReady] = createSignal(false);

  // Features
  const [paymentsEnabled, setPaymentsEnabled] = createSignal(false);

  // Step 1 - Instance config
  const [subdomain, setSubdomain] = createSignal('');
  const [serverName, setServerName] = createSignal('');
  const [selectedTier, setSelectedTier] = createSignal<'Free' | 'Basic' | 'Pro'>(initialTier);
  const [mediaEnabled, setMediaEnabled] = createSignal(false);
  const [subdomainStatus, setSubdomainStatus] = createSignal<'idle' | 'checking' | 'available' | 'taken'>('idle');
  const [subdomainReason, setSubdomainReason] = createSignal('');

  // Step 2 - Account
  const [email, setEmail] = createSignal('');
  const [username, setUsername] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [agreed, setAgreed] = createSignal(false);
  const [ageConfirmed, setAgeConfirmed] = createSignal(false);
  const [jurisdictionConfirmed, setJurisdictionConfirmed] = createSignal(false);
  const [captchaId, setCaptchaId] = createSignal('');
  const [captchaAnswer, setCaptchaAnswer] = createSignal('');

  // Modals
  const [showContact, setShowContact] = createSignal(false);
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [notifyMessage, setNotifyMessage] = createSignal('');

  // On mount: fetch features + check auth
  onMount(async () => {
    try {
      const featRes = await fetch('/api/v1/hub/features');
      if (featRes.ok) {
        const feat = await featRes.json();
        setPaymentsEnabled(feat.paymentsEnabled);
      }
    } catch { /* default to false */ }

    const restored = await auth.restoreSession();
    if (restored) {
      // Check if user already has an instance
      try {
        const token = localStorage.getItem('xcord_hub_token');
        const res = await fetch('/api/v1/hub/billing', {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });
        if (res.ok) {
          const data = await res.json();
          if (data.instances && data.instances.length > 0) {
            navigate('/dashboard', { replace: true });
            return;
          }
        }
      } catch {
        // If check fails, allow the form - backend enforces limits
      }
    }
    setReady(true);
  });

  const isLoggedIn = () => auth.isAuthenticated;
  const totalSteps = () => isLoggedIn() ? 1 : 2;

  // Subdomain validation
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

  const canProceedStep1 = () => subdomainValid() && subdomainStatus() === 'available' && serverName().trim().length > 0;

  const handleNext = () => {
    if (isLoggedIn()) {
      handleSubmitLoggedIn();
    } else {
      setStep(2);
      setError('');
    }
  };

  const handleBack = () => {
    setStep(1);
    setError('');
  };

  // Submit for logged-in users (Step 1 only)
  const handleSubmitLoggedIn = async () => {
    setError('');
    setLoading(true);
    try {
      await instanceStore.createInstance(
        subdomain(),
        serverName(),
        '',
        selectedTier(),
        mediaEnabled()
      );
      navigate('/dashboard', { replace: true });
    } catch (err: any) {
      if (err?.message?.includes('SUBDOMAIN_TAKEN')) {
        setSubdomainStatus('taken');
      }
      setError(err?.message || 'Failed to create server');
    } finally {
      setLoading(false);
    }
  };

  // Submit for new users (Step 2)
  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');

    if (password() !== confirmPassword()) {
      setError('Passwords do not match');
      return;
    }
    if (password().length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }
    if (!agreed() || !ageConfirmed() || !jurisdictionConfirmed()) {
      setError('You must agree to all terms');
      return;
    }

    setLoading(true);
    const result = await auth.signupWithInstance(
      email(),
      password(),
      displayName() || username(),
      username(),
      subdomain(),
      serverName(),
      selectedTier(),
      mediaEnabled(),
      captchaId(),
      captchaAnswer()
    );

    setLoading(false);
    if (result) {
      navigate('/dashboard', { replace: true });
    } else if (auth.error?.includes('SUBDOMAIN_TAKEN')) {
      setStep(1);
      setSubdomainStatus('taken');
      setError('This subdomain was taken while you were signing up. Please choose another.');
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

  return (
    <>
      <PageMeta
        title="Get Started - Xcord"
        description="Create your Xcord server."
        path="/get-started"
        noindex
      />
      <Show when={ready()} fallback={
        <div class="min-h-[60vh] flex items-center justify-center">
          <p class="text-xcord-text-muted">Loading...</p>
        </div>
      }>
        <div class="max-w-lg mx-auto py-12 px-4">
          {/* Step indicator */}
          <div data-testid="get-started-steps" class="flex items-center justify-center gap-2 mb-8">
            <div class={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold ${
              step() === 1 ? 'bg-xcord-brand text-white' : 'bg-xcord-bg-accent text-xcord-text-muted'
            }`}>1</div>
            <Show when={!isLoggedIn()}>
              <div class="w-8 h-0.5 bg-xcord-bg-accent" />
              <div class={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold ${
                step() === 2 ? 'bg-xcord-brand text-white' : 'bg-xcord-bg-accent text-xcord-text-muted'
              }`}>2</div>
            </Show>
          </div>

          {/* Step 1: Configure Your Server */}
          <Show when={step() === 1}>
            <div class="bg-xcord-bg-secondary rounded-lg p-8">
              <h1 class="text-xl font-bold text-xcord-text-primary mb-1">Configure Your Server</h1>
              <p class="text-sm text-xcord-text-muted mb-6">
                Step 1{!isLoggedIn() ? ' of 2' : ''} - Choose your server's identity
              </p>

              <div class="space-y-5">
                {/* Subdomain */}
                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Subdomain</label>
                  <div class="flex items-center">
                    <input
                      data-testid="get-started-subdomain"
                      type="text"
                      value={subdomain()}
                      onInput={(e) => handleSubdomainInput(e.currentTarget.value)}
                      class={`flex-1 px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded-l border-none outline-none focus:ring-2 ${
                        subdomainError() ? 'ring-2 ring-xcord-red focus:ring-xcord-red' : 'focus:ring-xcord-brand'
                      }`}
                      placeholder="my-server"
                      autocomplete="off"
                      pattern="[a-z0-9\-]+"
                      minLength={6}
                      disabled={loading()}
                    />
                    <span class="px-3 py-2 bg-xcord-bg-accent text-xcord-text-muted text-sm rounded-r whitespace-nowrap">
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

                {/* Server Name */}
                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Server Name</label>
                  <input
                    data-testid="get-started-server-name"
                    type="text"
                    value={serverName()}
                    onInput={(e) => setServerName(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    placeholder="My Awesome Server"
                    autocomplete="off"
                    disabled={loading()}
                  />
                </div>

                {/* Plan selection */}
                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Plan</label>
                  <div class="grid grid-cols-4 gap-2 mb-3">
                    <button data-testid="get-started-plan-free" type="button" disabled={loading()} onClick={() => setSelectedTier('Free')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${selectedTier() === 'Free' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
                      <div class="font-semibold">Free</div>
                      <div class="text-xs text-xcord-text-muted mt-1">Up to 10 users</div>
                    </button>
                    <Show when={paymentsEnabled()} fallback={
                      <button type="button" onClick={() => { setNotifyTier('Basic'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }} disabled={loading()} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
                        <div class="font-semibold">Basic</div>
                        <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                        <div class="text-xs text-xcord-brand mt-1">Notify me</div>
                      </button>
                    }>
                      <button type="button" disabled={loading()} onClick={() => setSelectedTier('Basic')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${selectedTier() === 'Basic' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
                        <div class="font-semibold">Basic</div>
                        <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                        <div class="text-xs text-xcord-brand mt-1">$60/mo</div>
                      </button>
                    </Show>
                    <Show when={paymentsEnabled()} fallback={
                      <button type="button" onClick={() => { setNotifyTier('Pro'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }} disabled={loading()} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
                        <div class="font-semibold">Pro</div>
                        <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                        <div class="text-xs text-xcord-brand mt-1">Notify me</div>
                      </button>
                    }>
                      <button type="button" disabled={loading()} onClick={() => setSelectedTier('Pro')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${selectedTier() === 'Pro' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
                        <div class="font-semibold">Pro</div>
                        <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                        <div class="text-xs text-xcord-brand mt-1">$150/mo</div>
                      </button>
                    </Show>
                    <button type="button" onClick={() => setShowContact(true)} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
                      <div class="font-semibold">Enterprise</div>
                      <div class="text-xs text-xcord-text-muted mt-1">500+ users</div>
                      <div class="text-xs text-xcord-brand mt-1">Contact us</div>
                    </button>
                  </div>

                  <div class="flex items-center gap-3 mb-3">
                    <span class="text-sm text-xcord-text-primary">Voice & video</span>
                    <Show when={paymentsEnabled()} fallback={
                      <button type="button" onClick={() => { setNotifyTier('Voice & Video'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }} class="text-xs text-xcord-brand hover:underline">Notify me</button>
                    }>
                      <button type="button" onClick={() => setMediaEnabled(!mediaEnabled())} class={`relative w-10 h-5 rounded-full transition ${mediaEnabled() ? 'bg-xcord-brand' : 'bg-xcord-bg-accent'}`}>
                        <div class={`absolute top-0.5 w-4 h-4 rounded-full bg-white transition-transform ${mediaEnabled() ? 'translate-x-5' : 'translate-x-0.5'}`} />
                      </button>
                    </Show>
                  </div>

                  <div class="px-3 py-2 bg-xcord-bg-accent rounded">
                    <div class="flex items-center justify-between">
                      <span class="text-xs font-medium text-xcord-text-primary">Total</span>
                      <span class="text-sm font-bold text-xcord-text-primary">
                        {selectedTier() === 'Free' && !mediaEnabled() ? 'Free' :
                         selectedTier() === 'Free' && mediaEnabled() ? '+$4/user' :
                         selectedTier() === 'Basic' ? (mediaEnabled() ? '$60/mo + $3/user' : '$60/mo') :
                         selectedTier() === 'Pro' ? (mediaEnabled() ? '$150/mo + $2/user' : '$150/mo') : 'Free'}
                      </span>
                    </div>
                  </div>
                </div>

                <Show when={error()}>
                  <div class="text-sm text-xcord-red">{error()}</div>
                </Show>

                <button
                  data-testid="get-started-next"
                  type="button"
                  onClick={handleNext}
                  disabled={loading() || !canProceedStep1()}
                  class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
                >
                  {loading() ? 'Creating...' : isLoggedIn() ? 'Create Server' : 'Next'}
                </button>
              </div>

              <Show when={!isLoggedIn()}>
                <p class="text-sm text-xcord-text-muted mt-4 text-center">
                  Already have an account? <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
                </p>
              </Show>
            </div>
          </Show>

          {/* Step 2: Create Your Account */}
          <Show when={step() === 2}>
            <div class="bg-xcord-bg-secondary rounded-lg p-8">
              <h1 class="text-xl font-bold text-xcord-text-primary mb-1">Create Your Account</h1>
              <p class="text-sm text-xcord-text-muted mb-6">Step 2 of 2 - Set up your account for {subdomain()}.xcord-dev.net</p>

              <form onSubmit={handleSubmit} class="space-y-4">
                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Email</label>
                  <input
                    data-testid="get-started-email"
                    type="email"
                    value={email()}
                    onInput={(e) => setEmail(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    required
                  />
                </div>

                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Username</label>
                  <input
                    data-testid="get-started-username"
                    type="text"
                    value={username()}
                    onInput={(e) => setUsername(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    required
                  />
                </div>

                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Display Name</label>
                  <input
                    data-testid="get-started-display-name"
                    type="text"
                    value={displayName()}
                    onInput={(e) => setDisplayName(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    placeholder={username() || 'Optional'}
                    autocomplete="nickname"
                  />
                </div>

                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Password</label>
                  <input
                    data-testid="get-started-password"
                    type="password"
                    value={password()}
                    onInput={(e) => setPassword(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    required
                    minLength={8}
                    autocomplete="new-password"
                  />
                  <PasswordStrength password={password()} />
                </div>

                <div>
                  <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Confirm Password</label>
                  <input
                    data-testid="get-started-confirm-password"
                    type="password"
                    value={confirmPassword()}
                    onInput={(e) => setConfirmPassword(e.currentTarget.value)}
                    class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
                    required
                    autocomplete="new-password"
                  />
                </div>

                <div class="space-y-2">
                  <label class="flex items-start gap-2 cursor-pointer">
                    <input
                      data-testid="get-started-tos"
                      type="checkbox"
                      checked={agreed()}
                      onChange={(e) => setAgreed(e.currentTarget.checked)}
                      class="mt-1 accent-xcord-brand"
                    />
                    <span class="text-xs text-xcord-text-muted">
                      I agree to the <A href="/terms" class="text-xcord-text-link hover:underline" target="_blank">Terms of Service</A> and{' '}
                      <A href="/privacy" class="text-xcord-text-link hover:underline" target="_blank">Privacy Policy</A>
                    </span>
                  </label>

                  <label class="flex items-start gap-2 cursor-pointer">
                    <input
                      data-testid="get-started-age"
                      type="checkbox"
                      checked={ageConfirmed()}
                      onChange={(e) => setAgeConfirmed(e.currentTarget.checked)}
                      class="mt-1 accent-xcord-brand"
                    />
                    <span class="text-xs text-xcord-text-muted">
                      I confirm that I am at least 18 years old
                    </span>
                  </label>

                  <label class="flex items-start gap-2 cursor-pointer">
                    <input
                      data-testid="get-started-jurisdiction"
                      type="checkbox"
                      checked={jurisdictionConfirmed()}
                      onChange={(e) => setJurisdictionConfirmed(e.currentTarget.checked)}
                      class="mt-1 accent-xcord-brand"
                    />
                    <span class="text-xs text-xcord-text-muted">
                      I confirm that the use of this platform is allowed in my jurisdiction
                    </span>
                  </label>
                </div>

                <Captcha onSolved={(id, ans) => { setCaptchaId(id); setCaptchaAnswer(ans); }} />

                <Show when={error() || auth.error}>
                  <div class="text-sm text-xcord-red">{error() || auth.error}</div>
                </Show>

                <div class="flex gap-3">
                  <button
                    type="button"
                    onClick={handleBack}
                    disabled={loading()}
                    class="px-4 py-2 bg-xcord-bg-tertiary hover:bg-xcord-bg-accent text-xcord-text-primary rounded font-medium transition"
                  >
                    Back
                  </button>
                  <button
                    data-testid="get-started-submit"
                    type="submit"
                    disabled={loading()}
                    class="flex-1 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
                  >
                    {loading() ? 'Creating...' : 'Create Account & Server'}
                  </button>
                </div>
              </form>

              <p class="text-sm text-xcord-text-muted mt-4 text-center">
                Already have an account? <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
              </p>
            </div>
          </Show>
        </div>
      </Show>

      {/* Notify-me modal */}
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
    </>
  );
}
