import { Show, onMount, createSignal } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { useAuth } from '../stores/auth.store';
import type { JSX } from 'solid-js';

export default function HubGuard(props: { children: JSX.Element }) {
  const auth = useAuth();
  const navigate = useNavigate();
  const [ready, setReady] = createSignal(false);

  onMount(async () => {
    if (!auth.isAuthenticated) {
      await auth.restoreSession();
    }
    if (!auth.isAuthenticated) {
      navigate('/login', { replace: true });
    } else {
      setReady(true);
    }
  });

  return (
    <Show
      when={ready()}
      fallback={
        <div class="flex items-center justify-center h-screen bg-xcord-bg-primary">
          <div class="flex flex-col items-center gap-3">
            <div class="w-8 h-8 border-2 border-xcord-brand border-t-transparent rounded-full animate-spin" />
            <span class="text-xcord-text-muted text-sm">Loading...</span>
          </div>
        </div>
      }
    >
      {props.children}
    </Show>
  );
}
