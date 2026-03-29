import { A, useLocation } from '@solidjs/router';
import { Show, createSignal, createEffect } from 'solid-js';
import { useAuth } from '../stores/auth.store';

const [sidebarOpen, setSidebarOpen] = createSignal(false);
export { sidebarOpen, setSidebarOpen };

export default function DashboardSidebar() {
  const auth = useAuth();
  const location = useLocation();

  const isActive = (path: string) => {
    if (path === '/dashboard') {
      return location.pathname === '/dashboard';
    }
    return location.pathname.startsWith(path);
  };

  const linkClass = (path: string) =>
    `flex items-center gap-3 px-3 py-2 rounded text-sm font-medium transition ${
      isActive(path)
        ? 'bg-xcord-bg-accent text-xcord-text-primary'
        : 'text-xcord-text-secondary hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary'
    }`;

  // Close sidebar on route change (mobile)
  createEffect(() => {
    location.pathname;
    setSidebarOpen(false);
  });

  return (
    <>
      {/* Backdrop (mobile only) */}
      <Show when={sidebarOpen()}>
        <div
          class="fixed inset-0 bg-black/50 z-20 md:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      </Show>

      <aside class={`
        fixed inset-y-0 left-0 z-30 w-60 bg-xcord-bg-secondary flex flex-col shrink-0
        transform transition-transform duration-200 ease-in-out
        md:relative md:translate-x-0 md:z-auto
        ${sidebarOpen() ? 'translate-x-0' : '-translate-x-full'}
      `}>
        {/* Close button (mobile only) */}
        <div class="flex items-center justify-end p-2 md:hidden">
          <button
            onClick={() => setSidebarOpen(false)}
            class="p-1 text-xcord-text-muted hover:text-xcord-text-primary transition"
          >
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <nav class="flex-1 p-3 space-y-1">
          <A data-testid="sidebar-nav-overview" href="/dashboard" class={linkClass('/dashboard')}>
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
            </svg>
            Overview
          </A>
          <A data-testid="sidebar-nav-create" href="/dashboard/create" class={linkClass('/dashboard/create')}>
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
            </svg>
            Create Instance
          </A>
          <A data-testid="sidebar-nav-billing" href="/dashboard/billing" class={linkClass('/dashboard/billing')}>
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
            </svg>
            Billing
          </A>
          <A data-testid="sidebar-nav-account" href="/dashboard/account" class={linkClass('/dashboard/account')}>
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
            </svg>
            Account
          </A>
        </nav>

        {/* User info + logout */}
        <div class="p-3 border-t border-xcord-bg-tertiary">
          <Show when={auth.user}>
            <div class="flex items-center gap-3 px-3 py-2">
              <div class="w-8 h-8 rounded-full bg-xcord-brand flex items-center justify-center text-white text-sm font-medium shrink-0">
                {auth.user!.displayName[0].toUpperCase()}
              </div>
              <div class="min-w-0">
                <div class="text-sm font-medium text-xcord-text-primary truncate">{auth.user!.displayName}</div>
                <div class="text-xs text-xcord-text-muted truncate">{auth.user!.email}</div>
              </div>
            </div>
          </Show>
          <button
            data-testid="hub-logout-button"
            onClick={() => { auth.logout(); window.location.href = '/'; }}
            class="w-full mt-1 px-3 py-2 text-sm text-xcord-text-secondary hover:text-xcord-red hover:bg-xcord-bg-accent/50 rounded transition text-left"
          >
            Log out
          </button>
        </div>
      </aside>
    </>
  );
}
