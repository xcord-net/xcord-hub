import { createSignal, createRoot } from 'solid-js';
import { api } from '../api/client';
import type { MailingListEntry, MailingListResponse } from '../types/mailing-list';

const store = createRoot(() => {
  const [entries, setEntries] = createSignal<MailingListEntry[]>([]);
  const [total, setTotal] = createSignal(0);
  const [page, setPage] = createSignal(1);
  const [pageSize] = createSignal(25);
  const [tierFilter, setTierFilter] = createSignal<string | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);

  return {
    entries,
    setEntries,
    total,
    setTotal,
    page,
    setPage,
    pageSize,
    tierFilter,
    setTierFilter,
    isLoading,
    setIsLoading,
  };
});

export function useMailingList() {
  return {
    get entries() { return store.entries(); },
    get total() { return store.total(); },
    get page() { return store.page(); },
    get pageSize() { return store.pageSize(); },
    get tierFilter() { return store.tierFilter(); },
    get isLoading() { return store.isLoading(); },

    setPage(p: number) {
      store.setPage(p);
    },

    setTierFilter(tier: string | null) {
      store.setTierFilter(tier);
      store.setPage(1);
    },

    async fetchEntries(): Promise<void> {
      store.setIsLoading(true);
      try {
        let url = `/api/v1/admin/mailing-list?page=${store.page()}&pageSize=${store.pageSize()}`;
        if (store.tierFilter()) {
          url += `&tier=${store.tierFilter()}`;
        }

        const response = await api.get<MailingListResponse>(url);
        store.setEntries(response.entries);
        store.setTotal(response.total);
      } finally {
        store.setIsLoading(false);
      }
    },
  };
}
