import { Show, For, createEffect, createSignal } from 'solid-js';
import { useNavigate, useLocation } from '@solidjs/router';
import { Plus, CreditCard, UserRound, LogOut } from 'lucide-solid';
import { instanceStore } from '../stores/instance.store';
import { unreadStore } from '../stores/unread.store';
import { useAuth } from '../stores/auth.store';
import InstanceIframe from './InstanceIframe';
import AddServerPopover from './AddServerPopover';
import { Icon } from './Icon';
import Logo from './Logo';
import type { JSX } from 'solid-js';

/**
 * The dashboard's own sections, which used to be a left rail. Overview is not
 * here: the mark is Overview, the way Home is in the app's strip.
 */
const SECTIONS = [
  { testid: 'sidebar-nav-create', label: 'New instance', href: '/dashboard/create', icon: Plus, exact: false },
  { testid: 'sidebar-nav-billing', label: 'Billing', href: '/dashboard/billing', icon: CreditCard, exact: false },
  { testid: 'sidebar-nav-account', label: 'Account', href: '/dashboard/account', icon: UserRound, exact: false },
] as const;

/**
 * One strip, one content pane — the same shape as the app's Deck.
 *
 * The dashboard used to be a top tab bar over a left nav rail: two navigation
 * systems stacked, with your instances in one and your account in the other.
 * They are now the same row. Your instances and the dashboard's own sections
 * are all just tabs, and the pane below shows whichever one is selected.
 */
export default function AppShell(props: { children: JSX.Element }) {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // With an instance selected the pane is that instance's iframe; otherwise it
  // is the dashboard route.
  const isHubActive = () => instanceStore.selectedInstanceUrl() === null;

  const isSection = (href: string, exact: boolean) =>
    isHubActive() && (exact ? location.pathname === href : location.pathname.startsWith(href));

  const openSection = (href: string) => {
    instanceStore.selectInstance(null);
    navigate(href);
  };

  const [addPopoverOpen, setAddPopoverOpen] = createSignal(false);

  // Selecting an instance keeps the URL on /dashboard so a reload lands back in
  // the shell rather than on a route the iframe does not own.
  createEffect(() => {
    if (instanceStore.selectedInstanceUrl() !== null && !location.pathname.startsWith('/dashboard')) {
      navigate('/dashboard');
    }
  });

  const tabClass = (active: boolean) =>
    `flex items-center gap-2 px-3 py-1.5 rounded text-sm font-medium transition whitespace-nowrap ${
      active
        ? 'bg-xcord-bg-accent text-xcord-text-primary'
        : 'text-xcord-text-muted hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary'
    }`;

  return (
    <div class="xcord-grid-ground flex flex-col h-screen bg-xcord-bg-primary">
      <header
        data-testid="hub-strip"
        class="flex items-center h-12 bg-xcord-bg-floating border-b border-xcord-bg-tertiary px-2 gap-1 shrink-0 overflow-x-auto"
      >
        {/* The mark returns to Overview, the way Home does in the app. */}
        <button
          data-testid="sidebar-nav-overview"
          onClick={() => openSection('/dashboard')}
          class={`${tabClass(isSection('/dashboard', true))} shrink-0`}
          title="Overview"
        >
          <Logo />
        </button>

        <For each={SECTIONS}>
          {(section) => (
            <button
              data-testid={section.testid}
              onClick={() => openSection(section.href)}
              class={`${tabClass(isSection(section.href, section.exact))} shrink-0`}
              title={section.label}
            >
              <Icon icon={section.icon} />
              <span class="hidden sm:inline">{section.label}</span>
            </button>
          )}
        </For>

        {/* A hairline, not a gap: your instances are a different kind of thing
            from the dashboard's own sections, but they live in the same row. */}
        <Show when={instanceStore.connectedInstances().length > 0}>
          <span class="w-px h-5 bg-xcord-bg-tertiary mx-1 shrink-0" aria-hidden="true" />
        </Show>

        <For each={instanceStore.connectedInstances()}>
          {(instance) => {
            const unreadCount = () => unreadStore.getUnreadCount(instance.url);
            const isSelected = () => instanceStore.selectedInstanceUrl() === instance.url;

            return (
              <button
                data-testid="hub-instance-tab"
                onClick={() => instanceStore.selectInstance(instance.url)}
                class={`relative shrink-0 ${tabClass(isSelected())}`}
              >
                {instance.name}
                <Show when={unreadCount() > 0 && !isSelected()}>
                  <span class="absolute -top-1 -right-1 bg-xcord-red text-xcord-text-primary rounded-full h-4 min-w-4 px-1 flex items-center justify-center text-[10px]">
                    {unreadCount()}
                  </span>
                </Show>
              </button>
            );
          }}
        </For>

        <div class="relative shrink-0">
          <button
            data-testid="hub-add-instance"
            onClick={() => setAddPopoverOpen(!addPopoverOpen())}
            class="px-3 py-1.5 rounded text-sm text-xcord-text-muted hover:bg-xcord-bg-accent/50 hover:text-xcord-text-primary transition"
            title="Connect a server"
            aria-label="Connect a server"
          >
            +
          </button>
          <AddServerPopover open={addPopoverOpen()} onClose={() => setAddPopoverOpen(false)} />
        </div>

        <div class="flex-1" />

        {/* Who you are signed in as. It opens Account too, so the identity and
            the place you change it are the same control. */}
        <button
          data-testid="hub-account-avatar"
          onClick={() => openSection('/dashboard/account')}
          class="w-8 h-8 shrink-0 rounded-full bg-xcord-brand flex items-center justify-center text-xcord-landing-bg text-sm font-medium hover:opacity-80 transition"
          title="Your account"
          aria-label="Your account"
        >
          <Show when={auth.user} fallback="?">
            {auth.user!.displayName[0].toUpperCase()}
          </Show>
        </button>

        <button
          data-testid="hub-logout-button"
          onClick={() => { auth.logout(); window.location.href = '/'; }}
          class="flex items-center gap-2 shrink-0 px-3 py-1.5 rounded text-sm text-xcord-text-muted hover:text-xcord-red hover:bg-xcord-bg-accent/50 transition"
          title="Log out"
        >
          <Icon icon={LogOut} />
          <span class="hidden sm:inline">Log out</span>
        </button>
      </header>

      <div class="flex-1 relative overflow-hidden">
        <div class="absolute inset-0" style={{ display: isHubActive() ? 'block' : 'none' }}>
          <main class="h-full overflow-y-auto">{props.children}</main>
        </div>

        {/* Instances stay mounted and are shown or hidden, so switching back to
            one does not reload it and lose your place. */}
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
