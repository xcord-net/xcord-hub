import { createSignal, Show } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import { InstanceStatus } from '../types/instance';

interface InstanceActionsProps {
  instanceId: string;
  status: InstanceStatus;
}

export function InstanceActions(props: InstanceActionsProps) {
  const instanceStore = useInstances();
  const [showConfirm, setShowConfirm] = createSignal<'suspend' | 'resume' | 'destroy' | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);

  const handleAction = async (action: 'suspend' | 'resume' | 'destroy') => {
    setIsLoading(true);
    try {
      switch (action) {
        case 'suspend':
          await instanceStore.suspendInstance(props.instanceId);
          break;
        case 'resume':
          await instanceStore.resumeInstance(props.instanceId);
          break;
        case 'destroy':
          await instanceStore.destroyInstance(props.instanceId);
          break;
      }
      setShowConfirm(null);
    } catch (error) {
      console.error('Action failed:', error);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <h3 class="text-lg font-semibold mb-4">Instance Actions</h3>

      <div class="space-y-3">
        <Show when={props.status === InstanceStatus.Running}>
          <button
            onClick={() => setShowConfirm('suspend')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-yellow-600 text-white rounded hover:bg-yellow-700 disabled:bg-gray-400"
          >
            Suspend Instance
          </button>
        </Show>

        <Show when={props.status === InstanceStatus.Suspended}>
          <button
            onClick={() => setShowConfirm('resume')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700 disabled:bg-gray-400"
          >
            Resume Instance
          </button>
        </Show>

        <Show when={props.status !== InstanceStatus.Destroyed}>
          <button
            onClick={() => setShowConfirm('destroy')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 disabled:bg-gray-400"
          >
            Destroy Instance
          </button>
        </Show>
      </div>

      <Show when={showConfirm()}>
        <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div class="bg-white rounded-lg p-6 max-w-md w-full mx-4">
            <h4 class="text-lg font-semibold mb-2">Confirm Action</h4>
            <p class="text-gray-600 mb-4">
              Are you sure you want to {showConfirm()} this instance?
              <Show when={showConfirm() === 'destroy'}>
                <strong class="text-red-600"> This action cannot be undone.</strong>
              </Show>
            </p>
            <div class="flex gap-3">
              <button
                onClick={() => handleAction(showConfirm()!)}
                disabled={isLoading()}
                class="flex-1 px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 disabled:bg-gray-400"
              >
                {isLoading() ? 'Processing...' : 'Confirm'}
              </button>
              <button
                onClick={() => setShowConfirm(null)}
                disabled={isLoading()}
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
