import { Show, For, createSignal, onMount } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import { ResourceLimitsEditor } from './ResourceLimitsEditor';
import { FeatureFlagsEditor } from './FeatureFlagsEditor';
import { InstanceActions } from './InstanceActions';

interface InstanceDetailProps {
  instanceId: string;
  onBack: () => void;
}

export function InstanceDetail(props: InstanceDetailProps) {
  const instanceStore = useInstances();
  const [activeTab, setActiveTab] = createSignal<'overview' | 'health' | 'config' | 'logs'>('overview');

  onMount(async () => {
    await instanceStore.fetchInstanceDetail(props.instanceId);
    await instanceStore.fetchInstanceLogs(props.instanceId);
  });

  const instance = () => instanceStore.selectedInstance;

  return (
    <div>
      <button
        onClick={props.onBack}
        class="mb-4 px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300"
      >
        Back to Instances
      </button>

      <Show when={instance()}>
        <div class="bg-white rounded-lg shadow mb-6">
          <div class="p-6 border-b border-gray-200">
            <h2 class="text-2xl font-bold">{instance()!.displayName}</h2>
            <p class="text-gray-600">{instance()!.domain}</p>
            <div class="mt-2 flex gap-4 text-sm">
              <span>ID: {instance()!.id}</span>
              <span>Owner: {instance()!.ownerUsername}</span>
              <span>Plan: {instance()!.featureTier} / {instance()!.userCountTier}</span>
            </div>
          </div>

          <div class="border-b border-gray-200">
            <nav class="flex">
              <button
                onClick={() => setActiveTab('overview')}
                class={`px-6 py-3 font-medium text-sm border-b-2 ${
                  activeTab() === 'overview'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Overview
              </button>
              <button
                onClick={() => setActiveTab('health')}
                class={`px-6 py-3 font-medium text-sm border-b-2 ${
                  activeTab() === 'health'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Health
              </button>
              <button
                onClick={() => setActiveTab('config')}
                class={`px-6 py-3 font-medium text-sm border-b-2 ${
                  activeTab() === 'config'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Configuration
              </button>
              <button
                onClick={() => setActiveTab('logs')}
                class={`px-6 py-3 font-medium text-sm border-b-2 ${
                  activeTab() === 'logs'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Logs
              </button>
            </nav>
          </div>

          <div class="p-6">
            <Show when={activeTab() === 'overview'}>
              <div class="grid grid-cols-2 gap-4">
                <div>
                  <h4 class="font-semibold mb-2">Status</h4>
                  <p>{instance()!.status}</p>
                </div>
                <div>
                  <h4 class="font-semibold mb-2">Created</h4>
                  <p>{new Date(instance()!.createdAt).toLocaleString()}</p>
                </div>
                <Show when={instance()!.suspendedAt}>
                  <div>
                    <h4 class="font-semibold mb-2">Suspended</h4>
                    <p>{new Date(instance()!.suspendedAt!).toLocaleString()}</p>
                  </div>
                </Show>
                <Show when={instance()!.infrastructure}>
                  <div class="col-span-2">
                    <h4 class="font-semibold mb-2">Infrastructure</h4>
                    <div class="text-sm space-y-1">
                      <p>Container: {instance()!.infrastructure!.containerName}</p>
                      <p>Database: {instance()!.infrastructure!.databaseName}</p>
                      <p>Redis: {instance()!.infrastructure!.redisHost}</p>
                      <p>MinIO: {instance()!.infrastructure!.minioBucket}</p>
                    </div>
                  </div>
                </Show>
              </div>
            </Show>

            <Show when={activeTab() === 'health'}>
              <Show
                when={instance()!.health}
                fallback={<p class="text-gray-500">Health data not available</p>}
              >
                <div class="space-y-4">
                  <div class="flex items-center gap-2">
                    <span class="font-semibold">Status:</span>
                    <span
                      class={`px-2 py-1 rounded text-sm ${
                        instance()!.health!.isHealthy
                          ? 'bg-green-100 text-green-800'
                          : 'bg-red-100 text-red-800'
                      }`}
                    >
                      {instance()!.health!.isHealthy ? 'Healthy' : 'Unhealthy'}
                    </span>
                  </div>
                  <div class="grid grid-cols-2 gap-4">
                    <div>
                      <span class="font-semibold">CPU:</span> {instance()!.health!.cpu.toFixed(1)}%
                    </div>
                    <div>
                      <span class="font-semibold">Memory:</span> {instance()!.health!.memory.toFixed(1)}%
                    </div>
                    <div>
                      <span class="font-semibold">Disk:</span> {instance()!.health!.diskUsage.toFixed(1)}%
                    </div>
                    <div>
                      <span class="font-semibold">Connections:</span> {instance()!.health!.activeConnections}
                    </div>
                  </div>
                  <Show when={instance()!.health!.errors && instance()!.health!.errors!.length > 0}>
                    <div>
                      <h4 class="font-semibold mb-2">Errors</h4>
                      <ul class="list-disc list-inside text-sm text-red-600">
                        <For each={instance()!.health!.errors}>
                          {(error) => <li>{error}</li>}
                        </For>
                      </ul>
                    </div>
                  </Show>
                </div>
              </Show>
            </Show>

            <Show when={activeTab() === 'config'}>
              <div class="space-y-6">
                <ResourceLimitsEditor
                  instanceId={instance()!.id}
                  initialLimits={instance()!.resourceLimits}
                />
                <FeatureFlagsEditor
                  instanceId={instance()!.id}
                  initialFlags={instance()!.featureFlags}
                />
              </div>
            </Show>

            <Show when={activeTab() === 'logs'}>
              <Show
                when={instanceStore.logs.length > 0}
                fallback={<p class="text-gray-500">No logs available</p>}
              >
                <div class="bg-gray-900 text-gray-100 p-4 rounded font-mono text-xs overflow-auto max-h-96">
                  <For each={instanceStore.logs}>
                    {(log) => (
                      <div class="mb-1">
                        <span class="text-gray-500">{log.timestamp}</span>{' '}
                        <span
                          class={
                            log.level === 'Error'
                              ? 'text-red-400'
                              : log.level === 'Warning'
                              ? 'text-yellow-400'
                              : 'text-blue-400'
                          }
                        >
                          [{log.level}]
                        </span>{' '}
                        <span class="text-gray-400">{log.source}:</span> {log.message}
                      </div>
                    )}
                  </For>
                </div>
              </Show>
            </Show>
          </div>
        </div>

        <InstanceActions instanceId={instance()!.id} status={instance()!.status} />
      </Show>
    </div>
  );
}
