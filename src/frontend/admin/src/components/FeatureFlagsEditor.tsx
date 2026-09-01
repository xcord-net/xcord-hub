import { createSignal, createEffect } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { FeatureFlags } from '../types/instance';

interface FeatureFlagsEditorProps {
  instanceId: string;
  initialFlags: FeatureFlags;
}

/**
 * The hub stores FeatureFlagsJson with PascalCase keys (JsonSerializer defaults)
 * and the admin API hands that JSON back verbatim, so what arrives here is
 * `CanUseVoiceChannels`, not `canUseVoiceChannels`. Normalise on the way in;
 * outbound camelCase binds fine, since ASP.NET matches property names
 * case-insensitively.
 */
function normaliseFlags(raw: FeatureFlags | Record<string, unknown> | undefined | null): FeatureFlags {
  const source = (raw ?? {}) as Record<string, unknown>;
  const read = (name: string) =>
    Boolean(source[name] ?? source[name.charAt(0).toUpperCase() + name.slice(1)]);
  return {
    canUseVoiceChannels: read('canUseVoiceChannels'),
    canUseVideoChannels: read('canUseVideoChannels'),
    canUseSimulcast: read('canUseSimulcast'),
    canUseMemberTiers: read('canUseMemberTiers'),
    canBroadcast: read('canBroadcast'),
  };
}

export function FeatureFlagsEditor(props: FeatureFlagsEditorProps) {
  const instanceStore = useInstances();
  const [flags, setFlags] = createSignal<FeatureFlags>(normaliseFlags(props.initialFlags));
  const [isEditing, setIsEditing] = createSignal(false);
  const [isSaving, setIsSaving] = createSignal(false);

  createEffect(() => {
    setFlags(normaliseFlags(props.initialFlags));
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
    setFlags(normaliseFlags(props.initialFlags));
    setIsEditing(false);
  };

  const toggleFlag = (key: keyof FeatureFlags) => {
    setFlags({ ...flags(), [key]: !flags()[key] });
  };

  const featureFlagLabels: Record<keyof FeatureFlags, string> = {
    canUseVoiceChannels: 'Voice Channels',
    canUseVideoChannels: 'Video Channels',
    canUseSimulcast: 'Simulcast',
    canUseMemberTiers: 'Member Tiers',
    canBroadcast: 'Broadcast',
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-lg font-semibold">Feature Flags</h3>
        {!isEditing() && (
          <button
            data-testid="feature-flags-edit"
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
              data-testid={`feature-flag-${key}`}
              data-enabled={flags()[key as keyof FeatureFlags] ? 'true' : 'false'}
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
              data-testid="feature-flags-save"
              onClick={handleSave}
              disabled={isSaving()}
              class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400"
            >
              {isSaving() ? 'Saving...' : 'Save Changes'}
            </button>
            <button
              data-testid="feature-flags-cancel"
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
