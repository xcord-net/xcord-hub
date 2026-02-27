import { createMemo, For, Show } from 'solid-js';
import { A } from '@solidjs/router';
import PageMeta from '../components/PageMeta';
import { detectPlatform, getDownloadLinks, type Platform } from '../utils/platform';

/* ── Platform SVG icons ──────────────────────────────────────────────── */

function WindowsIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8">
      <path d="M3 12.5v-8l7-1v8.5H3zm8-.5V2.5l10-1.5v11h-10zM3 13.5h7V22l-7-1v-7.5zm8 0h10V23l-10-1.5v-8z" />
    </svg>
  );
}

function AppleIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8">
      <path d="M18.71 19.5c-.83 1.24-1.71 2.45-3.05 2.47-1.34.03-1.77-.79-3.29-.79-1.53 0-2 .77-3.27.82-1.31.05-2.3-1.32-3.14-2.53C4.25 17 2.94 12.45 4.7 9.39c.87-1.52 2.43-2.48 4.12-2.51 1.28-.02 2.5.87 3.29.87.78 0 2.26-1.07 3.8-.91.65.03 2.47.26 3.64 1.98-.09.06-2.17 1.28-2.15 3.81.03 3.02 2.65 4.03 2.68 4.04-.03.07-.42 1.44-1.38 2.83M13 3.5c.73-.83 1.94-1.46 2.94-1.5.13 1.17-.34 2.35-1.04 3.19-.69.85-1.83 1.51-2.95 1.42-.15-1.15.41-2.35 1.05-3.11z" />
    </svg>
  );
}

function LinuxIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8">
      <path d="M12.504 0c-.155 0-.315.008-.48.021-4.226.333-3.105 4.807-3.17 6.298-.076 1.092-.3 1.953-1.05 3.02-.885 1.051-2.127 2.75-2.716 4.521-.278.832-.41 1.684-.287 2.489a.424.424 0 00-.11.135c-.26.268-.45.6-.663.839-.199.199-.485.267-.797.4-.313.136-.658.269-.864.68-.09.189-.136.394-.132.602 0 .199.027.4.055.536.058.399.116.728.04.97-.249.68-.28 1.145-.106 1.484.174.334.535.47.94.601.81.2 1.91.135 2.774.6.926.466 1.866.67 2.616.47.526-.116.97-.464 1.208-.946.587-.003 1.23-.269 2.26-.334.699-.058 1.574.267 2.577.2.025.134.063.198.114.333l.003.003c.391.778 1.113 1.368 1.884 1.43.868.134 1.703-.272 2.191-.574.3-.18.599-.306.9-.36.3-.06.6-.04.801.07.748.5 1.616.67 2.275.56.66-.11 1.155-.46 1.35-.84.39-.69-.12-1.414-.12-2.09 0-.68.18-1.37.27-2.08.09-.68-.06-1.56-.36-2.4-.66-1.84-1.93-3.52-2.84-4.59-.67-.78-.87-1.62-.97-2.72C16.927 4.857 18.065.394 13.68.02c-.25-.02-.5-.02-.68-.02zm-.005 1.09h.01c.12 0 .269.009.43.022 2.413.192 2.214 2.26 2.327 4.19.044.6.173 1.175.417 1.723.244.546.6 1.066 1.12 1.658.94 1.037 2.148 2.66 2.743 4.32.282.79.374 1.49.3 2.04-.074.55-.27 1.33-.27 2.05 0 .228.03.394.074.548-.442.142-.889.395-1.28.625-.449.27-1.108.52-1.666.412-.558-.103-.98-.546-1.266-1.12l-.007-.015c-.095-.168-.232-.354-.394-.465-.083-.06-.186-.087-.29-.1-.104-.012-.226.001-.35.034-.088.026-.192.062-.287.088.015-.09.028-.152.028-.237-.01-.367-.12-.753-.278-1.114-.374-.87-1.044-1.46-1.044-1.46-.024-.022-.051-.04-.074-.061-.098-.34-.19-.597-.257-.821-.077-.24-.093-.381-.093-.598 0-.203.035-.394.095-.578.063-.19.142-.375.262-.56.243-.38.583-.693.895-1.157.617-.93.955-2.03 1.043-3.33.084-1.234-.198-3.038-1.592-3.148zM8.092 6.15c.075 0 .189.003.28.025.478.113 1.042.713.938 2.408-.069 1.13-.343 1.91-.818 2.626-.278.42-.605.72-.876 1.131a3.673 3.673 0 00-.384.817c-.086.263-.12.522-.16.758-.17-.15-.33-.317-.51-.398l-.003-.002c-.485-.233-.947-.514-1.475-.69-.24-.091-.498-.138-.753-.124.09-.313.22-.635.415-.97.53-.858 1.704-2.455 2.536-3.452.594-.714.9-1.398 1.003-2.482.016-.17.03-.321.04-.45.2.117.46.257.68.315.025.009.05.013.076.016l.01.002zM6.022 14.91c.175.004.344.08.51.164.375.203.657.439.884.627.111.09.21.176.28.185l.012.003a.374.374 0 00.039.006c.038.006.062.014.08.025.019.01.06.038.09.1.07.129.1.332.135.57.035.244.046.539.197.87.023.049.084.176.136.275-.064.026-.106.053-.18.067-.336.057-.717-.05-1.25-.3-.533-.25-1.09-.284-1.544-.315-.455-.03-.812-.043-1.023-.18-.093-.062-.174-.207-.065-.507.109-.3.113-.587.06-.936-.053-.352-.087-.603-.032-.716.027-.052.099-.076.209-.102.113-.028.257-.034.41-.037l.052.001zm9.547.29c.088.007.19.022.293.042.383.064.76.21 1.08.395.256.154.477.34.647.524-.056.01-.115.024-.162.04-.217.068-.52.25-.817.428-.298.177-.574.349-.75.381-.177.03-.392-.058-.665-.222-.272-.164-.588-.376-.95-.432a3.562 3.562 0 00-.186-.024c.092-.196.196-.363.3-.452.139-.122.323-.208.543-.289.207-.082.453-.1.667-.11zm-9.853 2.125c.032-.003.063.002.093.008.091.015.137.069.2.188.064.12.096.287.04.464-.073.233-.204.3-.395.315-.204.012-.455-.053-.628-.18-.172-.13-.22-.273-.156-.452.064-.178.191-.306.47-.326.048-.003.108-.01.167-.014l.21-.003zm10.488.295c.036 0 .072.002.108.007.237.031.385.2.463.387.078.19.078.39.004.534-.073.15-.225.25-.451.237-.228-.01-.377-.109-.47-.287-.094-.179-.088-.39 0-.557.065-.127.175-.238.346-.32z" />
    </svg>
  );
}

function AndroidIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8">
      <path d="M17.523 15.341a1.015 1.015 0 001.026-1.01 1.015 1.015 0 00-1.026-1.011 1.015 1.015 0 00-1.025 1.01c0 .559.46 1.011 1.025 1.011zm-11.046 0a1.015 1.015 0 001.025-1.01 1.015 1.015 0 00-1.025-1.011 1.015 1.015 0 00-1.026 1.01c0 .559.46 1.011 1.026 1.011zm11.405-4.563l1.98-3.431a.413.413 0 00-.151-.563.413.413 0 00-.563.151l-2.004 3.473a12.174 12.174 0 00-5.288-1.186c-1.895 0-3.658.428-5.288 1.186l-2.004-3.473a.413.413 0 00-.563-.151.413.413 0 00-.151.563l1.98 3.431C2.658 12.606.5 15.681.5 19.222h23c0-3.54-2.158-6.616-5.618-8.444z" />
    </svg>
  );
}

function IosIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8">
      <path d="M18.71 19.5c-.83 1.24-1.71 2.45-3.05 2.47-1.34.03-1.77-.79-3.29-.79-1.53 0-2 .77-3.27.82-1.31.05-2.3-1.32-3.14-2.53C4.25 17 2.94 12.45 4.7 9.39c.87-1.52 2.43-2.48 4.12-2.51 1.28-.02 2.5.87 3.29.87.78 0 2.26-1.07 3.8-.91.65.03 2.47.26 3.64 1.98-.09.06-2.17 1.28-2.15 3.81.03 3.02 2.65 4.03 2.68 4.04-.03.07-.42 1.44-1.38 2.83M13 3.5c.73-.83 1.94-1.46 2.94-1.5.13 1.17-.34 2.35-1.04 3.19-.69.85-1.83 1.51-2.95 1.42-.15-1.15.41-2.35 1.05-3.11z" />
    </svg>
  );
}

function DownloadArrow() {
  return (
    <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
      <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 10l5 5m0 0l5-5m-5 5V3" />
    </svg>
  );
}

const platformIcons: Record<string, () => ReturnType<typeof WindowsIcon>> = {
  windows: WindowsIcon,
  macos: AppleIcon,
  linux: LinuxIcon,
  android: AndroidIcon,
  ios: IosIcon,
};

/* ── Page ─────────────────────────────────────────────────────────────── */

