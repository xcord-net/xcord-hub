import { createSignal, createEffect } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { ResourceLimits } from '../types/instance';

interface ResourceLimitsEditorProps {
  instanceId: string;
  initialLimits: ResourceLimits;
}

/**
 * ResourceLimitsJson is stored with JsonSerializer defaults and returned
 * verbatim, so the payload keys are PascalCase.
 */
function normaliseLimits(raw: ResourceLimits | Record<string, unknown> | undefined | null): ResourceLimits {
  const source = (raw ?? {}) as Record<string, unknown>;
  const read = (name: string) =>
    Number(source[name] ?? source[name.charAt(0).toUpperCase() + name.slice(1)] ?? 0);
  return {
    maxUsers: read('maxUsers'),
    maxServers: read('maxServers'),
    maxStorageMb: read('maxStorageMb'),
    maxCpuPercent: read('maxCpuPercent'),
    maxMemoryMb: read('maxMemoryMb'),
    maxRateLimit: read('maxRateLimit'),
    maxVoiceConcurrency: read('maxVoiceConcurrency'),
    maxVideoConcurrency: read('maxVideoConcurrency'),
  };
}

export function ResourceLimitsEditor(props: ResourceLimitsEditorProps) {
  const instanceStore = useInstances();
  const [limits, setLimits] = createSignal<ResourceLimits>(normaliseLimits(props.initialLimits));
  const [isEditing, setIsEditing] = createSignal(false);
  const [isSaving, setIsSaving] = createSignal(false);

  createEffect(() => {
    setLimits(normaliseLimits(props.initialLimits));
  });

  const handleSave = async () => {
    setIsSaving(true);
    try {
      await instanceStore.updateResourceLimits(props.instanceId, limits());
      setIsEditing(false);
    } catch (error) {
      console.error('Failed to update limits:', error);
    } finally {
      setIsSaving(false);
    }
  };

  const handleCancel = () => {
    setLimits(normaliseLimits(props.initialLimits));
    setIsEditing(false);
  };

  const updateLimit = (key: keyof ResourceLimits, value: number) => {
    setLimits({ ...limits(), [key]: value });
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-lg font-semibold">Resource Limits</h3>
        {!isEditing() && (
          <button
            data-testid="resource-limits-edit"
            onClick={() => setIsEditing(true)}
            class="px-3 py-1 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
          >
            Edit
          </button>
        )}
      </div>

      <div class="space-y-3">
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="block text-sm font-medium mb-1">Max Users</label>
            <input
              data-testid="resource-limit-maxUsers"
              type="number"
              value={limits().maxUsers}
              onInput={(e) => updateLimit('maxUsers', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Servers</label>
            <input
              data-testid="resource-limit-maxServers"
              type="number"
              value={limits().maxServers}
              onInput={(e) => updateLimit('maxServers', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max CPU (%)</label>
            <input
              data-testid="resource-limit-maxCpuPercent"
              type="number"
              value={limits().maxCpuPercent}
              onInput={(e) => updateLimit('maxCpuPercent', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Memory (MB)</label>
            <input
              data-testid="resource-limit-maxMemoryMb"
              type="number"
              value={limits().maxMemoryMb}
              onInput={(e) => updateLimit('maxMemoryMb', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Storage (MB)</label>
            <input
              data-testid="resource-limit-maxStorageMb"
              type="number"
              value={limits().maxStorageMb}
              onInput={(e) => updateLimit('maxStorageMb', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Rate Limit (req/min)</label>
            <input
              data-testid="resource-limit-maxRateLimit"
              type="number"
              value={limits().maxRateLimit}
              onInput={(e) => updateLimit('maxRateLimit', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>
        </div>

        {isEditing() && (
          <div class="flex gap-3 pt-2">
            <button
              data-testid="resource-limits-save"
              onClick={handleSave}
              disabled={isSaving()}
              class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400"
            >
              {isSaving() ? 'Saving...' : 'Save Changes'}
            </button>
            <button
              data-testid="resource-limits-cancel"
              onClick={handleCancel}
              disabled={isSaving()}
              class="px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300"
            >
              Cancel
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
