import { Show } from 'solid-js';
import type { Accessor } from 'solid-js';
import { TIER_BASE_PRICE, TIER_MEDIA_ADDON, type StripeContext, type Tier } from './state';

interface PaymentStepProps {
  totalSteps: Accessor<number>;
  loading: Accessor<boolean>;
  error: Accessor<string>;
  selectedTier: Accessor<Tier>;
  mediaEnabled: Accessor<boolean>;
  stripeCtx: Accessor<StripeContext | null>;
  onBack: () => void;
  onConfirm: () => void;
}

export default function PaymentStep(props: PaymentStepProps) {
  return (
    <div data-testid="get-started-payment-step" class="bg-xcord-bg-secondary rounded-lg p-8">
      <h1 class="text-xl font-bold text-xcord-text-primary mb-1">Payment</h1>
      <p class="text-sm text-xcord-text-muted mb-6">
        Step 2 of {props.totalSteps()} - {props.selectedTier()} plan
      </p>

      {/* Price summary */}
      <div class="mb-6 p-4 bg-xcord-bg-accent rounded space-y-1">
        <div class="flex justify-between text-sm">
          <span class="text-xcord-text-primary">{props.selectedTier()} plan</span>
          <span class="text-xcord-text-primary font-medium">${TIER_BASE_PRICE[props.selectedTier()] ?? 0}/mo</span>
        </div>
        <Show when={props.mediaEnabled()}>
          <div class="flex justify-between text-sm">
            <span class="text-xcord-text-muted">Voice & video</span>
            <span class="text-xcord-text-muted">+${TIER_MEDIA_ADDON[props.selectedTier()] ?? 0}/mo</span>
          </div>
        </Show>
        <div class="flex justify-between text-sm pt-1 border-t border-xcord-bg-tertiary">
          <span class="text-xcord-text-primary font-medium">Total</span>
          <span class="text-xcord-text-primary font-bold">
            ${(TIER_BASE_PRICE[props.selectedTier()] ?? 0) + (props.mediaEnabled() ? (TIER_MEDIA_ADDON[props.selectedTier()] ?? 0) : 0)}/mo
          </span>
        </div>
      </div>

      {/* Stripe Payment Element mount point */}
      <div id="payment-element" data-testid="stripe-payment-element" class="mb-6" />

      <Show when={props.error()}>
        <div class="text-sm text-xcord-red mb-4">{props.error()}</div>
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
          data-testid="get-started-payment-continue"
          type="button"
          onClick={props.onConfirm}
          disabled={props.loading() || !props.stripeCtx()}
          class="flex-1 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
        >
          {props.loading() ? 'Processing...' : 'Continue'}
        </button>
      </div>
    </div>
  );
}
