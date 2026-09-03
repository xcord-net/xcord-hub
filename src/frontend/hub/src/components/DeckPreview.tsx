import { For } from "solid-js";

/**
 * A running Deck, rendered as real DOM rather than a screenshot.
 *
 * The spec calls for "a live-looking Deck screenshot as hero". A screenshot
 * would go stale the first time the shell moves and cannot reflow on a phone,
 * so this builds the same frame out of markup: one tab strip, one content
 * pane, no rails. Structure and tokens track
 * docs/design/mockups/deck-redesign/deck-v2.html.
 *
 * The two dot colors below are community accents - owner-chosen, per-community
 * values, not brand tokens - so they are deliberately literal here. Everything
 * else comes from the shared token sheet.
 */

const PINNED = [
  { name: "announcements", accent: "#d4703f", unread: 0 },
  { name: "builds", accent: "#8b96f8", unread: 12 },
  { name: "mara", accent: "#3f3e46", unread: 0 },
];

export default function DeckPreview() {
  return (
    <div
      aria-hidden="true"
      class="rounded-lg overflow-hidden shadow-3 bg-xcord-landing-bg border border-xcord-landing-border select-none"
    >
      {/* Tab strip: Home, pinned tabs, one ghost, then the switchboard hint */}
      <div class="flex items-center gap-0.5 px-3 pt-2 border-b border-xcord-landing-border overflow-hidden">
        {/* Home tab - the waveform mark, always leftmost, always present */}
        <div class="flex items-center gap-2 px-2.5 py-2 rounded-t-[9px] bg-xcord-landing-surface border border-xcord-landing-border border-b-transparent relative top-px">
          <span class="xcord-wave flex items-end gap-[1.5px] h-3">
            <i class="block w-[2px] rounded-[2px] bg-xcord-brand h-1" />
            <i class="block w-[2px] rounded-[2px] bg-xcord-brand h-2" />
            <i class="block w-[2px] rounded-[2px] bg-xcord-brand h-3" />
            <i class="block w-[2px] rounded-[2px] bg-xcord-brand h-[7px]" />
            <i class="block w-[2px] rounded-[2px] bg-xcord-brand h-1" />
          </span>
        </div>

        <For each={PINNED}>
          {(tab, i) => (
            <div
              class={`${i() === 0 ? "flex" : "hidden sm:flex"} items-center gap-2 px-3 py-2 text-[0.84rem] text-xcord-text-muted relative top-px`}
            >
              <span
                class="w-[7px] h-[7px] rounded-[3px] shrink-0"
                style={{ "background-color": tab.accent }}
              />
              {tab.name}
              {tab.unread > 0 && (
                <span class="min-w-[15px] h-[15px] px-1 rounded-pill bg-xcord-brand text-xcord-landing-bg text-[0.6rem] font-extrabold flex items-center justify-center">
                  {tab.unread}
                </span>
              )}
            </div>
          )}
        </For>

        {/* Ghost tab: unpinned, surfaced by activity, dashed and italic. It
            fades in a beat after load so the concept reads without a caption. */}
        <div class="xcord-ghost-tab hidden md:flex items-center gap-2 px-3 py-2 text-[0.84rem] italic text-xcord-text-muted border border-dashed border-white/20 border-b-0 rounded-t-[9px] relative top-px">
          <span class="w-[7px] h-[7px] rounded-[3px] shrink-0 bg-[#8b96f8]" />
          patch-notes
          <span class="min-w-[15px] h-[15px] px-1 rounded-pill bg-xcord-bg-floating text-xcord-text-secondary text-[0.6rem] font-extrabold flex items-center justify-center">
            4
          </span>
        </div>

        <div class="ml-auto flex items-center gap-3 pb-1.5">
          <kbd class="hidden sm:block font-mono text-[0.6rem] text-xcord-text-faint border border-xcord-line rounded-sm px-1.5 py-0.5">
            ⌘K
          </kbd>
          <div class="w-6 h-6 rounded-md bg-gradient-to-br from-xcord-brand to-[#7d5518]" />
        </div>
      </div>

      {/* Content pane: the Home catch-up surface */}
      <div class="bg-xcord-landing-surface px-5 sm:px-8 py-6">
        <div class="font-display font-extrabold text-xl tracking-[-0.01em] text-xcord-landing-text">
          While you were away
        </div>
        <div class="text-[0.82rem] text-xcord-text-muted mt-0.5">
          3 conversations · 2 things happening in your communities
        </div>

        <h5 class="font-mono text-[0.66rem] tracking-[0.16em] text-xcord-text-faint mt-6 mb-2">
          MENTIONS &amp; UNREAD
        </h5>

        <div class="rounded-md bg-xcord-bg-accent p-3.5 flex gap-3 items-start">
          <div class="w-8 h-8 rounded-md shrink-0 flex items-center justify-center font-display font-extrabold text-[0.8rem] bg-[#8b96f8] text-white/85">
            N
          </div>
          <div class="flex-1 min-w-0">
            <div class="flex items-baseline gap-2 flex-wrap">
              <span class="font-bold text-[0.86rem]">builds</span>
              <span class="text-xcord-text-faint text-[0.7rem]">Night Shift</span>
              <span class="text-xcord-brand text-[0.7rem] font-bold ml-auto">12 new</span>
            </div>
            <div class="text-xcord-text-secondary text-[0.8rem] mt-1 leading-relaxed truncate">
              <b class="text-xcord-landing-text font-semibold">kex:</b> Season 9 tier list is up,{" "}
              <b class="text-xcord-brand font-semibold">@you</b> argue with me in voice.
            </div>
          </div>
        </div>

        <h5 class="font-mono text-[0.66rem] tracking-[0.16em] text-xcord-text-faint mt-5 mb-2">
          IN YOUR COMMUNITIES
        </h5>

        <div class="rounded-md bg-xcord-bg-accent p-3.5 flex gap-3 items-center">
          <div class="w-8 h-8 rounded-md shrink-0 flex items-center justify-center font-display font-extrabold text-[0.8rem] bg-[#d4703f] text-black/60">
            F
          </div>
          <div class="flex-1 min-w-0">
            <div class="flex items-baseline gap-2 flex-wrap">
              <span class="font-bold text-[0.86rem]">forge-floor</span>
              <span class="text-xcord-text-faint text-[0.7rem]">The Foundry</span>
              <span class="inline-flex items-center gap-1.5 text-[0.68rem] font-semibold text-xcord-brand bg-xcord-brand/10 rounded-pill px-2 py-0.5">
                live now
              </span>
            </div>
            <div class="text-xcord-text-secondary text-[0.8rem] mt-0.5 truncate">
              mara, ollie and 3 others are in voice
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
