import { For, Show, createEffect, onMount } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import { InstanceStatus } from '../types/instance';

interface InstanceListProps {
  onSelectInstance: (id: string) => void;
  onProvisionNew: () => void;
}

export function InstanceList(props: InstanceListProps) {
  const instanceStore = useInstances();

  onMount(() => {
    instanceStore.fetchInstances();
  });

  createEffect(() => {
    instanceStore.fetchInstances();
  });

  const totalPages = () => Math.ceil(instanceStore.total / instanceStore.pageSize);

  const handlePrevPage = () => {
    if (instanceStore.page > 1) {
      instanceStore.setPage(instanceStore.page - 1);
    }
  };

  const handleNextPage = () => {
    if (instanceStore.page < totalPages()) {
      instanceStore.setPage(instanceStore.page + 1);
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case InstanceStatus.Running:
        return 'bg-green-100 text-green-800';
      case InstanceStatus.Provisioning:
        return 'bg-blue-100 text-blue-800';
      case InstanceStatus.Suspended:
        return 'bg-yellow-100 text-yellow-800';
      case InstanceStatus.Destroyed:
        return 'bg-gray-100 text-gray-800';
      case InstanceStatus.Failed:
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div class="bg-white rounded-lg shadow">
      <div class="p-6 border-b border-gray-200">
        <div class="flex items-center justify-between">
          <h2 class="text-xl font-semibold">Instances</h2>
          <button
            onClick={props.onProvisionNew}
            class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
          >
            Provision New Instance
          </button>
        </div>

        <div class="mt-4 flex gap-2">
          <button
            onClick={() => instanceStore.setStatusFilter(null)}
            class={`px-3 py-1 rounded text-sm ${
              !instanceStore.statusFilter
                ? 'bg-blue-600 text-white'
                : 'bg-gray-100 hover:bg-gray-200'
            }`}
          >
            All
          </button>
          <For each={Object.values(InstanceStatus)}>
            {(status) => (
              <button
                onClick={() => instanceStore.setStatusFilter(status)}
                class={`px-3 py-1 rounded text-sm ${
                  instanceStore.statusFilter === status
                    ? 'bg-blue-600 text-white'
                    : 'bg-gray-100 hover:bg-gray-200'
                }`}
              >
                {status}
              </button>
            )}
          </For>
        </div>
      </div>

      <Show when={!instanceStore.isLoading} fallback={<div class="p-6 text-center">Loading...</div>}>
        <Show
          when={instanceStore.instances.length > 0}
          fallback={<div class="p-6 text-center text-gray-500">No instances found</div>}
        >
          <table class="w-full">
            <thead class="bg-gray-50 border-b border-gray-200">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Subdomain
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Display Name
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Owner
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Status
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Plan
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Created
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-200">
              <For each={instanceStore.instances}>
                {(instance) => (
                  <tr
                    onClick={() => props.onSelectInstance(instance.id)}
                    class="hover:bg-gray-50 cursor-pointer"
                  >
                    <td class="px-6 py-4 text-sm font-medium">{instance.subdomain}</td>
                    <td class="px-6 py-4 text-sm">{instance.displayName}</td>
                    <td class="px-6 py-4 text-sm">{instance.ownerUsername}</td>
                    <td class="px-6 py-4">
                      <span class={`px-2 py-1 text-xs rounded ${getStatusColor(instance.status)}`}>
                        {instance.status}
                      </span>
                    </td>
                    <td class="px-6 py-4 text-sm">{instance.featureTier} / {instance.userCountTier}</td>
                    <td class="px-6 py-4 text-sm">
                      {new Date(instance.createdAt).toLocaleDateString()}
                    </td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>

          <div class="p-4 border-t border-gray-200 flex items-center justify-between">
            <div class="text-sm text-gray-700">
              Showing {(instanceStore.page - 1) * instanceStore.pageSize + 1} to{' '}
              {Math.min(instanceStore.page * instanceStore.pageSize, instanceStore.total)} of{' '}
              {instanceStore.total} instances
            </div>
            <div class="flex gap-2">
              <button
                onClick={handlePrevPage}
                disabled={instanceStore.page === 1}
                class="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Previous
              </button>
              <button
                onClick={handleNextPage}
                disabled={instanceStore.page >= totalPages()}
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
