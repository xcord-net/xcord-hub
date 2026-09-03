import { A } from "@solidjs/router";
import { onMount, onCleanup, For } from "solid-js";
import PageMeta from "../components/PageMeta";
import DeckPreview from "../components/DeckPreview";

/**
 * Landing surface, rebuilt to the approved Deck design spec:
 * wordmark, one ownership thesis, a live-looking Deck as the hero, four short
 * pillar sections, no feature-card grid. Copy traces to
 * docs/marketing/assets/landing-copy.md, which traces to positioning.md.
 */

// Each pillar names the product it replaces. That label is the structural
// device on this page and it carries real information, so it is not decoration.
const PILLARS = [
  {
    instead: "INSTEAD OF DISCORD",
    title: "Community and voice",
    body: "Text and voice channels, threads, forums, roles, moderation, and bots. The parts of Discord that work, without the parts that do not.",
  },
  {
    instead: "INSTEAD OF LOCALS",
    title: "A membership home you own",
    body: "A community your members join on infrastructure you control, instead of a platform that can remove you from it.",
  },
  {
    instead: "INSTEAD OF PATREON",
    title: "Paid tiers, zero revenue share",
    body: "Design your own membership tiers. Members pay through your own Stripe account, so the money goes straight to you and Xcord takes none of it.",
  },
  {
    instead: "INSTEAD OF STREAMYARD",
    title: "Broadcasting built in",
    body: "Go live to your community with layout presets, up to 8 guests on stage, and a green room. Relay the same broadcast to YouTube, Twitch, Rumble, and custom RTMP at once.",
  },
];

