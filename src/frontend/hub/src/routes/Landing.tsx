import { A } from "@solidjs/router";
import { onMount, onCleanup } from "solid-js";
import FeatureCard from "../components/FeatureCard";
import PageMeta from "../components/PageMeta";

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
      {/* Hero Section */}
      <section class="py-20 sm:py-32">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <div class="mb-4">
            <span class="inline-block text-xs font-bold uppercase tracking-widest bg-xcord-brand/20 text-xcord-brand px-3 py-1 rounded-full">beta</span>
          </div>
          <h1 class="text-4xl sm:text-6xl font-bold text-white tracking-tight">
            Your corner of
            <br />
            <span class="text-xcord-brand">the internet.</span>
          </h1>
          <p class="mt-6 text-lg sm:text-xl text-xcord-landing-text-muted max-w-2xl mx-auto">
            Voice, video, and text - fully open-source and yours to keep.
            Run it on our cloud or self-host it. Same software either way.
          </p>
          <div class="mt-10 flex flex-col sm:flex-row gap-4 justify-center">
            <A
              data-testid="hero-cta-get-started"
              href="/get-started"
              class="px-8 py-3 bg-xcord-brand text-white font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors text-lg"
            >
              Launch a Server
            </A>
            <A
              data-testid="hero-cta-pricing"
              href="/pricing"
              class="px-8 py-3 bg-xcord-landing-surface border border-xcord-landing-border text-white font-medium rounded-lg hover:bg-xcord-landing-border transition-colors text-lg"
            >
              View Pricing
            </A>
          </div>
        </div>
      </section>

      {/* Features Grid */}
      <section class="py-20 bg-xcord-landing-surface/50">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="text-center mb-16">
            <h2 class="text-3xl font-bold text-white">Why Xcord?</h2>
            <p class="mt-4 text-xcord-landing-text-muted max-w-xl mx-auto">
              Built for the people who run servers, not the people who sell ads.
            </p>
          </div>
          <div class="flex flex-wrap justify-center gap-6">
            <div class="w-full md:w-[calc(33.333%-1rem)]">
              <FeatureCard
                icon="🎙️"
                title="Everything built in"
                description="Voice, video, streaming, bots, automod, forums, polls, emoji, and events. No plugins to install, no add-ons to buy."
              />
            </div>
            <div class="w-full md:w-[calc(33.333%-1rem)]">
              <FeatureCard
                icon="💰"
                title="Keep every dollar"
                description="Built-in paid tiers with zero revenue share. Gate channels, roles, and content. No third-party tools, no middleman taking a cut."
              />
            </div>
            <div class="w-full md:w-[calc(33.333%-1rem)]">
              <FeatureCard
                icon="⚖️"
                title="Your rules"
                description="Configurable automod, custom moderation policies, audit logs. You decide what's acceptable on your server - not us."
              />
            </div>
            <div class="w-full md:w-[calc(33.333%-1rem)]">
              <FeatureCard
                icon="🛡️"
                title="No platform risk"
                description="Your server can't be shut down by a policy change you didn't agree to. Want to leave? Take your data and go."
              />
            </div>
            <div class="w-full md:w-[calc(33.333%-1rem)]">
              <FeatureCard
                icon="🔒"
                title="No ads, no tracking"
                description="Your members' data isn't the product. No analytics pixels, no advertising networks, no data sales. Ever."
              />
            </div>
          </div>
        </div>
      </section>

      {/* How It Works */}
      <section class="py-20">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="text-center mb-16">
            <h2 class="text-3xl font-bold text-white">Up and running in minutes</h2>
          </div>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                1
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Pick a name</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Choose your server's address. No credit card, no phone number.
              </p>
            </div>
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                2
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Make it yours</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Set your rules, add your branding, configure your channels.
              </p>
            </div>
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                3
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Bring your people</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Share a link. Text, voice, and video - all built in.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Stats Section - hidden until real data is available */}

      {/* Final CTA */}
      <section class="py-20">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <h2 class="text-3xl sm:text-4xl font-bold text-white">Ready to run your own server?</h2>
          <p class="mt-4 text-lg text-xcord-landing-text-muted max-w-xl mx-auto">
            Free for up to 10 members. No credit card required.
          </p>
          <A
            data-testid="final-cta-get-started"
            href="/get-started"
            class="mt-8 inline-block px-8 py-3 bg-xcord-brand text-white font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors text-lg"
          >
            Launch a Server
          </A>
        </div>
      </section>
    </div>
  );
}
