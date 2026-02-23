import { createResource } from "solid-js";
import { A } from "@solidjs/router";
import FeatureCard from "../components/FeatureCard";

interface HubStats {
  totalServers: number;
  totalUsers: number;
  totalMessages: number;
}

async function getPublicStats(): Promise<HubStats> {
  const res = await fetch("/api/v1/hub/stats");
  if (!res.ok) throw new Error("Failed to fetch stats");
  return res.json();
}

export default function Landing() {
  const [stats] = createResource(getPublicStats);

  return (
    <div>
      {/* Hero Section */}
      <section class="py-20 sm:py-32">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <div class="mb-4">
            <span class="inline-block text-xs font-bold uppercase tracking-widest bg-xcord-brand/20 text-xcord-brand px-3 py-1 rounded-full">beta</span>
          </div>
          <h1 class="text-4xl sm:text-6xl font-bold text-white tracking-tight">
            Chat that's
            <br />
            <span class="text-xcord-brand">actually yours.</span>
          </h1>
          <p class="mt-6 text-lg sm:text-xl text-xcord-landing-text-muted max-w-2xl mx-auto">
            Open-source community platform with voice, video, and text.
            Host it yourself or start free on our cloud.
          </p>
          <div class="mt-10 flex flex-col sm:flex-row gap-4 justify-center">
            <A
              data-testid="hero-cta-register"
              href="/register"
              class="px-8 py-3 bg-xcord-brand text-white font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors text-lg"
            >
              Get Started Free
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
              From plug-and-play to full control — your community, your way.
            </p>
          </div>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
            <FeatureCard
              icon="🔀"
              title="Turnkey to Self-Hosted"
              description="Start managed — we handle hosting, updates, and infrastructure. Migrate to your own servers anytime. Your data comes with you."
            />
            <FeatureCard
              icon="👥"
              title="Membership Tiers"
              description="Create tiered access levels for your community. Gate channels, roles, and content, all built in — no third-party tools needed."
            />
            <FeatureCard
              icon="🌐"
              title="Federated + Open Source"
              description="Communities connect across instances — like email, but for chat. Apache 2.0 licensed. Inspect the code, contribute, or fork it."
            />
          </div>
        </div>
      </section>

      {/* How It Works */}
      <section class="py-20">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="text-center mb-16">
            <h2 class="text-3xl font-bold text-white">How It Works</h2>
            <p class="mt-4 text-xcord-landing-text-muted">Three steps to your own community.</p>
          </div>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                1
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Sign Up</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Create your free account in seconds. No credit card required.
              </p>
            </div>
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                2
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Create Your Server</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Launch your instance with a custom subdomain and your branding. Ready in under a minute.
              </p>
            </div>
            <div class="text-center">
              <div class="w-12 h-12 bg-xcord-brand/10 text-xcord-brand rounded-full flex items-center justify-center text-xl font-bold mx-auto mb-4">
                3
              </div>
              <h3 class="text-lg font-semibold text-white mb-2">Invite Your Community</h3>
              <p class="text-sm text-xcord-landing-text-muted">
                Share your invite link and start chatting. Voice, video, and text — all in one place.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Stats Section */}
      <section class="py-20 bg-xcord-landing-surface/50">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="grid grid-cols-1 md:grid-cols-3 gap-8 text-center">
            <div data-testid="stat-servers">
              <div class="text-4xl font-bold text-xcord-brand">
                {stats()?.totalServers?.toLocaleString() ?? "—"}
              </div>
              <div class="mt-2 text-sm text-xcord-landing-text-muted">Active Servers</div>
            </div>
            <div data-testid="stat-users">
              <div class="text-4xl font-bold text-xcord-brand">
                {stats()?.totalUsers?.toLocaleString() ?? "—"}
              </div>
              <div class="mt-2 text-sm text-xcord-landing-text-muted">Happy Users</div>
            </div>
            <div data-testid="stat-messages">
              <div class="text-4xl font-bold text-xcord-brand">
                {stats()?.totalMessages?.toLocaleString() ?? "—"}
              </div>
              <div class="mt-2 text-sm text-xcord-landing-text-muted">Messages Sent</div>
            </div>
          </div>
        </div>
      </section>

      {/* Final CTA */}
      <section class="py-20">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <h2 class="text-3xl sm:text-4xl font-bold text-white">Ready to start building?</h2>
          <p class="mt-4 text-lg text-xcord-landing-text-muted max-w-xl mx-auto">
            Join communities that chose their own platform.
          </p>
          <A
            data-testid="final-cta-register"
            href="/register"
            class="mt-8 inline-block px-8 py-3 bg-xcord-brand text-white font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors text-lg"
          >
            Get Started for Free
          </A>
        </div>
      </section>
    </div>
  );
}
