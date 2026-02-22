import { For, Show, createEffect, onMount } from 'solid-js';
import { useMailingList } from '../stores/mailing-list.store';

const TIERS = ['Basic', 'Pro'];

export function MailingListPage() {
  const mailingList = useMailingList();

  onMount(() => {
    mailingList.fetchEntries();
  });

  createEffect(() => {
    mailingList.fetchEntries();
  });

  const totalPages = () => Math.ceil(mailingList.total / mailingList.pageSize);

  const handlePrevPage = () => {
    if (mailingList.page > 1) {
      mailingList.setPage(mailingList.page - 1);
    }
  };

  const handleNextPage = () => {
    if (mailingList.page < totalPages()) {
      mailingList.setPage(mailingList.page + 1);
    }
  };

  const getTierColor = (tier: string) => {
    switch (tier) {
      case 'Free':
        return 'bg-gray-100 text-gray-800';
      case 'Basic':
        return 'bg-blue-100 text-blue-800';
      case 'Pro':
        return 'bg-purple-100 text-purple-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div class="bg-white rounded-lg shadow">
      <div class="p-6 border-b border-gray-200">
        <div class="flex items-center justify-between">
          <h2 class="text-xl font-semibold">Mailing List</h2>
          <span class="text-sm text-gray-500">{mailingList.total} subscribers</span>
        </div>

        <div class="mt-4 flex gap-2">
          <button
            onClick={() => mailingList.setTierFilter(null)}
            class={`px-3 py-1 rounded text-sm ${
              !mailingList.tierFilter
                ? 'bg-blue-600 text-white'
                : 'bg-gray-100 hover:bg-gray-200'
            }`}
          >
            All
          </button>
          <For each={TIERS}>
            {(tier) => (
              <button
                onClick={() => mailingList.setTierFilter(tier)}
                class={`px-3 py-1 rounded text-sm ${
                  mailingList.tierFilter === tier
                    ? 'bg-blue-600 text-white'
                    : 'bg-gray-100 hover:bg-gray-200'
                }`}
              >
                {tier}
              </button>
            )}
          </For>
        </div>
      </div>

      <Show when={!mailingList.isLoading} fallback={<div class="p-6 text-center">Loading...</div>}>
        <Show
          when={mailingList.entries.length > 0}
          fallback={<div class="p-6 text-center text-gray-500">No subscribers found</div>}
        >
          <table class="w-full">
            <thead class="bg-gray-50 border-b border-gray-200">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Email
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Tier
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Subscribed
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-200">
              <For each={mailingList.entries}>
                {(entry) => (
                  <tr class="hover:bg-gray-50">
                    <td class="px-6 py-4 text-sm font-medium">{entry.email}</td>
                    <td class="px-6 py-4">
                      <span class={`px-2 py-1 text-xs rounded ${getTierColor(entry.tier)}`}>
                        {entry.tier}
                      </span>
                    </td>
                    <td class="px-6 py-4 text-sm text-gray-500">
                      {new Date(entry.createdAt).toLocaleDateString()}
                    </td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>

          <div class="p-4 border-t border-gray-200 flex items-center justify-between">
            <div class="text-sm text-gray-700">
              Showing {(mailingList.page - 1) * mailingList.pageSize + 1} to{' '}
              {Math.min(mailingList.page * mailingList.pageSize, mailingList.total)} of{' '}
              {mailingList.total} subscribers
            </div>
            <div class="flex gap-2">
              <button
                onClick={handlePrevPage}
                disabled={mailingList.page === 1}
                class="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Previous
              </button>
              <button
                onClick={handleNextPage}
                disabled={mailingList.page >= totalPages()}
                class="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Next
              </button>
            </div>
          </div>
        </Show>
      </Show>
    </div>
  );
}
