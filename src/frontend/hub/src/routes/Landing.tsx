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
          description: 'Open-source, self-hosted Discord alternative with voice, video, and text. Federated community platform where you own the server and the encryption keys.',
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
        description="Xcord is an open-source Discord alternative you can self-host or run on our cloud. Federated community platform with voice, video, and text. You own the server and the encryption keys."
        path="/"
      />
      {/* Hero Section */}
      <section class="py-20 sm:py-32">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <div class="mb-4">
            <span class="inline-block text-xs font-bold uppercase tracking-widest bg-xcord-brand/20 text-xcord-brand px-3 py-1 rounded-full">beta</span>
          </div>
          <h1 class="text-4xl sm:text-6xl font-bold text-white tracking-tight">
            Your server. Your keys.
            <br />
            <span class="text-xcord-brand">Your community.</span>
          </h1>
          <p class="mt-6 text-lg sm:text-xl text-xcord-landing-text-muted max-w-2xl mx-auto">
            Open-source community platform with voice, video, and text.
            Every server generates its own encryption keys. We never see them - even on our cloud.
          </p>
          <div class="mt-10 flex flex-col sm:flex-row gap-4 justify-center">
            <A
              data-testid="hero-cta-register"
              href="/register"
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
              You own it whether we host it or you do.
            </p>
          </div>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
            <FeatureCard
              icon="🔀"
              title="Turnkey to Self-Hosted"
              description="Start managed - we handle hosting, updates, and infrastructure. Migrate to your own servers anytime. Your data comes with you."
            />
            <FeatureCard
              icon="👥"
              title="Membership Tiers"
              description="Create tiered access levels for your community. Gate channels, roles, and content, all built in - no third-party tools needed."
            />
            <FeatureCard
              icon="🌐"
              title="Open Source, Full Stop"
              description="Apache 2.0. No open-core tricks, no enterprise edition behind a paywall. The code running on our cloud is the same code you'd run on yours."
            />
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
              <h3 class="text-lg font-semibold text-white mb-2">Claim a subdomain</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Pick your address on xcord.net. No credit card, no phone number.
              </p>
            </div>
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                2
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">You're the admin</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Your instance generates its own encryption keys on first boot. Set your rules, add your branding.
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
          <h2 class="text-3xl sm:text-4xl font-bold text-white">Own your community server.</h2>
          <p class="mt-4 text-lg text-xcord-landing-text-muted max-w-xl mx-auto">
            Join communities that chose their own platform.
          </p>
          <A
            data-testid="final-cta-register"
            href="/register"
            class="mt-8 inline-block px-8 py-3 bg-xcord-brand text-white font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors text-lg"
          >
            Launch a Server
          </A>
        </div>
      </section>
    </div>
  );
}
