import { Show, For, createSignal, onMount, createMemo } from 'solid-js';
import { useInstances } from '../stores/instance.store';
import type { AvailableVersion, ReleaseNotes } from '../types/instance';

interface VersionTabProps {
  instanceId: string;
  currentVersion: string | undefined;
  currentImage: string | undefined;
  instanceStatus: string;
}

function parseReleaseNotes(raw: string | null): ReleaseNotes | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as ReleaseNotes;
  } catch {
    return null;
  }
}

export function VersionTab(props: VersionTabProps) {
  const instanceStore = useInstances();
  const [selectedVersion, setSelectedVersion] = createSignal<AvailableVersion | null>(null);
  const [isLoading, setIsLoading] = createSignal(true);
  const [isUpgrading, setIsUpgrading] = createSignal(false);
  const [upgradeResult, setUpgradeResult] = createSignal<'success' | 'error' | null>(null);
  const [errorMessage, setErrorMessage] = createSignal<string>('');

  onMount(async () => {
    try {
      await instanceStore.fetchVersions();
      const versions = instanceStore.availableVersions;
      if (versions.length > 0) {
        // Pre-select current version if it exists in list, otherwise select latest
        const current = versions.find((v) => v.version === props.currentVersion);
        setSelectedVersion(current ?? versions[0]);
      }
    } catch (error) {
      console.error('Failed to fetch versions:', error);
    } finally {
      setIsLoading(false);
    }
  });

  const releaseNotes = createMemo(() => {
    const version = selectedVersion();
    if (!version) return null;
    return parseReleaseNotes(version.releaseNotes);
  });

  const isCurrentVersion = createMemo(() => {
    const version = selectedVersion();
    if (!version) return true;
    return version.version === props.currentVersion || version.image === props.currentImage;
  });

  const isUpgradeDisabled = createMemo(() => {
    return isCurrentVersion() || props.instanceStatus === 'Upgrading' || isUpgrading();
  });

  const handleUpgrade = async () => {
    const version = selectedVersion();
    if (!version || isUpgradeDisabled()) return;
    setIsUpgrading(true);
    setUpgradeResult(null);
    setErrorMessage('');
    try {
      await instanceStore.upgradeInstance(props.instanceId, version.image);
      setUpgradeResult('success');
    } catch (error) {
      console.error('Failed to upgrade instance:', error);
      setUpgradeResult('error');
      setErrorMessage(error instanceof Error ? error.message : 'Upgrade failed');
    } finally {
      setIsUpgrading(false);
    }
  };

  return (
    <div data-testid="version-tab" class="space-y-4">
      <Show when={isLoading()}>
        <p class="text-gray-500 text-sm">Loading versions...</p>
      </Show>

      <Show when={!isLoading()}>
        <div class="flex gap-6">
          {/* Version list panel */}
          <div class="w-64 flex-shrink-0">
            <div class="mb-2">
              <p class="text-sm text-gray-600">
                Current version:{' '}
                <span class="font-medium text-gray-900">
                  {props.currentVersion ?? 'Unknown'}
                </span>
              </p>
            </div>
            <div
              data-testid="version-list"
              class="border border-gray-200 rounded-lg overflow-hidden"
            >
              <Show
                when={instanceStore.availableVersions.length > 0}
                fallback={
                  <p class="p-4 text-sm text-gray-500">No versions available</p>
                }
              >
                <ul class="divide-y divide-gray-200">
                  <For each={instanceStore.availableVersions}>
                    {(version) => (
                      <li
                        data-testid={`version-item-${version.version}`}
                        onClick={() => setSelectedVersion(version)}
                        class={`px-4 py-3 cursor-pointer hover:bg-gray-50 ${
                          selectedVersion()?.id === version.id
                            ? 'bg-blue-50 border-l-2 border-blue-600'
                            : ''
                        }`}
                      >
                        <div class="flex items-center justify-between">
                          <span class="text-sm font-medium text-gray-900">
                            {version.version}
                          </span>
                          <div class="flex flex-col items-end gap-1">
                            <Show when={version.version === props.currentVersion}>
                              <span class="text-xs bg-green-100 text-green-700 px-1.5 py-0.5 rounded">
                                current
                              </span>
                            </Show>
                            <Show when={version.isMinimumVersion}>
                              <span class="text-xs bg-red-100 text-red-700 px-1.5 py-0.5 rounded">
                                min
                              </span>
                            </Show>
                          </div>
                        </div>
                        <p class="text-xs text-gray-500 mt-0.5">
                          {new Date(version.publishedAt).toLocaleDateString()}
                        </p>
                      </li>
                    )}
                  </For>
                </ul>
              </Show>
            </div>
          </div>

          {/* Release notes panel */}
          <div class="flex-1 min-w-0">
            <Show
              when={selectedVersion()}
              fallback={
                <p class="text-sm text-gray-500">Select a version to view release notes</p>
              }
            >
              <div data-testid="release-notes">
                <div class="flex items-start justify-between mb-4">
                  <div>
                    <h3 class="text-lg font-semibold text-gray-900">
                      Version {selectedVersion()!.version}
                    </h3>
                    <p class="text-xs text-gray-500 font-mono mt-0.5">
                      {selectedVersion()!.image}
                    </p>
                  </div>
                  <div class="flex items-center gap-3">
                    <Show when={upgradeResult() === 'success'}>
                      <span class="text-sm text-green-600">Upgrade initiated.</span>
                    </Show>
                    <Show when={upgradeResult() === 'error'}>
                      <span class="text-sm text-red-600">{errorMessage() || 'Upgrade failed.'}</span>
                    </Show>
                    <button
                      data-testid="update-button"
                      onClick={handleUpgrade}
                      disabled={isUpgradeDisabled()}
                      class={`px-4 py-2 rounded text-sm font-medium ${
                        isUpgradeDisabled()
                          ? 'bg-gray-200 text-gray-400 cursor-not-allowed'
                          : 'bg-blue-600 text-white hover:bg-blue-700'
                      }`}
                    >
                      {isUpgrading()
                        ? 'Upgrading...'
                        : isCurrentVersion()
                        ? 'Current Version'
                        : 'Update to This Version'}
                    </button>
                  </div>
                </div>

                <Show
                  when={releaseNotes()}
                  fallback={
                    <p class="text-sm text-gray-500">No release notes available for this version.</p>
                  }
                >
                  <div class="space-y-4">
                    <Show when={releaseNotes()!.breakingChanges.length > 0}>
                      <div class="bg-red-50 border border-red-200 rounded-lg p-4">
                        <h4 class="text-sm font-semibold text-red-800 mb-2">Breaking Changes</h4>
                        <ul class="list-disc list-inside space-y-1">
                          <For each={releaseNotes()!.breakingChanges}>
                            {(change) => (
                              <li class="text-sm text-red-700">{change}</li>
                            )}
                          </For>
                        </ul>
                      </div>
                    </Show>

                    <Show when={releaseNotes()!.features.length > 0}>
                      <div>
                        <h4 class="text-sm font-semibold text-gray-700 mb-2">New Features</h4>
                        <ul class="space-y-1">
                          <For each={releaseNotes()!.features}>
                            {(item) => (
                              <li class="flex items-start gap-2 text-sm text-gray-700">
                                <span class="text-green-500 mt-0.5">+</span>
                                <span>{item.summary}</span>
                                <span class="text-xs text-gray-400 font-mono ml-auto">
                                  {item.commit.slice(0, 7)}
                                </span>
                              </li>
                            )}
                          </For>
                        </ul>
                      </div>
                    </Show>

                    <Show when={releaseNotes()!.fixes.length > 0}>
                      <div>
                        <h4 class="text-sm font-semibold text-gray-700 mb-2">Bug Fixes</h4>
                        <ul class="space-y-1">
                          <For each={releaseNotes()!.fixes}>
                            {(item) => (
                              <li class="flex items-start gap-2 text-sm text-gray-700">
                                <span class="text-blue-500 mt-0.5">*</span>
                                <span>{item.summary}</span>
                                <span class="text-xs text-gray-400 font-mono ml-auto">
                                  {item.commit.slice(0, 7)}
                                </span>
                              </li>
                            )}
                          </For>
                        </ul>
                      </div>
                    </Show>

                    <Show when={releaseNotes()!.other.length > 0}>
                      <div>
                        <h4 class="text-sm font-semibold text-gray-700 mb-2">Other Changes</h4>
                        <ul class="space-y-1">
                          <For each={releaseNotes()!.other}>
                            {(item) => (
                              <li class="flex items-start gap-2 text-sm text-gray-700">
                                <span class="text-gray-400 mt-0.5 text-xs font-medium uppercase">
                                  {item.type}
                                </span>
                                <span>{item.summary}</span>
                                <span class="text-xs text-gray-400 font-mono ml-auto">
                                  {item.commit.slice(0, 7)}
                                </span>
                              </li>
                            )}
                          </For>
                        </ul>
                      </div>
                    </Show>

                    <Show when={releaseNotes()!.migrationNotes}>
                      <div class="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
                        <h4 class="text-sm font-semibold text-yellow-800 mb-1">Migration Notes</h4>
                        <p class="text-sm text-yellow-700 whitespace-pre-wrap">
                          {releaseNotes()!.migrationNotes}
                        </p>
                      </div>
                    </Show>

                    <Show when={releaseNotes()!.knownIssues}>
                      <div class="bg-gray-50 border border-gray-200 rounded-lg p-4">
                        <h4 class="text-sm font-semibold text-gray-700 mb-1">Known Issues</h4>
                        <p class="text-sm text-gray-600 whitespace-pre-wrap">
                          {releaseNotes()!.knownIssues}
                        </p>
                      </div>
                    </Show>
                  </div>
                </Show>
              </div>
            </Show>
          </div>
        </div>
      </Show>
    </div>
  );
}
