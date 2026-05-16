import { Show } from 'solid-js';
import type { Accessor, Setter } from 'solid-js';
import { A } from '@solidjs/router';
import type { NotifyStatus, SubdomainStatus, Tier } from './state';

interface ConfigStepProps {
  totalSteps: Accessor<number>;
  loading: Accessor<boolean>;
  error: Accessor<string>;
  isLoggedIn: () => boolean;
  paidTierAvailable: () => boolean;
  isPaidTier: () => boolean;

  subdomain: Accessor<string>;
  subdomainStatus: Accessor<SubdomainStatus>;
  subdomainError: () => string;
  subdomainValid: () => boolean;
  onSubdomainInput: (value: string) => void;

  serverName: Accessor<string>;
  setServerName: Setter<string>;

  selectedTier: Accessor<Tier>;
  setSelectedTier: Setter<Tier>;

  mediaEnabled: Accessor<boolean>;
  setMediaEnabled: Setter<boolean>;

  canProceedStep1: () => boolean;
  onNext: () => void;
  onShowContact: () => void;
  onNotifyOpen: (tier: string) => void;

  setNotifyStatus: Setter<NotifyStatus>;
  setNotifyMessage: Setter<string>;
  setNotifyEmail: Setter<string>;
}

export default function ConfigStep(props: ConfigStepProps) {
  return (
    <div class="bg-xcord-bg-secondary rounded-lg p-8">
      <h1 class="text-xl font-bold text-xcord-text-primary mb-1">Configure Your Server</h1>
      <p class="text-sm text-xcord-text-muted mb-6">
        Step 1 of {props.totalSteps()} - Choose your server's identity
      </p>

      <div class="space-y-5">
        {/* Subdomain */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Subdomain</label>
          <div class="flex items-center">
            <input
              data-testid="get-started-subdomain"
              type="text"
              value={props.subdomain()}
              onInput={(e) => props.onSubdomainInput(e.currentTarget.value)}
              class={`flex-1 px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded-l border-none outline-none focus:ring-2 ${
                props.subdomainError() ? 'ring-2 ring-xcord-red focus:ring-xcord-red' : 'focus:ring-xcord-brand'
              }`}
              placeholder="my-server"
              autocomplete="off"
              pattern="[a-z0-9\-]+"
              minLength={6}
              disabled={props.loading()}
            />
            <span class="px-3 py-2 bg-xcord-bg-accent text-xcord-text-muted text-sm rounded-r whitespace-nowrap">
              .xcord-dev.net
            </span>
          </div>
          <Show when={props.subdomainStatus() === 'checking' && !props.subdomainError()}>
            <span class="text-xs text-xcord-text-muted mt-1 block">Checking availability...</span>
          </Show>
          <Show when={props.subdomain().length > 0 && props.subdomainError()}>
            <span class="text-xs text-xcord-red mt-1 block">{props.subdomainError()}</span>
          </Show>
          <Show when={props.subdomainValid() && props.subdomainStatus() === 'available'}>
            <span class="text-xs text-xcord-green mt-1 block">Available!</span>
          </Show>
        </div>

        {/* Server Name */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Server Name</label>
          <input
            data-testid="get-started-server-name"
            type="text"
            value={props.serverName()}
            onInput={(e) => props.setServerName(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder="My Awesome Server"
            autocomplete="off"
            disabled={props.loading()}
          />
        </div>

        {/* Plan selection */}
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Plan</label>
          <div class="grid grid-cols-4 gap-2 mb-3">
            <button data-testid="get-started-plan-free" type="button" disabled={props.loading()} onClick={() => props.setSelectedTier('Free')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${props.selectedTier() === 'Free' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
              <div class="font-semibold">Free</div>
              <div class="text-xs text-xcord-text-muted mt-1">Up to 10 users</div>
            </button>
            <Show when={props.paidTierAvailable()} fallback={
              <button data-testid="get-started-plan-basic-notify" type="button" onClick={() => props.onNotifyOpen('Basic')} disabled={props.loading()} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
                <div class="font-semibold">Basic</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                <div class="text-xs text-xcord-brand mt-1">Notify me</div>
              </button>
            }>
              <button data-testid="get-started-plan-basic" type="button" disabled={props.loading()} onClick={() => props.setSelectedTier('Basic')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${props.selectedTier() === 'Basic' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
                <div class="font-semibold">Basic</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 50 users</div>
                <div class="text-xs text-xcord-brand mt-1">$60/mo</div>
              </button>
            </Show>
            <Show when={props.paidTierAvailable()} fallback={
              <button data-testid="get-started-plan-pro-notify" type="button" onClick={() => props.onNotifyOpen('Pro')} disabled={props.loading()} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
                <div class="font-semibold">Pro</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                <div class="text-xs text-xcord-brand mt-1">Notify me</div>
              </button>
            }>
              <button data-testid="get-started-plan-pro" type="button" disabled={props.loading()} onClick={() => props.setSelectedTier('Pro')} class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ${props.selectedTier() === 'Pro' ? 'ring-2 ring-xcord-brand' : 'hover:bg-xcord-bg-accent'} transition`}>
                <div class="font-semibold">Pro</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 200 users</div>
                <div class="text-xs text-xcord-brand mt-1">$150/mo</div>
              </button>
            </Show>
            <button type="button" onClick={props.onShowContact} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition">
              <div class="font-semibold">Enterprise</div>
              <div class="text-xs text-xcord-text-muted mt-1">500+ users</div>
              <div class="text-xs text-xcord-brand mt-1">Contact us</div>
            </button>
          </div>

          <div class="flex items-center gap-3 mb-3">
            <span class="text-sm text-xcord-text-primary">Voice & video</span>
            <Show when={props.paidTierAvailable()} fallback={
              <button type="button" onClick={() => props.onNotifyOpen('Voice & Video')} class="text-xs text-xcord-brand hover:underline">Notify me</button>
            }>
              <button type="button" onClick={() => props.setMediaEnabled(!props.mediaEnabled())} class={`relative w-10 h-5 rounded-full transition ${props.mediaEnabled() ? 'bg-xcord-brand' : 'bg-xcord-bg-accent'}`}>
                <div class={`absolute top-0.5 w-4 h-4 rounded-full bg-white transition-transform ${props.mediaEnabled() ? 'translate-x-5' : 'translate-x-0.5'}`} />
              </button>
            </Show>
          </div>

          <div class="px-3 py-2 bg-xcord-bg-accent rounded">
            <div class="flex items-center justify-between">
              <span class="text-xs font-medium text-xcord-text-primary">Total</span>
              <span class="text-sm font-bold text-xcord-text-primary">
                {props.selectedTier() === 'Free' && !props.mediaEnabled() ? 'Free' :
                 props.selectedTier() === 'Free' && props.mediaEnabled() ? '+$4/user' :
                 props.selectedTier() === 'Basic' ? (props.mediaEnabled() ? '$60/mo + $3/user' : '$60/mo') :
                 props.selectedTier() === 'Pro' ? (props.mediaEnabled() ? '$150/mo + $2/user' : '$150/mo') : 'Free'}
              </span>
            </div>
          </div>
        </div>

        <Show when={props.error()}>
          <div class="text-sm text-xcord-red">{props.error()}</div>
        </Show>

        <button
          data-testid="get-started-next"
          type="button"
          onClick={props.onNext}
          disabled={props.loading() || !props.canProceedStep1()}
          class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
        >
          {props.loading()
            ? (props.isPaidTier() ? 'Initializing...' : 'Creating...')
            : props.isLoggedIn()
              ? (props.isPaidTier() ? 'Next: Payment' : 'Create Server')
              : 'Next'}
        </button>
      </div>

      <Show when={!props.isLoggedIn()}>
        <p class="text-sm text-xcord-text-muted mt-4 text-center">
          Already have an account? <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
        </p>
      </Show>
    </div>
  );
}
