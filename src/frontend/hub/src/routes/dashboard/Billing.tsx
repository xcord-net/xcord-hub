import { A } from '@solidjs/router';
import { createResource, createSignal, For, Show } from 'solid-js';
import PageMeta from '../../components/PageMeta';
import InvoiceList from './Billing/InvoiceList';
import SubscriptionPanel from './Billing/SubscriptionPanel';
import UsageSummary from './Billing/UsageSummary';
import {
  fetchBilling,
  fetchInvoices,
  formatPrice,
  formatTier,
  statusBadge,
  TIER_CONFIG,
  type InstanceBillingItem,
  type Tier,
} from './Billing/types';

export default function Billing() {
  const [billing, { refetch: refetchBilling }] = createResource(fetchBilling);
  const [invoices] = createResource(fetchInvoices);
  const [editingInstance, setEditingInstance] = createSignal<InstanceBillingItem | null>(null);

  return (
    <>
      <PageMeta
        title="Billing - Xcord"
        description="Manage your billing and subscriptions."
        path="/dashboard/billing"
        noindex
      />
      <div class="p-8 max-w-3xl">
        <h1 data-testid="billing-heading" class="font-display text-2xl font-bold tracking-[-0.01em] text-xcord-text-primary mb-8">Billing</h1>

        {/* Instance list loading / error */}
        <Show when={billing.loading}>
          <div class="text-xcord-text-muted text-sm py-4">Loading billing info...</div>
        </Show>

        <Show when={billing.error}>
          <div class="bg-xcord-red/10 text-xcord-red text-sm rounded-lg p-4 mb-6">
            Failed to load billing information. Please refresh the page.
          </div>
        </Show>

        <Show when={billing()}>
          {(data) => (
            <div class="mb-8">
              <Show
                when={data().instances.length > 0}
                fallback={
                  <div class="bg-xcord-bg-secondary rounded-lg p-8 text-center">
                    <p class="text-xcord-text-muted text-sm mb-2">
                      No instances yet. Create one to get started.
                    </p>
                    <A
                      href="/dashboard/create-instance"
                      class="text-sm text-xcord-text-link hover:underline"
                    >
                      Create an instance &rarr;
                    </A>
                  </div>
                }
              >
                <div class="space-y-4">
                  <For each={data().instances}>
                    {(instance) => (
                      <div class="bg-xcord-bg-secondary rounded-lg p-6">
                        <div class="flex items-start justify-between mb-3">
                          <div>
                            <div class="text-base font-semibold text-xcord-text-primary">
                              {instance.displayName}
                            </div>
                            <div class="text-xs text-xcord-text-muted mt-0.5">{instance.domain}</div>
                          </div>
                          <span class={`px-3 py-1 text-sm font-medium rounded ${statusBadge(instance.billingStatus)}`}>
                            {instance.billingStatus}
                          </span>
                        </div>

                        <div class="grid grid-cols-3 gap-4 mb-4">
                          <div>
                            <div class="text-xs text-xcord-text-muted mb-1">Plan</div>
                            <div class="text-sm text-xcord-text-primary font-medium">
                              {formatTier(instance.tier, instance.mediaEnabled)}
                            </div>
                          </div>
                          <div>
                            <div class="text-xs text-xcord-text-muted mb-1">User Limit</div>
                            <div class="text-sm text-xcord-text-primary font-medium">
                              {TIER_CONFIG[instance.tier as Tier]?.maxUsers ?? '-'} users
                            </div>
                          </div>
                          <div>
                            <div class="text-xs text-xcord-text-muted mb-1">Price</div>
                            <div class="text-sm text-xcord-text-primary font-medium">
                              {formatPrice(instance.priceCents)}
                            </div>
                          </div>
                        </div>

                        {/* Usage breakdown for Enterprise metered instances */}
                        <Show when={instance.tier === 'Enterprise'}>
                          <UsageSummary instanceId={instance.instanceId} />
                        </Show>

                        <div class="pt-3 border-t border-xcord-bg-tertiary">
                          <button
                            onClick={() => setEditingInstance(instance)}
                            class="px-3 py-1.5 text-xs font-medium text-xcord-text-link bg-xcord-brand/10 rounded hover:bg-xcord-brand/20 transition"
                          >
                            Change Plan
                          </button>
                        </div>
                      </div>
                    )}
                  </For>
                </div>
              </Show>
            </div>
          )}
        </Show>

        {/* Plan editor modal */}
        <Show when={editingInstance()}>
          {(instance) => (
            <SubscriptionPanel
              instance={instance()}
              onClose={() => setEditingInstance(null)}
              onSaved={() => { refetchBilling(); }}
            />
          )}
        </Show>

        {/* Invoices */}
        <InvoiceList invoices={invoices} />
      </div>
    </>
  );
}