export default function Landing() {
  onMount(() => {
    const script = document.createElement('script');
    script.type = 'application/ld+json';
    script.id = 'xcord-jsonld';
    script.textContent = JSON.stringify({
      '@context': 'https://schema.org',
      '@graph': [
        {
          '@type': 'Organization',
          name: 'Xcord',
          alternateName: 'Xcord - Open Source Discord Alternative',
          url: window.location.origin,
          logo: `${window.location.origin}/android-chrome-512x512.png`,
          description: 'Open-source, self-hosted Discord alternative with voice and video streaming. Federated community platform where you own the server and the encryption keys.',
        },
        {
          '@type': 'WebSite',
          name: 'Xcord',
          url: window.location.origin,
        },
      ],
    });
    document.head.appendChild(script);
  });

  onCleanup(() => {
    document.getElementById('xcord-jsonld')?.remove();
  });

  return (
    <div>
      <PageMeta
        title="Xcord - The Open Source Discord Alternative | Self-Hosted Chat Platform"
        description="Xcord is an open-source Discord alternative you can self-host or run on our cloud. Federated community platform with voice and video streaming. You own the server and the encryption keys."
        path="/"
      />

      {/* Hero ------------------------------------------------------------- */}
      <section class="xcord-grid-ground pt-16 pb-20 sm:pt-24 sm:pb-28">
        <div class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="max-w-3xl">
            <h1 data-testid="landing-hero-heading" class="font-display text-4xl sm:text-6xl font-extrabold tracking-[-0.02em] leading-[1.05] text-xcord-landing-text">
              Four tools in one
              <br />
              platform <span class="text-xcord-brand">you own.</span>
            </h1>
            <p class="mt-6 text-lg text-xcord-landing-text-muted max-w-2xl leading-relaxed">
              Build a community with chat and voice, run it as your own membership home,
              charge for tiers while keeping every dollar, and broadcast live to it.
              Federated, self-hostable, and free of data harvesting.
            </p>

            <div class="mt-9 flex flex-col sm:flex-row gap-3 sm:items-center">
              <A
                data-testid="hero-cta-get-started"
                href="/get-started"
                class="px-7 py-3 bg-xcord-brand text-xcord-landing-bg font-semibold rounded-md hover:bg-xcord-brand-hover transition-colors duration-200 text-center focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-xcord-brand"
              >
                Launch a server
              </A>
              <A
                data-testid="hero-cta-pricing"
                href="/pricing"
                class="px-7 py-3 border border-xcord-line text-xcord-landing-text font-medium rounded-md hover:bg-xcord-landing-surface transition-colors duration-200 text-center focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-xcord-brand"
              >
                See pricing
              </A>
            </div>

            <p class="mt-5 font-mono text-[0.7rem] tracking-[0.1em] text-xcord-text-muted">
              BETA · APACHE 2.0 · YOUR COMMUNITY, YOUR DATA, YOUR KEYS
            </p>
          </div>

          {/* The signature: a Deck that is actually running, not a picture */}
          <div class="mt-14 sm:mt-16">
            <DeckPreview />
          </div>
        </div>
      </section>

      {/* The problem ------------------------------------------------------ */}
      <section class="py-20 border-t border-xcord-landing-border">
        <div class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="max-w-3xl">
            <h2 data-testid="landing-problem-heading" class="font-display text-3xl sm:text-4xl font-bold tracking-[-0.01em] text-xcord-landing-text">
              You are renting your community from four different landlords.
            </h2>
            <p class="mt-5 text-lg text-xcord-landing-text-muted leading-relaxed">
              Most creators run Discord for community, Locals for a paid membership home,
              Patreon for tiers, and StreamYard for multistreaming. Four subscriptions.
              Two of them take a cut of what your members pay. All of them sit on
              infrastructure you do not own, and treat your members' data as the product.
            </p>
          </div>
        </div>
      </section>

      {/* Four pillars ----------------------------------------------------- */}
      <section class="py-20 bg-xcord-landing-surface border-t border-xcord-landing-border">
        <div class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8">
          <h2 class="font-display text-3xl sm:text-4xl font-bold tracking-[-0.01em] text-xcord-landing-text max-w-2xl">
            One interface. Four products' worth of tools. All yours.
          </h2>

          <div class="mt-12">
            <For each={PILLARS}>
              {(pillar) => (
                <div class="grid grid-cols-1 md:grid-cols-[14rem_1fr] gap-2 md:gap-10 py-8 border-t border-xcord-landing-border">
                  <div
                    data-testid="landing-pillar-replaces"
                    class="font-mono text-[0.68rem] tracking-[0.18em] text-xcord-brand pt-1"
                  >
                    {pillar.instead}
                  </div>
                  <div>
                    <h3 class="font-display text-xl font-bold text-xcord-landing-text">
                      {pillar.title}
                    </h3>
                    <p class="mt-2 text-xcord-landing-text-muted leading-relaxed max-w-2xl">
                      {pillar.body}
                    </p>
                  </div>
                </div>
              )}
            </For>
          </div>

          <p class="font-display text-xl sm:text-2xl font-bold tracking-[-0.01em] text-xcord-landing-text max-w-2xl pt-8 border-t border-xcord-landing-border">
            One place you own, instead of four subscriptions that each take a cut.
          </p>
        </div>
      </section>

      {/* Ownership -------------------------------------------------------- */}
      <section class="py-20 border-t border-xcord-landing-border">
        <div class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="max-w-3xl">
            <h2 class="font-display text-3xl sm:text-4xl font-bold tracking-[-0.01em] text-xcord-landing-text">
              You hold the keys. Not us.
            </h2>
            <p class="mt-5 text-lg text-xcord-landing-text-muted leading-relaxed">
              Run Xcord standalone on your own hardware, or let the hub provision and
              manage an instance for you. Either way, each instance generates and holds
              its own encryption keys, and the hub never has them. Apache 2.0, no lock-in.
            </p>
            <p data-testid="landing-membership-caveat" class="mt-6 border-l-2 border-xcord-line pl-4 text-sm text-xcord-text-muted leading-relaxed">
              Membership tiers are available on standalone self-hosted instances, and on
              hub-hosted instances at the Pro tier or higher.
            </p>
          </div>
        </div>
      </section>

      {/* Final CTA -------------------------------------------------------- */}
      <section class="py-24 border-t border-xcord-landing-border xcord-grid-ground">
        <div class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8">
          <h2 class="font-display text-3xl sm:text-4xl font-bold tracking-[-0.01em] text-xcord-landing-text max-w-2xl">
            Own your community. Keep your revenue. Hold your keys.
          </h2>
          <p class="mt-5 text-lg text-xcord-landing-text-muted max-w-2xl">
            Free for up to 10 members. No credit card required.
          </p>
          <div class="mt-9 flex flex-col sm:flex-row gap-3 sm:items-center">
            <A
              data-testid="final-cta-get-started"
              href="/get-started"
              class="px-7 py-3 bg-xcord-brand text-xcord-landing-bg font-semibold rounded-md hover:bg-xcord-brand-hover transition-colors duration-200 text-center focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-xcord-brand"
            >
              Launch a server
            </A>
            <A
              href="/download"
              class="px-7 py-3 border border-xcord-line text-xcord-landing-text font-medium rounded-md hover:bg-xcord-landing-surface transition-colors duration-200 text-center focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-xcord-brand"
            >
              Get the apps
            </A>
          </div>
          <p class="mt-5 font-mono text-[0.7rem] tracking-[0.1em] text-xcord-text-faint">
            WEB · WINDOWS · MACOS · LINUX · IOS · ANDROID
          </p>
        </div>
      </section>
    </div>
  );
}
