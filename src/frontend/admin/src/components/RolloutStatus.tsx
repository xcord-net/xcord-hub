import { Show, For, createSignal, onMount } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { UpgradeRollout } from '../types/instance';

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'InProgress':
      return 'bg-blue-100 text-blue-800';
    case 'Paused':
      return 'bg-yellow-100 text-yellow-800';
    case 'Pending':
      return 'bg-gray-100 text-gray-700';
    case 'Completed':
      return 'bg-green-100 text-green-800';
    case 'Failed':
      return 'bg-red-100 text-red-800';
    case 'Cancelled':
      return 'bg-gray-100 text-gray-500';
    default:
      return 'bg-gray-100 text-gray-700';
  }
}

function progressPercent(rollout: UpgradeRollout): number {
  if (rollout.totalInstances === 0) return 0;
  return Math.round((rollout.completedInstances / rollout.totalInstances) * 100);
}

function imageTag(image: string): string {
  const parts = image.split(':');
  return parts.length > 1 ? parts[parts.length - 1] : image;
}

export function RolloutStatus() {
  const instanceStore = useInstances();
  const [actionPending, setActionPending] = createSignal<string | null>(null);

  onMount(async () => {
    try {
      await instanceStore.fetchActiveRollouts();
    } catch (error) {
      console.error('Failed to fetch active rollouts:', error);
    }
  });

  const handlePause = async (rolloutId: string) => {
    setActionPending(rolloutId);
    try {
      await instanceStore.pauseRollout(rolloutId);
      await instanceStore.fetchActiveRollouts();
    } catch (error) {
      console.error('Failed to pause rollout:', error);
    } finally {
      setActionPending(null);
    }
  };

  const handleResume = async (rolloutId: string) => {
    setActionPending(rolloutId);
    try {
      await instanceStore.resumeRollout(rolloutId);
      await instanceStore.fetchActiveRollouts();
    } catch (error) {
      console.error('Failed to resume rollout:', error);
    } finally {
      setActionPending(null);
    }
  };

  const handleCancel = async (rolloutId: string) => {
    setActionPending(rolloutId);
    try {
      await instanceStore.cancelRollout(rolloutId);
      await instanceStore.fetchActiveRollouts();
    } catch (error) {
      console.error('Failed to cancel rollout:', error);
    } finally {
      setActionPending(null);
    }
  };

  return (
    <Show when={instanceStore.activeRollouts.length > 0}>
      <div data-testid="rollout-status-banner" class="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-4">
        <h3 class="text-sm font-semibold text-blue-900 mb-3">Active Rollouts</h3>
        <div class="space-y-3">
          <For each={instanceStore.activeRollouts}>
            {(rollout) => (
              <div
                data-testid={`rollout-item-${rollout.id}`}
                class="bg-white rounded-lg border border-blue-100 p-3"
              >
                <div class="flex items-center justify-between mb-2">
                  <div class="flex items-center gap-2">
                    <span class="text-sm font-medium text-gray-900">
                      {rollout.fromImage ? `${imageTag(rollout.fromImage)} ` : 'All instances '}
                      &rarr; {imageTag(rollout.toImage)}
                    </span>
                    <Show when={rollout.targetPool}>
                      <span class="text-xs text-gray-500">
                        (pool: {rollout.targetPool})
                      </span>
                    </Show>
                  </div>
                  <div class="flex items-center gap-2">
                    <span
                      data-testid={`rollout-status-${rollout.id}`}
                      class={`px-2 py-0.5 text-xs font-medium rounded ${statusBadgeClass(rollout.status)}`}
                    >
                      {rollout.status}
                    </span>
                    <Show when={rollout.status === 'InProgress'}>
                      <button
                        data-testid={`rollout-pause-${rollout.id}`}
                        onClick={() => handlePause(rollout.id)}
                        disabled={actionPending() === rollout.id}
                        class="px-2 py-1 text-xs bg-yellow-100 text-yellow-800 rounded hover:bg-yellow-200 disabled:opacity-50"
                      >
                        Pause
                      </button>
                    </Show>
                    <Show when={rollout.status === 'Paused'}>
                      <button
                        data-testid={`rollout-resume-${rollout.id}`}
                        onClick={() => handleResume(rollout.id)}
                        disabled={actionPending() === rollout.id}
                        class="px-2 py-1 text-xs bg-green-100 text-green-800 rounded hover:bg-green-200 disabled:opacity-50"
                      >
                        Resume
                      </button>
                    </Show>
                    <Show when={rollout.status === 'InProgress' || rollout.status === 'Paused' || rollout.status === 'Pending'}>
                      <button
                        data-testid={`rollout-cancel-${rollout.id}`}
                        onClick={() => handleCancel(rollout.id)}
                        disabled={actionPending() === rollout.id}
                        class="px-2 py-1 text-xs bg-red-100 text-red-800 rounded hover:bg-red-200 disabled:opacity-50"
                      >
                        Cancel
                      </button>
                    </Show>
                  </div>
                </div>

                {/* Progress bar */}
                <div class="flex items-center gap-2">
                  <div class="flex-1 bg-gray-200 rounded-full h-2 overflow-hidden">
                    <div
                      data-testid={`rollout-progress-${rollout.id}`}
                      class="bg-blue-500 h-2 rounded-full transition-all"
                      style={{ width: `${progressPercent(rollout)}%` }}
                    />
                  </div>
                  <span class="text-xs text-gray-600 whitespace-nowrap">
                    {rollout.completedInstances}/{rollout.totalInstances}
                    <Show when={rollout.failedInstances > 0}>
                      {' '}
                      <span class="text-red-600">({rollout.failedInstances} failed)</span>
                    </Show>
                  </span>
                </div>

                <div class="flex items-center justify-between mt-1">
                  <p class="text-xs text-gray-500">
                    Batch size: {rollout.batchSize} | Max failures: {rollout.maxFailures}
                  </p>
                  <Show when={rollout.scheduledAt}>
                    <p class="text-xs text-gray-500">
                      Scheduled: {new Date(rollout.scheduledAt!).toLocaleString()}
                    </p>
                  </Show>
                </div>
              </div>
            )}
          </For>
        </div>
      </div>
    </Show>
  );
}
