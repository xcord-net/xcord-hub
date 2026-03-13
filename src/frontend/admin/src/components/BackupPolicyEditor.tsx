import { createSignal, onMount, Show } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { BackupPolicy } from '../stores/instance.store';

interface BackupPolicyEditorProps {
  instanceId: string;
}

export function BackupPolicyEditor(props: BackupPolicyEditorProps) {
  const instanceStore = useInstances();
  const [policy, setPolicy] = createSignal<BackupPolicy | null>(null);
  const [isLoading, setIsLoading] = createSignal(true);
  const [isSaving, setIsSaving] = createSignal(false);
  const [saveResult, setSaveResult] = createSignal<'success' | 'error' | null>(null);

  onMount(async () => {
    try {
      const fetched = await instanceStore.fetchBackupPolicy(props.instanceId);
      setPolicy(fetched);
    } catch (error) {
      console.error('Failed to fetch backup policy:', error);
    } finally {
      setIsLoading(false);
    }
  });

  const handleSave = async () => {
    const current = policy();
    if (!current) return;
    setIsSaving(true);
    setSaveResult(null);
    try {
      await instanceStore.updateBackupPolicy(props.instanceId, current);
      setSaveResult('success');
    } catch (error) {
      console.error('Failed to update backup policy:', error);
      setSaveResult('error');
    } finally {
      setIsSaving(false);
    }
  };

  const update = <K extends keyof BackupPolicy>(key: K, value: BackupPolicy[K]) => {
    const current = policy();
    if (current) setPolicy({ ...current, [key]: value });
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <h3 class="text-lg font-semibold mb-4">Backup Policy</h3>

      <Show when={isLoading()}>
        <p class="text-gray-500 text-sm">Loading policy...</p>
      </Show>

      <Show when={!isLoading() && policy()}>
        <div class="space-y-4">
          <div class="flex items-center justify-between">
            <label class="text-sm font-medium">Enable Scheduled Backups</label>
            <button
              onClick={() => update('enabled', !policy()!.enabled)}
              class={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors cursor-pointer ${
                policy()!.enabled ? 'bg-blue-600' : 'bg-gray-300'
              }`}
            >
              <span
                class={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                  policy()!.enabled ? 'translate-x-6' : 'translate-x-1'
                }`}
              />
            </button>
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Frequency</label>
            <select
              value={policy()!.frequency}
              onChange={(e) => update('frequency', e.currentTarget.value as BackupPolicy['frequency'])}
              disabled={!policy()!.enabled}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            >
              <option value="Hourly">Hourly</option>
              <option value="Daily">Daily</option>
              <option value="Weekly">Weekly</option>
            </select>
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Retention (days)</label>
            <input
              type="number"
              min="1"
              value={policy()!.retentionDays}
              onInput={(e) => update('retentionDays', parseInt(e.currentTarget.value) || 1)}
              disabled={!policy()!.enabled}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div class="space-y-2">
            <p class="text-sm font-medium">What to back up</p>

            <label class="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={policy()!.backupDatabase}
                onChange={(e) => update('backupDatabase', e.currentTarget.checked)}
                disabled={!policy()!.enabled}
                class="h-4 w-4 rounded border-gray-300"
              />
              <span class="text-sm">Database</span>
            </label>

            <label class="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={policy()!.backupFiles}
                onChange={(e) => update('backupFiles', e.currentTarget.checked)}
                disabled={!policy()!.enabled}
                class="h-4 w-4 rounded border-gray-300"
              />
              <span class="text-sm">Files</span>
            </label>

            <label class="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={policy()!.backupRedis}
                onChange={(e) => update('backupRedis', e.currentTarget.checked)}
                disabled={!policy()!.enabled}
                class="h-4 w-4 rounded border-gray-300"
              />
              <span class="text-sm">Redis</span>
            </label>
          </div>

          <div class="flex items-center gap-3 pt-2">
            <button
              onClick={handleSave}
              disabled={isSaving()}
              class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400"
            >
              {isSaving() ? 'Saving...' : 'Save Policy'}
            </button>

            <Show when={saveResult() === 'success'}>
              <span class="text-sm text-green-600">Policy saved.</span>
            </Show>
            <Show when={saveResult() === 'error'}>
              <span class="text-sm text-red-600">Failed to save policy.</span>
            </Show>
          </div>
        </div>
      </Show>

      <Show when={!isLoading() && !policy()}>
        <p class="text-gray-500 text-sm">Could not load backup policy.</p>
      </Show>
    </div>
  );
}
