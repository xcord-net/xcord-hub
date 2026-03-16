import { Show, For, createSignal, onMount } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { StartRolloutRequest } from '../types/instance';

interface FleetUpgradeProps {
  isOpen: boolean;
  onClose: () => void;
}

export function FleetUpgrade(props: FleetUpgradeProps) {
  const instanceStore = useInstances();
  const [toImage, setToImage] = createSignal('');
  const [fromImage, setFromImage] = createSignal('');
  const [targetPool, setTargetPool] = createSignal('');
  const [batchSize, setBatchSize] = createSignal(5);
  const [maxFailures, setMaxFailures] = createSignal(1);
  const [scheduledAt, setScheduledAt] = createSignal('');
  const [force, setForce] = createSignal(false);
  const [isSubmitting, setIsSubmitting] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);
  const [isVersionsLoading, setIsVersionsLoading] = createSignal(false);

  onMount(async () => {
    if (instanceStore.availableVersions.length === 0) {
      setIsVersionsLoading(true);
      try {
        await instanceStore.fetchVersions();
        const versions = instanceStore.availableVersions;
        if (versions.length > 0) {
          setToImage(versions[0].image);
        }
      } catch (err) {
        console.error('Failed to fetch versions:', err);
      } finally {
        setIsVersionsLoading(false);
      }
    } else {
      const versions = instanceStore.availableVersions;
      if (versions.length > 0) {
        setToImage(versions[0].image);
      }
    }
  });

  const handleSubmit = async () => {
    if (!toImage()) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const request: StartRolloutRequest = {
        toImage: toImage(),
        force: force(),
        batchSize: batchSize(),
        maxFailures: maxFailures(),
      };
      if (fromImage()) request.fromImage = fromImage();
      if (targetPool()) request.targetPool = targetPool();
      if (scheduledAt()) request.scheduledAt = new Date(scheduledAt()).toISOString();

      await instanceStore.startRollout(request);
      await instanceStore.fetchActiveRollouts();
      props.onClose();
    } catch (err) {
      console.error('Failed to start rollout:', err);
      setError(err instanceof Error ? err.message : 'Failed to start rollout');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Show when={props.isOpen}>
      <div
        data-testid="fleet-upgrade-dialog"
        class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
        onClick={(e) => { if (e.target === e.currentTarget) props.onClose(); }}
      >
        <div class="bg-white rounded-lg shadow-xl max-w-lg w-full mx-4 max-h-[90vh] overflow-y-auto">
          <div class="p-6 border-b border-gray-200">
            <h2 class="text-lg font-semibold text-gray-900">Start Fleet Upgrade</h2>
            <p class="text-sm text-gray-600 mt-1">
              Roll out a version upgrade to instances in batches.
            </p>
          </div>

          <div class="p-6 space-y-4">
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">
                Target Version <span class="text-red-500">*</span>
              </label>
              <Show when={isVersionsLoading()}>
                <p class="text-sm text-gray-500">Loading versions...</p>
              </Show>
              <Show when={!isVersionsLoading()}>
                <Show
                  when={instanceStore.availableVersions.length > 0}
                  fallback={
                    <input
                      data-testid="fleet-upgrade-to-image"
                      type="text"
                      value={toImage()}
                      onInput={(e) => setToImage(e.currentTarget.value)}
                      placeholder="docker.xcord.net/fed:1.2.3"
                      class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                  }
                >
                  <select
                    data-testid="fleet-upgrade-to-image"
                    value={toImage()}
                    onChange={(e) => setToImage(e.currentTarget.value)}
                    class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  >
                    <For each={instanceStore.availableVersions}>
                      {(version) => (
                        <option value={version.image}>
                          {version.version} - {version.image}
                        </option>
                      )}
                    </For>
                  </select>
                </Show>
              </Show>
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">
                From Image (optional - only upgrade instances on this image)
              </label>
              <input
                data-testid="fleet-upgrade-from-image"
                type="text"
                value={fromImage()}
                onInput={(e) => setFromImage(e.currentTarget.value)}
                placeholder="Leave blank to upgrade all instances"
                class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">
                Target Pool (optional)
              </label>
              <input
                data-testid="fleet-upgrade-target-pool"
                type="text"
                value={targetPool()}
                onInput={(e) => setTargetPool(e.currentTarget.value)}
                placeholder="Filter by pool name"
                class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div class="grid grid-cols-2 gap-4">
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Batch Size</label>
                <input
                  data-testid="fleet-upgrade-batch-size"
                  type="number"
                  min="1"
                  value={batchSize()}
                  onInput={(e) => setBatchSize(parseInt(e.currentTarget.value) || 1)}
                  class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <p class="text-xs text-gray-500 mt-1">Instances to upgrade per batch</p>
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Max Failures</label>
                <input
                  data-testid="fleet-upgrade-max-failures"
                  type="number"
                  min="0"
                  value={maxFailures()}
                  onInput={(e) => setMaxFailures(parseInt(e.currentTarget.value) || 0)}
                  class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <p class="text-xs text-gray-500 mt-1">Failures before pausing rollout</p>
              </div>
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">
                Scheduled Start (optional)
              </label>
              <input
                data-testid="fleet-upgrade-scheduled-at"
                type="datetime-local"
                value={scheduledAt()}
                onInput={(e) => setScheduledAt(e.currentTarget.value)}
                class="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div class="flex items-center gap-3">
              <button
                data-testid="fleet-upgrade-force-toggle"
                onClick={() => setForce(!force())}
                class={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors cursor-pointer ${
                  force() ? 'bg-orange-500' : 'bg-gray-300'
                }`}
              >
                <span
                  class={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                    force() ? 'translate-x-6' : 'translate-x-1'
                  }`}
                />
              </button>
              <div>
                <span class="text-sm font-medium text-gray-700">Force upgrade</span>
                <p class="text-xs text-gray-500">Upgrade instances already on the target image</p>
              </div>
            </div>

            <Show when={error()}>
              <div class="bg-red-50 border border-red-200 rounded p-3">
                <p class="text-sm text-red-700">{error()}</p>
              </div>
            </Show>
          </div>

          <div class="p-6 border-t border-gray-200 flex justify-end gap-3">
            <button
              data-testid="fleet-upgrade-cancel"
              onClick={props.onClose}
              disabled={isSubmitting()}
              class="px-4 py-2 text-sm text-gray-700 bg-gray-100 rounded hover:bg-gray-200 disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              data-testid="fleet-upgrade-submit"
              onClick={handleSubmit}
              disabled={!toImage() || isSubmitting()}
              class="px-4 py-2 text-sm text-white bg-blue-600 rounded hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed"
            >
              {isSubmitting() ? 'Starting...' : 'Start Rollout'}
            </button>
          </div>
        </div>
      </div>
    </Show>
  );
}
