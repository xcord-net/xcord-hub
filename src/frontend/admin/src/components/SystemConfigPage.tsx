import { Show, onMount } from 'solid-js';
import { useSystemConfig } from '../stores/system-config.store';

export function SystemConfigPage() {
  const systemConfig = useSystemConfig();

  onMount(() => {
    systemConfig.fetch();
  });

  const handleTogglePaidServersDisabled = async (e: Event) => {
    const checked = (e.currentTarget as HTMLInputElement).checked;
    await systemConfig.setPaidServersDisabled(checked);
  };

  return (
    <div class="bg-white rounded-lg shadow">
      <div class="p-6 border-b border-gray-200">
        <h2 class="text-xl font-semibold">System Settings</h2>
        <p class="text-sm text-gray-500 mt-1">Hub-wide controls for instance creation and billing</p>
      </div>

      <Show when={!systemConfig.isLoading} fallback={<div class="p-6 text-center">Loading...</div>}>
        <Show when={systemConfig.config}>
          {(cfg) => (
            <div class="p-6 space-y-6">
              <div class="flex items-start justify-between gap-6">
                <div class="flex-1">
                  <label class="block text-sm font-semibold text-gray-900" for="paidServersDisabled">
                    Disable new paid servers
                  </label>
                  <p class="text-sm text-gray-500 mt-1">
                    When enabled, users cannot provision Basic, Pro, or Enterprise tier instances.
                    Free-tier creation is unaffected. Existing paid servers continue to operate.
                  </p>
                </div>
                <label class="relative inline-flex items-center cursor-pointer mt-1">
                  <input
                    id="paidServersDisabled"
                    data-testid="paid-servers-disabled-toggle"
                    type="checkbox"
                    class="sr-only peer"
                    checked={cfg().paidServersDisabled}
                    disabled={systemConfig.isSaving}
                    onChange={handleTogglePaidServersDisabled}
                  />
                  <div class="w-11 h-6 bg-gray-200 peer-focus:outline-none peer-focus:ring-2 peer-focus:ring-blue-300 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-600"></div>
                </label>
              </div>

              <div class="text-xs text-gray-400 border-t pt-4">
                Last updated: {new Date(cfg().updatedAt).toLocaleString()}
              </div>
            </div>
          )}
        </Show>
      </Show>
    </div>
  );
}
