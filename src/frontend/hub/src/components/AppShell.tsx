import { Show, For, createEffect } from 'solid-js';
import { A, useNavigate, useLocation } from '@solidjs/router';
import { instanceStore } from '../stores/instance.store';
import { unreadStore } from '../stores/unread.store';
import { useAuth } from '../stores/auth.store';
import InstanceIframe from './InstanceIframe';
import DashboardSidebar from './DashboardSidebar';
import type { JSX } from 'solid-js';

export default function AppShell(props: { children: JSX.Element }) {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // When an instance is selected, we show the iframe; when null, show dashboard
  const isHubActive = () => instanceStore.selectedInstanceUrl() === null;

  const handleSelectHub = () => {
    instanceStore.selectInstance(null);
    // Only navigate if not already on a dashboard route
    if (!location.pathname.startsWith('/dashboard')) {
      navigate('/dashboard');
    }
  };

  const handleSelectInstance = (url: string) => {
    instanceStore.selectInstance(url);
  };

  const handleAddInstance = () => {
    instanceStore.selectInstance(null);
    navigate('/dashboard/create');
  };

  // If an instance gets selected, ensure we stay on dashboard path for URL consistency
  createEffect(() => {
    if (instanceStore.selectedInstanceUrl() !== null && !location.pathname.startsWith('/dashboard')) {
      navigate('/dashboard');
    }
  });

  return (
    <div class="flex flex-col h-screen bg-xcord-bg-primary">
      {/* Header bar */}
      <header class="flex items-center h-12 bg-xcord-bg-floating border-b border-xcord-bg-tertiary px-2 gap-1 shrink-0">
        {/* Hub tab */}
        <button
          onClick={handleSelectHub}
          class={`px-4 py-1.5 rounded text-sm font-medium transition ${
            isHubActive()
              ? 'bg-xcord-bg-accent text-xcord-text-primary'
              : 'text-xcord-text-muted hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary'
          }`}
        >
          xcord
        </button>

        {/* Instance tabs */}
        <For each={instanceStore.connectedInstances()}>
          {(instance) => {
            const unreadCount = () => unreadStore.getUnreadCount(instance.url);
            const isSelected = () => instanceStore.selectedInstanceUrl() === instance.url;

            return (
              <button
                onClick={() => handleSelectInstance(instance.url)}
                class={`relative px-4 py-1.5 rounded text-sm font-medium transition ${
                  isSelected()
                    ? 'bg-xcord-bg-accent text-xcord-text-primary'
                    : 'text-xcord-text-muted hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary'
                }`}
              >
                {instance.name}
                <Show when={unreadCount() > 0 && !isSelected()}>
                  <span class="absolute -top-1 -right-1 bg-xcord-red text-white text-xs rounded-full h-4 min-w-4 px-1 flex items-center justify-center text-[10px]">
                    {unreadCount()}
                  </span>
                </Show>
              </button>
            );
          }}
        </For>

        {/* Add button */}
        <button
          onClick={handleAddInstance}
          class="px-3 py-1.5 rounded text-sm text-xcord-text-muted hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary transition"
          title="Add server"
        >
          +
        </button>

        {/* Spacer */}
        <div class="flex-1" />

        {/* Profile avatar */}
        <button
          onClick={() => { instanceStore.selectInstance(null); navigate('/dashboard/account'); }}
          class="w-8 h-8 rounded-full bg-xcord-brand flex items-center justify-center text-white text-sm font-medium hover:opacity-80 transition"
          title="Account"
        >
          <Show when={auth.user} fallback="?">
            {auth.user!.displayName[0].toUpperCase()}
          </Show>
        </button>
      </header>

      {/* Content area */}
      <div class="flex-1 relative overflow-hidden">
        {/* Dashboard content (sidebar + page) — visible when hub tab active */}
        <div
          class="absolute inset-0 flex"
          style={{ display: isHubActive() ? 'flex' : 'none' }}
        >
          <DashboardSidebar />
          <main class="flex-1 overflow-y-auto">
            {props.children}
          </main>
        </div>

        {/* Instance iframes — all stay loaded, display toggled */}
        <For each={instanceStore.connectedInstances()}>
          {(instance) => (
            <div
              class="absolute inset-0"
              style={{
                display: instanceStore.selectedInstanceUrl() === instance.url ? 'block' : 'none',
              }}
            >
              <InstanceIframe
                url={instance.url}
                visible={instanceStore.selectedInstanceUrl() === instance.url}
              />
            </div>
          )}
        </For>
      </div>
    </div>
  );
}