export default function Download() {
  const detected = detectPlatform();
  const links = createMemo(() => getDownloadLinks());

  const heroInfo = createMemo(() => {
    if (detected === 'unknown') return null;
    return links()[detected] ?? null;
  });

  const heroPrimary = createMemo(() => {
    const info = heroInfo();
    if (!info || info.comingSoon) return null;
    return info.links[0];
  });

  const platformOrder: Platform[] = ['windows', 'macos', 'linux', 'android', 'ios'];

  return (
    <>
      <PageMeta
        title="Download Xcord - Desktop & Mobile Apps"
        description="Download Xcord for Windows, macOS, Linux, Android, and iOS. Native apps with voice, video, and text."
        path="/download"
      />

      {/* Hero */}
      <section class="py-20 sm:py-28">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <h1 class="text-4xl sm:text-5xl font-bold text-white tracking-tight">
            Get Xcord
          </h1>
          <p class="mt-4 text-lg text-xcord-landing-text-muted max-w-xl mx-auto">
            Available on desktop and mobile. Connect to your communities from anywhere.
          </p>

          {/* Detected OS hero button */}
          <Show when={heroPrimary()}>
            {(primary) => {
              const info = heroInfo()!;
              const Icon = platformIcons[detected];
              return (
                <div class="mt-10 flex flex-col items-center gap-3">
                  <a
                    href={primary().url}
                    class="inline-flex items-center gap-3 px-8 py-4 bg-xcord-brand text-white font-semibold rounded-xl hover:bg-xcord-brand-hover transition-colors text-lg shadow-lg shadow-xcord-brand/20"
                  >
                    <DownloadArrow />
                    Download for {info.name}
                  </a>
                  <span class="text-sm text-xcord-landing-text-muted">
                    {primary().format}
                    <Show when={info.arch}>
                      {' '}&middot; {info.arch}
                    </Show>
                  </span>
                  <Show when={info.links.length > 1}>
                    <div class="flex gap-3 mt-1">
                      <For each={info.links.slice(1)}>
                        {(link) => (
                          <a
                            href={link.url}
                            class="text-sm text-xcord-brand hover:underline"
                          >
                            {link.label} ({link.format})
                          </a>
                        )}
                      </For>
                    </div>
                  </Show>
                </div>
              );
            }}
          </Show>

          {/* Fallback when platform is unknown or mobile */}
          <Show when={!heroPrimary()}>
            <div class="mt-10">
              <Show when={heroInfo()?.comingSoon}>
                <p class="text-xcord-landing-text-muted mb-4">
                  {heroInfo()!.name} app coming soon. Download the desktop app below.
                </p>
              </Show>
              <Show when={detected === 'unknown'}>
                <p class="text-xcord-landing-text-muted mb-4">
                  Choose your platform below.
                </p>
              </Show>
            </div>
          </Show>
        </div>
      </section>

      {/* All platforms grid */}
      <section class="pb-24">
        <div class="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8">
          <h2 class="text-xl font-semibold text-white text-center mb-10">
            All platforms
          </h2>
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
            <For each={platformOrder}>
              {(key) => {
                const info = links()[key];
                const Icon = platformIcons[key];
                const isDetected = key === detected;
                return (
                  <div
                    class={`rounded-xl border p-6 flex flex-col gap-4 transition-colors ${
                      isDetected
                        ? 'bg-xcord-brand/5 border-xcord-brand/40'
                        : 'bg-xcord-landing-surface border-xcord-landing-border'
                    }`}
                  >
                    <div class="flex items-center gap-3">
                      <div class={`${isDetected ? 'text-xcord-brand' : 'text-xcord-landing-text-muted'}`}>
                        <Icon />
                      </div>
                      <div>
                        <h3 class="font-semibold text-white">{info.name}</h3>
                        <Show when={info.arch}>
                          <p class="text-xs text-xcord-landing-text-muted">{info.arch}</p>
                        </Show>
                      </div>
                      <Show when={isDetected}>
                        <span class="ml-auto text-[10px] font-bold uppercase tracking-widest bg-xcord-brand/20 text-xcord-brand px-2 py-0.5 rounded-full">
                          Your OS
                        </span>
                      </Show>
                    </div>

                    <Show
                      when={!info.comingSoon}
                      fallback={
                        <span class="text-sm text-xcord-landing-text-muted italic">
                          Coming soon
                        </span>
                      }
                    >
                      <div class="flex flex-col gap-2">
                        <For each={info.links}>
                          {(link) => (
                            <a
                              href={link.url}
                              class="inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg text-sm font-medium transition border border-xcord-landing-border text-white hover:bg-xcord-landing-border"
                            >
                              <DownloadArrow />
                              {link.label}
                              <span class="ml-auto text-xs text-xcord-landing-text-muted">{link.format}</span>
                            </a>
                          )}
                        </For>
                      </div>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>

          {/* Open source callout */}
          <div class="mt-14 text-center">
            <p class="text-sm text-xcord-landing-text-muted">
              Xcord is open source.{' '}
              <a
                href="https://github.com/xcord-net"
                target="_blank"
                rel="noopener noreferrer"
                class="text-xcord-brand hover:underline"
              >
                View on GitHub
              </a>
            </p>
          </div>
        </div>
      </section>
    </>
  );
}
