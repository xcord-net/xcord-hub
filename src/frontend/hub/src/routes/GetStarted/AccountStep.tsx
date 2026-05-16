import { Show } from 'solid-js';
import type { Accessor, Setter } from 'solid-js';
import { A } from '@solidjs/router';
import Captcha from '../../components/Captcha';
import PasswordStrength from '../../components/PasswordStrength';

interface AccountStepProps {
  totalSteps: Accessor<number>;
  accountStep: () => number;
  loading: Accessor<boolean>;
  error: Accessor<string>;
  authError: () => string | null | undefined;
  subdomain: Accessor<string>;
  isPaidTier: () => boolean;

  email: Accessor<string>;
  setEmail: Setter<string>;
  username: Accessor<string>;
  setUsername: Setter<string>;
  displayName: Accessor<string>;
  setDisplayName: Setter<string>;
  password: Accessor<string>;
  setPassword: Setter<string>;
  confirmPassword: Accessor<string>;
  setConfirmPassword: Setter<string>;
  agreed: Accessor<boolean>;
  setAgreed: Setter<boolean>;
  ageConfirmed: Accessor<boolean>;
  setAgeConfirmed: Setter<boolean>;
  jurisdictionConfirmed: Accessor<boolean>;
  setJurisdictionConfirmed: Setter<boolean>;

  onCaptchaSolved: (id: string, ans: string) => void;
  onSubmit: (e: Event) => void;
  onBack: () => void;
}

export default function AccountStep(props: AccountStepProps) {
  return (
    <div class="bg-xcord-bg-secondary rounded-lg p-8">
      <h1 class="text-xl font-bold text-xcord-text-primary mb-1">Create Your Account</h1>
      <p class="text-sm text-xcord-text-muted mb-6">
        Step {props.accountStep()} of {props.totalSteps()} - Set up your account for {props.subdomain()}.xcord-dev.net
      </p>

      <form onSubmit={props.onSubmit} class="space-y-4">
        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Email</label>
          <input
            data-testid="get-started-email"
            type="email"
            value={props.email()}
            onInput={(e) => props.setEmail(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            required
          />
        </div>

        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Username</label>
          <input
            data-testid="get-started-username"
            type="text"
            value={props.username()}
            onInput={(e) => props.setUsername(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            required
          />
        </div>

        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Display Name</label>
          <input
            data-testid="get-started-display-name"
            type="text"
            value={props.displayName()}
            onInput={(e) => props.setDisplayName(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            placeholder={props.username() || 'Optional'}
            autocomplete="nickname"
          />
        </div>

        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Password</label>
          <input
            data-testid="get-started-password"
            type="password"
            value={props.password()}
            onInput={(e) => props.setPassword(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand"
            required
            minLength={8}
            autocomplete="new-password"
          />
          <PasswordStrength password={props.password()} />
        </div>

        <div>
          <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Confirm Password</label>
          <input
            data-testid="get-started-confirm-password"
            type="password"
            value={props.confirmPassword()}
            onInput={(e) => props.setConfirmPassword(e.currentTarget.value)}
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
              checked={props.agreed()}
              onChange={(e) => props.setAgreed(e.currentTarget.checked)}
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
              checked={props.ageConfirmed()}
              onChange={(e) => props.setAgeConfirmed(e.currentTarget.checked)}
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
              checked={props.jurisdictionConfirmed()}
              onChange={(e) => props.setJurisdictionConfirmed(e.currentTarget.checked)}
              class="mt-1 accent-xcord-brand"
            />
            <span class="text-xs text-xcord-text-muted">
              I confirm that the use of this platform is allowed in my jurisdiction
            </span>
          </label>
        </div>

        <Captcha onSolved={props.onCaptchaSolved} />

        <Show when={props.error() || props.authError()}>
          <div class="text-sm text-xcord-red">{props.error() || props.authError()}</div>
        </Show>

        <div class="flex gap-3">
          <button
            type="button"
            onClick={props.onBack}
            disabled={props.loading()}
            class="px-4 py-2 bg-xcord-bg-tertiary hover:bg-xcord-bg-accent text-xcord-text-primary rounded font-medium transition"
          >
            Back
          </button>
          <button
            data-testid="get-started-submit"
            type="submit"
            disabled={props.loading()}
            class="flex-1 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
          >
            {props.loading() ? 'Creating...' : 'Create Account & Server'}
          </button>
        </div>
      </form>

      <p class="text-sm text-xcord-text-muted mt-4 text-center">
        Already have an account? <A href="/login" class="text-xcord-text-link hover:underline">Log In</A>
      </p>
    </div>
  );
}
