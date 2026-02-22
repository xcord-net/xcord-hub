import { createSignal, createEffect } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { ResourceLimits } from '../types/instance';

interface ResourceLimitsEditorProps {
  instanceId: string;
  initialLimits: ResourceLimits;
}

export function ResourceLimitsEditor(props: ResourceLimitsEditorProps) {
  const instanceStore = useInstances();
  const [limits, setLimits] = createSignal<ResourceLimits>(props.initialLimits);
  const [isEditing, setIsEditing] = createSignal(false);
  const [isSaving, setIsSaving] = createSignal(false);

  createEffect(() => {
    setLimits(props.initialLimits);
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
    setLimits(props.initialLimits);
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
            <label class="block text-sm font-medium mb-1">Max Members</label>
            <input
              type="number"
              value={limits().maxMembers}
              onInput={(e) => updateLimit('maxMembers', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Servers</label>
            <input
              type="number"
              value={limits().maxServers}
              onInput={(e) => updateLimit('maxServers', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Channels per Server</label>
            <input
              type="number"
              value={limits().maxChannelsPerServer}
              onInput={(e) => updateLimit('maxChannelsPerServer', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max File Upload (MB)</label>
            <input
              type="number"
              value={limits().maxFileUploadMb}
              onInput={(e) => updateLimit('maxFileUploadMb', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Storage (GB)</label>
            <input
              type="number"
              value={limits().maxStorageGb}
              onInput={(e) => updateLimit('maxStorageGb', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Max Monthly Bandwidth (GB)</label>
            <input
              type="number"
              value={limits().maxMonthlyBandwidthGb}
              onInput={(e) => updateLimit('maxMonthlyBandwidthGb', parseInt(e.currentTarget.value))}
              disabled={!isEditing()}
              class="w-full px-3 py-2 border rounded disabled:bg-gray-100"
            />
          </div>
        </div>

        {isEditing() && (
          <div class="flex gap-3 pt-2">
            <button
              onClick={handleSave}
              disabled={isSaving()}
              class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400"
            >
              {isSaving() ? 'Saving...' : 'Save Changes'}
            </button>
            <button
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
