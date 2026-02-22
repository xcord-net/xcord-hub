import { createSignal, createEffect } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { FeatureFlags } from '../types/instance';

interface FeatureFlagsEditorProps {
  instanceId: string;
  initialFlags: FeatureFlags;
}

export function FeatureFlagsEditor(props: FeatureFlagsEditorProps) {
  const instanceStore = useInstances();
  const [flags, setFlags] = createSignal<FeatureFlags>(props.initialFlags);
  const [isEditing, setIsEditing] = createSignal(false);
  const [isSaving, setIsSaving] = createSignal(false);

  createEffect(() => {
    setFlags(props.initialFlags);
  });

  const handleSave = async () => {
    setIsSaving(true);
    try {
      await instanceStore.updateFeatureFlags(props.instanceId, flags());
      setIsEditing(false);
    } catch (error) {
      console.error('Failed to update flags:', error);
    } finally {
      setIsSaving(false);
    }
  };

  const handleCancel = () => {
    setFlags(props.initialFlags);
    setIsEditing(false);
  };

  const toggleFlag = (key: keyof FeatureFlags) => {
    setFlags({ ...flags(), [key]: !flags()[key] });
  };

  const featureFlagLabels: Record<keyof FeatureFlags, string> = {
    allowCustomEmoji: 'Custom Emoji',
    allowVoiceChannels: 'Voice Channels',
    allowVideoStreaming: 'Video Streaming',
    allowBots: 'Bots',
    allowWebhooks: 'Webhooks',
    allowAutomod: 'Auto Moderation',
    allowServerDiscovery: 'Server Discovery',
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-lg font-semibold">Feature Flags</h3>
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
        {Object.entries(featureFlagLabels).map(([key, label]) => (
          <div class="flex items-center justify-between">
            <label class="text-sm font-medium">{label}</label>
            <button
              onClick={() => isEditing() && toggleFlag(key as keyof FeatureFlags)}
              disabled={!isEditing()}
              class={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                flags()[key as keyof FeatureFlags] ? 'bg-blue-600' : 'bg-gray-300'
              } ${!isEditing() ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'}`}
            >
              <span
                class={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                  flags()[key as keyof FeatureFlags] ? 'translate-x-6' : 'translate-x-1'
                }`}
              />
            </button>
          </div>
        ))}

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
