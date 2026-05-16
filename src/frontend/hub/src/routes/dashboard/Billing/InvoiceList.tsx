import { For, Show } from 'solid-js';
import type { Resource } from 'solid-js';
import { formatAmount, formatDate, statusBadge, type InvoicesData } from './types';

interface InvoiceListProps {
  invoices: Resource<InvoicesData>;
}

export default function InvoiceList(props: InvoiceListProps) {
  return (
    <div class="bg-xcord-bg-secondary rounded-lg p-6">
      <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Invoices</h2>

      <Show when={props.invoices.loading}>
        <div class="text-xcord-text-muted text-sm py-4">Loading invoices...</div>
      </Show>

      <Show when={props.invoices.error}>
        <div class="text-xcord-red text-sm">Failed to load invoices.</div>
      </Show>

      <Show when={props.invoices() && props.invoices()!.invoices.length === 0}>
        <div class="text-center py-8">
          <p class="text-xcord-text-muted text-sm">No invoices yet.</p>
          <p class="text-xcord-text-muted text-xs mt-1">
            Invoices will appear here once you have an active paid subscription.
          </p>
        </div>
      </Show>

      <Show when={props.invoices() && props.invoices()!.invoices.length > 0}>
        <div class="divide-y divide-xcord-bg-tertiary">
          <For each={props.invoices()!.invoices}>
            {(invoice) => (
              <div class="flex items-center justify-between py-3">
                <div>
                  <div class="text-sm text-xcord-text-primary">{invoice.description}</div>
                  <div class="text-xs text-xcord-text-muted mt-0.5">{formatDate(invoice.createdAt)}</div>
                </div>
                <div class="flex items-center gap-4">
                  <span class={`text-xs px-2 py-0.5 rounded ${statusBadge(invoice.status)}`}>
                    {invoice.status}
                  </span>
                  <span class="text-sm font-medium text-xcord-text-primary">
                    {formatAmount(invoice.amountCents, invoice.currency)}
                  </span>
                  <Show when={invoice.pdfUrl}>
                    <a
                      href={invoice.pdfUrl!}
                      target="_blank"
                      rel="noopener noreferrer"
                      class="text-xs text-xcord-text-link hover:underline"
                    >
                      PDF
                    </a>
                  </Show>
                </div>
              </div>
            )}
          </For>
        </div>
      </Show>
    </div>
  );
}
