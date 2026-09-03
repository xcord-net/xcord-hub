import { createSignal, Show } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import { InstanceStatus } from '../types/instance';

interface InstanceActionsProps {
  instanceId: string;
  status: string;
}

export function InstanceActions(props: InstanceActionsProps) {
  const instanceStore = useInstances();
  const [showConfirm, setShowConfirm] = createSignal<'suspend' | 'resume' | 'destroy' | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);
  const [error, setError] = createSignal('');

  const handleAction = async (action: 'suspend' | 'resume' | 'destroy') => {
    setIsLoading(true);
    setError('');
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
    } catch (err: unknown) {
      // A refused lifecycle action used to go to the console and nowhere else:
      // the dialog stayed open on an enabled Confirm button, the status did not
      // move, and an operator watching the screen had no way to tell a failure
      // from a slow one. It is the operator's decision what to do next, so they
      // have to be told.
      setError(
        (err as { message?: string } | null)?.message
          ?? `Could not ${action} this instance. It has been left as it was.`,
      );
    } finally {
      setIsLoading(false);
    }
  };

  // Opening a fresh confirmation must not inherit the last one's error.
  const openConfirm = (action: 'suspend' | 'resume' | 'destroy') => {
    setError('');
    setShowConfirm(action);
  };

  const closeConfirm = () => {
    setError('');
    setShowConfirm(null);
  };

  return (
    <div class="bg-white rounded-lg shadow p-6">
      <h3 class="text-lg font-semibold mb-4">Instance Actions</h3>

      <div class="space-y-3">
        <Show when={props.status === InstanceStatus.Running}>
          <button
            data-testid="instance-action-suspend"
            onClick={() => openConfirm('suspend')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-yellow-600 text-xcord-text-primary rounded hover:bg-yellow-700 disabled:bg-gray-400"
          >
            Suspend Instance
          </button>
        </Show>

        <Show when={props.status === InstanceStatus.Suspended}>
          <button
            data-testid="instance-action-resume"
            onClick={() => openConfirm('resume')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-green-600 text-xcord-text-primary rounded hover:bg-green-700 disabled:bg-gray-400"
          >
            Resume Instance
          </button>
        </Show>

        <Show when={props.status !== InstanceStatus.Destroyed}>
          <button
            data-testid="instance-action-destroy"
            onClick={() => openConfirm('destroy')}
            disabled={isLoading()}
            class="w-full px-4 py-2 bg-red-600 text-xcord-text-primary rounded hover:bg-red-700 disabled:bg-gray-400"
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
            <Show when={error()}>
              <p
                data-testid="instance-action-error"
                role="alert"
                class="mb-4 text-sm text-red-600"
              >
                {error()}
              </p>
            </Show>
            <div class="flex gap-3">
              <button
                data-testid="instance-action-confirm"
                onClick={() => handleAction(showConfirm()!)}
                disabled={isLoading()}
                class="flex-1 px-4 py-2 bg-red-600 text-xcord-text-primary rounded hover:bg-red-700 disabled:bg-gray-400"
              >
                {isLoading() ? 'Processing...' : 'Confirm'}
              </button>
              <button
                data-testid="instance-action-cancel"
                onClick={closeConfirm}
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
