import { Show } from 'solid-js';
import type { Accessor, Setter } from 'solid-js';
import type { NotifyStatus } from './state';

interface NotifyModalProps {
  notifyTier: Accessor<string | null>;
  setNotifyTier: Setter<string | null>;
  notifyEmail: Accessor<string>;
  setNotifyEmail: Setter<string>;
  notifyStatus: Accessor<NotifyStatus>;
  notifyMessage: Accessor<string>;
  onSubmit: (e: Event) => void;
}

export default function NotifyModal(props: NotifyModalProps) {
  return (
    <Show when={props.notifyTier()}>
      <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4" onClick={(e) => { if (e.target === e.currentTarget) props.setNotifyTier(null); }}>
        <div class="bg-xcord-bg-primary rounded-lg w-full max-w-sm p-6">
          <div class="flex items-center justify-between mb-4">
            <h3 class="text-lg font-bold text-xcord-text-primary">{props.notifyTier()}</h3>
            <button onClick={() => props.setNotifyTier(null)} class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none">&times;</button>
          </div>
          <Show when={props.notifyStatus() !== 'success'} fallback={<p class="text-sm text-xcord-green py-4 text-center">{props.notifyMessage()}</p>}>
            <p class="text-sm text-xcord-text-muted mb-4">We'll let you know when {props.notifyTier()} is available.</p>
            <form onSubmit={props.onSubmit} class="space-y-3">
              <input type="email" required placeholder="you@example.com" value={props.notifyEmail()} onInput={(e) => props.setNotifyEmail(e.currentTarget.value)} class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand text-sm" />
              <button type="submit" disabled={props.notifyStatus() === 'loading'} class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-xcord-landing-bg rounded font-medium transition text-sm">
                {props.notifyStatus() === 'loading' ? 'Submitting...' : 'Notify Me'}
              </button>
            </form>
            <Show when={props.notifyStatus() === 'error'}>
              <p class="text-xs text-xcord-red mt-2">{props.notifyMessage()}</p>
            </Show>
          </Show>
        </div>
      </div>
    </Show>
  );
}
