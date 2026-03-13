import { createSignal, onMount, Show, For } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { BackupRecord } from '../stores/instance.store';

interface BackupHistoryProps {
  instanceId: string;
}

type BackupKind = 'Full' | 'Database' | 'Files' | 'Redis';
type ConfirmAction = { type: 'restore' | 'delete'; backupId: string } | null;

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
}

function statusBadge(status: BackupRecord['status']): string {
  switch (status) {
    case 'Completed':
      return 'bg-green-100 text-green-800';
    case 'InProgress':
      return 'bg-yellow-100 text-yellow-800';
    case 'Failed':
      return 'bg-red-100 text-red-800';
  }
}

export function BackupHistory(props: BackupHistoryProps) {
  const instanceStore = useInstances();
  const [records, setRecords] = createSignal<BackupRecord[]>([]);
  const [isLoading, setIsLoading] = createSignal(true);
  const [page, setPage] = createSignal(1);
  const pageSize = 20;
  const [triggerKind, setTriggerKind] = createSignal<BackupKind>('Full');
  const [isTriggering, setIsTriggering] = createSignal(false);
  const [confirm, setConfirm] = createSignal<ConfirmAction>(null);
  const [isActing, setIsActing] = createSignal(false);

  const fetchRecords = async () => {
    setIsLoading(true);
    try {
      const data = await instanceStore.fetchBackupRecords(props.instanceId, page(), pageSize);
      setRecords(data);
    } catch (error) {
      console.error('Failed to fetch backup records:', error);
      setRecords([]);
    } finally {
      setIsLoading(false);
    }
  };

  onMount(fetchRecords);

  const handleTrigger = async () => {
    setIsTriggering(true);
    try {
      await instanceStore.triggerBackup(props.instanceId, triggerKind());
      await fetchRecords();
    } catch (error) {
      console.error('Failed to trigger backup:', error);
    } finally {
      setIsTriggering(false);
    }
  };

  const handleConfirmedAction = async () => {
    const action = confirm();
    if (!action) return;
    setIsActing(true);
    try {
      if (action.type === 'restore') {
        await instanceStore.triggerRestore(props.instanceId, action.backupId);
      } else {
        await instanceStore.deleteBackup(props.instanceId, action.backupId);
        await fetchRecords();
      }
      setConfirm(null);
    } catch (error) {
      console.error('Action failed:', error);
    } finally {
      setIsActing(false);
    }
  };

  const handlePageChange = async (newPage: number) => {
    setPage(newPage);
    await fetchRecords();
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-lg font-semibold">Backup History</h3>

        <div class="flex items-center gap-2">
          <select
            value={triggerKind()}
            onChange={(e) => setTriggerKind(e.currentTarget.value as BackupKind)}
            class="px-3 py-2 border rounded text-sm"
          >
            <option value="Full">Full</option>
            <option value="Database">Database</option>
            <option value="Files">Files</option>
            <option value="Redis">Redis</option>
          </select>
          <button
            onClick={handleTrigger}
            disabled={isTriggering()}
            class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400 text-sm"
          >
            {isTriggering() ? 'Triggering...' : 'Trigger Backup'}
          </button>
        </div>
      </div>

      <Show when={isLoading()}>
        <p class="text-gray-500 text-sm">Loading backups...</p>
      </Show>

      <Show when={!isLoading()}>
        <Show
          when={records().length > 0}
          fallback={<p class="text-gray-500 text-sm">No backups found.</p>}
        >
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-gray-200 text-left">
                  <th class="pb-2 font-medium text-gray-600">Date</th>
                  <th class="pb-2 font-medium text-gray-600">Kind</th>
                  <th class="pb-2 font-medium text-gray-600">Status</th>
                  <th class="pb-2 font-medium text-gray-600">Size</th>
                  <th class="pb-2 font-medium text-gray-600">Actions</th>
                </tr>
              </thead>
              <tbody>
                <For each={records()}>
                  {(record) => (
                    <tr class="border-b border-gray-100 hover:bg-gray-50">
                      <td class="py-2 pr-4 text-gray-700">
                        {new Date(record.startedAt).toLocaleString()}
                      </td>
                      <td class="py-2 pr-4 text-gray-700">{record.kind}</td>
                      <td class="py-2 pr-4">
                        <span class={`px-2 py-0.5 rounded text-xs font-medium ${statusBadge(record.status)}`}>
                          {record.status}
                        </span>
                      </td>
                      <td class="py-2 pr-4 text-gray-700">{formatBytes(record.sizeBytes)}</td>
                      <td class="py-2">
                        <div class="flex gap-2">
                          <Show when={record.status === 'Completed'}>
                            <button
                              onClick={() => setConfirm({ type: 'restore', backupId: record.id })}
                              class="px-2 py-1 text-xs bg-green-600 text-white rounded hover:bg-green-700"
                            >
                              Restore
                            </button>
                          </Show>
                          <button
                            onClick={() => setConfirm({ type: 'delete', backupId: record.id })}
                            class="px-2 py-1 text-xs bg-red-600 text-white rounded hover:bg-red-700"
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  )}
                </For>
              </tbody>
            </table>
          </div>

          <div class="flex items-center justify-between mt-4">
            <button
              onClick={() => handlePageChange(page() - 1)}
              disabled={page() <= 1}
              class="px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300 disabled:opacity-40"
            >
              Previous
            </button>
            <span class="text-sm text-gray-600">Page {page()}</span>
            <button
              onClick={() => handlePageChange(page() + 1)}
              disabled={records().length < pageSize}
              class="px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300 disabled:opacity-40"
            >
              Next
            </button>
          </div>
        </Show>
      </Show>

      <Show when={confirm()}>
        <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div class="bg-white rounded-lg p-6 max-w-md w-full mx-4">
            <h4 class="text-lg font-semibold mb-2">Confirm Action</h4>
            <p class="text-gray-600 mb-4">
              {confirm()!.type === 'restore'
                ? 'Are you sure you want to restore from this backup? This will overwrite current data.'
                : 'Are you sure you want to delete this backup? This cannot be undone.'}
            </p>
            <div class="flex gap-3">
              <button
                onClick={handleConfirmedAction}
                disabled={isActing()}
                class="flex-1 px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 disabled:bg-gray-400"
              >
                {isActing() ? 'Processing...' : 'Confirm'}
              </button>
              <button
                onClick={() => setConfirm(null)}
                disabled={isActing()}
                class="flex-1 px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      </Show>
    </div>
  );
}
