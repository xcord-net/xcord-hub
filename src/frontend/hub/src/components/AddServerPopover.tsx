import { createSignal, createEffect, onCleanup, Show, For } from 'solid-js';
import { instanceStore, type DiscoverableInstance } from '../stores/instance.store';

interface AddServerPopoverProps {
  open: boolean;
  onClose: () => void;
}

export default function AddServerPopover(props: AddServerPopoverProps) {
  const [query, setQuery] = createSignal('');
  const [results, setResults] = createSignal<DiscoverableInstance[]>([]);
  const [searching, setSearching] = createSignal(false);
  const [error, setError] = createSignal('');

  let popoverRef: HTMLDivElement | undefined;
  let inputRef: HTMLInputElement | undefined;
  let debounceTimer: ReturnType<typeof setTimeout>;

  // Focus input when popover opens
  createEffect(() => {
    if (props.open) {
      setTimeout(() => inputRef?.focus(), 0);
    } else {
      setQuery('');
      setResults([]);
      setError('');
    }
  });

  // Close on outside click
  createEffect(() => {
    if (!props.open) return;

    const handleClick = (e: MouseEvent) => {
      if (popoverRef && !popoverRef.contains(e.target as Node)) {
        props.onClose();
      }
    };

    // Delay to avoid the opening click triggering close
    setTimeout(() => document.addEventListener('mousedown', handleClick), 0);
    onCleanup(() => document.removeEventListener('mousedown', handleClick));
  });

  // Close on Escape
  createEffect(() => {
    if (!props.open) return;

    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') props.onClose();
    };

    document.addEventListener('keydown', handleKey);
    onCleanup(() => document.removeEventListener('keydown', handleKey));
  });

  const handleInput = (value: string) => {
    setQuery(value);
    setError('');
    clearTimeout(debounceTimer);

    if (value.trim().length < 2) {
      setResults([]);
      setSearching(false);
      return;
    }

    setSearching(true);
    debounceTimer = setTimeout(async () => {
      const found = await instanceStore.searchInstances(value.trim());
      setResults(found);
      setSearching(false);
    }, 300);
  };

  const normalizeUrl = (input: string): string => {
    let url = input.trim();
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
      url = `https://${url}`;
    }
    return url.replace(/\/+$/, '');
  };

  const isAlreadyAdded = (url: string): boolean => {
    const normalized = normalizeUrl(url);
    return instanceStore.connectedInstances().some(i => i.url === normalized);
  };

  const addFromResult = (result: DiscoverableInstance) => {
    if (isAlreadyAdded(result.url)) {
      setError('Already added');
      return;
    }
    instanceStore.addInstance({
      url: result.url,
      name: result.name,
      iconUrl: result.iconUrl,
    });
    props.onClose();
  };

  const addCustomUrl = () => {
    const raw = query().trim();
    if (!raw) return;

    const url = normalizeUrl(raw);

    if (isAlreadyAdded(url)) {
      setError('Already added');
      return;
    }

    try {
      const hostname = new URL(url).hostname;
      instanceStore.addInstance({ url, name: hostname });
      props.onClose();
    } catch {
      setError('Invalid URL');
    }
  };

  const handleKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      // If there's exactly one result, add it
      const r = results();
      if (r.length === 1) {
        addFromResult(r[0]);
      } else {
        addCustomUrl();
      }
    }
  };

  return (
    <Show when={props.open}>
      <div
        ref={popoverRef}
        class="absolute top-full mt-1 left-0 w-80 bg-xcord-bg-floating border border-xcord-bg-tertiary rounded-lg shadow-xl z-50 overflow-hidden"
      >
        {/* Input */}
        <div class="p-2">
          <input
            ref={inputRef}
            type="text"
            value={query()}
            onInput={(e) => handleInput(e.currentTarget.value)}
            onKeyDown={handleKeyDown}
            placeholder="Enter server address or search..."
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary text-sm rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand placeholder:text-xcord-text-muted/50"
          />
        </div>

        {/* Error */}
        <Show when={error()}>
          <div class="px-4 py-2 text-xs text-xcord-red">{error()}</div>
        </Show>

        {/* Results */}
        <Show when={query().trim().length >= 2}>
          <div class="max-h-60 overflow-y-auto border-t border-xcord-bg-tertiary">
            <Show when={searching()}>
              <div class="px-4 py-3 text-sm text-xcord-text-muted">Searching...</div>
            </Show>

            <Show when={!searching() && results().length === 0}>
              <div class="px-4 py-3 text-sm text-xcord-text-muted">
                No servers found. Press Enter to add by URL.
              </div>
            </Show>

            <For each={results()}>
              {(result) => {
                const added = () => isAlreadyAdded(result.url);
                return (
                  <button
                    onClick={() => addFromResult(result)}
                    disabled={added()}
                    class="w-full flex items-center gap-3 px-4 py-2.5 text-left hover:bg-xcord-bg-accent/50 transition disabled:opacity-50 disabled:cursor-default"
                  >
                    {/* Icon */}
                    <Show
                      when={result.iconUrl}
                      fallback={
                        <div class="w-8 h-8 rounded-full bg-xcord-bg-accent flex items-center justify-center text-xcord-text-primary text-sm font-medium shrink-0">
                          {result.name[0]?.toUpperCase() ?? '?'}
                        </div>
                      }
                    >
                      <img
                        src={result.iconUrl}
                        alt=""
                        class="w-8 h-8 rounded-full object-cover shrink-0"
                      />
                    </Show>

                    {/* Info */}
                    <div class="min-w-0 flex-1">
                      <div class="text-sm font-medium text-xcord-text-primary truncate">
                        {result.name}
                      </div>
                      <div class="text-xs text-xcord-text-muted truncate">
                        {new URL(result.url).hostname}
                        <Show when={result.memberCount > 0}>
                          {' '}&middot; {result.memberCount} members
                        </Show>
                      </div>
                    </div>

                    {/* Already added indicator */}
                    <Show when={added()}>
                      <span class="text-xs text-xcord-text-muted shrink-0">Added</span>
                    </Show>
                  </button>
                );
              }}
            </For>
          </div>
        </Show>
      </div>
    </Show>
  );
}
