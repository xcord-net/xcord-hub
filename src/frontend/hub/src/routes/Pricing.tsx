import { A } from '@solidjs/router';
import { createSignal, For, Show } from 'solid-js';

interface Tier {
  name: string;
  price: string;
  period: string;
  description: string;
  features: string[];
  cta: 'link' | 'notify';
  ctaLabel: string;
  ctaHref?: string;
  highlighted: boolean;
}

interface SplitCard {
  top: {
    name: string;
    tagline: string;
    description: string;
    cta: string;
    ctaHref: string;
  };
  bottom: {
    name: string;
    tagline: string;
    description: string;
    cta: string;
    ctaHref: string;
  };
}

const tiers: Tier[] = [
  {
    name: 'Free',
    price: '$0',
    period: '/month',
    description: 'Perfect for getting started',
    features: ['1 instance', '50 members', '1 GB storage', 'Voice channels', 'Community support'],
    cta: 'link',
    ctaLabel: 'Get Started',
    ctaHref: '/register',
    highlighted: false,
  },
  {
    name: 'Basic',
    price: 'TBD',
    period: '/month',
    description: 'For growing communities',
    features: ['1 instance', '250 members', '10 GB storage', 'Video & screen sharing', 'Email support'],
    cta: 'notify',
    ctaLabel: 'Notify Me',
    highlighted: false,
  },
  {
    name: 'Pro',
    price: 'TBD',
    period: '/month',
    description: 'For serious communities',
    features: ['1 instance', '1,000 members', '50 GB storage', 'Video, screen sharing & Go Live', 'Priority support'],
    cta: 'notify',
    ctaLabel: 'Notify Me',
    highlighted: true,
  },
];

const splitCard: SplitCard = {
  top: {
    name: 'Custom',
    tagline: 'Need more?',
    description: 'Custom limits, dedicated infrastructure, SLA',
    cta: 'Request Quote',
    ctaHref: 'mailto:sales@xcord.net',
  },
  bottom: {
    name: 'Self-Hosted',
    tagline: 'Full control',
    description: 'Unlimited everything, your servers',
    cta: 'View Docs',
    ctaHref: '/docs/getting-started',
  },
};

const faqs = [
  { q: 'Can I switch plans later?', a: 'Yes, you can upgrade or downgrade at any time. Changes take effect immediately and billing is prorated.' },
  { q: 'What happens to my data if I cancel?', a: 'Your data remains available for 30 days after cancellation. You can export everything at any time.' },
  { q: 'Is the self-hosted version really free?', a: 'Yes. Xcord is open source. You only pay for your own infrastructure costs.' },
  { q: 'Can I connect a self-hosted instance to the hub?', a: 'Absolutely. Self-hosted instances can connect to the hub for discovery and SSO, or run completely standalone.' },
];

function NotifyMeButton(props: { tier: string; highlighted: boolean }) {
  const [expanded, setExpanded] = createSignal(false);
  const [email, setEmail] = createSignal('');
  const [submitting, setSubmitting] = createSignal(false);
  const [submitted, setSubmitted] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    const value = email().trim();
    if (!value) return;
    setError(null);
    setSubmitting(true);
    try {
      const res = await fetch('/api/v1/mailing-list', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: value, tier: props.tier }),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        setError(data?.detail || data?.message || 'Something went wrong.');
        return;
      }
      setSubmitted(true);
    } catch {
      setError('Network error. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Show when={!submitted()} fallback={
      <p class="text-sm text-xcord-green text-center py-2">You're on the list!</p>
    }>
      <Show when={expanded()} fallback={
        <button
          onClick={() => setExpanded(true)}
          class={`block w-full text-center py-2 rounded-lg font-medium transition ${
            props.highlighted
              ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
              : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
          }`}
        >
          Notify Me
        </button>
      }>
        <form onSubmit={handleSubmit} class="flex flex-col gap-2">
          <input
            type="email"
            required
            placeholder="you@example.com"
            value={email()}
            onInput={(e) => setEmail(e.currentTarget.value)}
            class="w-full px-3 py-2 rounded-lg bg-xcord-bg-primary border border-xcord-landing-border text-white text-sm placeholder:text-xcord-landing-text-muted focus:outline-none focus:border-xcord-brand"
          />
          <button
            type="submit"
            disabled={submitting()}
            class={`w-full py-2 rounded-lg font-medium text-sm transition disabled:opacity-50 ${
              props.highlighted
                ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
                : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
            }`}
          >
            {submitting() ? 'Submitting...' : 'Submit'}
          </button>
          <Show when={error()}>
            <p class="text-xs text-xcord-red">{error()}</p>
          </Show>
        </form>
      </Show>
    </Show>
  );
}

export default function Pricing() {
  const [openFaq, setOpenFaq] = createSignal<number | null>(null);

  return (
    <>
      <section class="max-w-7xl mx-auto px-6 py-20 text-center">
        <h1 class="text-4xl font-bold mb-16">Pricing</h1>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-20">
          <For each={tiers}>
            {(tier) => (
              <div class={`rounded-xl p-6 text-left flex flex-col ${
                tier.highlighted
                  ? 'bg-xcord-brand/10 border-2 border-xcord-brand'
                  : 'bg-xcord-landing-surface border border-xcord-landing-border'
              }`}>
                <h3 class="text-lg font-semibold text-white">{tier.name}</h3>
                <div class="mt-4 mb-2">
                  <span class="text-3xl font-bold text-white">{tier.price}</span>
                  <span class="text-sm text-xcord-landing-text-muted">{tier.period}</span>
                </div>
                <p class="text-sm text-xcord-landing-text-muted mb-6">{tier.description}</p>
                <ul class="space-y-2 mb-8 flex-1">
                  <For each={tier.features}>
                    {(feature) => (
                      <li class="flex items-center gap-2 text-sm text-xcord-landing-text">
                        <svg class="w-4 h-4 text-xcord-green shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                        </svg>
                        {feature}
                      </li>
                    )}
                  </For>
                </ul>
                <Show when={tier.cta === 'link'}>
                  <A
                    href={tier.ctaHref!}
                    class={`block text-center py-2 rounded-lg font-medium transition ${
                      tier.highlighted
                        ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
                        : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
                    }`}
                  >
                    {tier.ctaLabel}
                  </A>
                </Show>
                <Show when={tier.cta === 'notify'}>
                  <NotifyMeButton tier={tier.name} highlighted={tier.highlighted} />
                </Show>
              </div>
            )}
          </For>

          {/* Split card: Custom (top) + Self-Hosted (bottom) */}
          <div class="rounded-xl text-left flex flex-col border border-xcord-landing-border overflow-hidden">
            <div class="flex-1 p-6 bg-xcord-landing-surface flex flex-col">
              <h3 class="text-lg font-semibold text-white">{splitCard.top.name}</h3>
              <p class="text-sm text-xcord-brand font-medium mt-2">{splitCard.top.tagline}</p>
              <p class="text-sm text-xcord-landing-text-muted mt-2 mb-6 flex-1">{splitCard.top.description}</p>
              <a
                href={splitCard.top.ctaHref}
                class="block text-center py-2 rounded-lg font-medium bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white transition"
              >
                {splitCard.top.cta}
              </a>
            </div>
            <div class="border-t border-xcord-landing-border" />
            <div class="flex-1 p-6 bg-xcord-landing-surface flex flex-col">
              <h3 class="text-lg font-semibold text-white">{splitCard.bottom.name}</h3>
              <p class="text-sm text-xcord-brand font-medium mt-2">{splitCard.bottom.tagline}</p>
              <p class="text-sm text-xcord-landing-text-muted mt-2 mb-6 flex-1">{splitCard.bottom.description}</p>
              <A
                href={splitCard.bottom.ctaHref}
                class="block text-center py-2 rounded-lg font-medium bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white transition"
              >
                {splitCard.bottom.cta}
              </A>
            </div>
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section class="max-w-3xl mx-auto px-6 pb-20">
        <h2 class="text-2xl font-bold text-center mb-8">Frequently asked questions</h2>
        <div class="space-y-2">
          <For each={faqs}>
            {(faq, i) => (
              <div class="border border-xcord-landing-border rounded-lg overflow-hidden">
                <button
                  onClick={() => setOpenFaq(openFaq() === i() ? null : i())}
                  class="w-full flex items-center justify-between px-6 py-4 text-left text-white hover:bg-xcord-landing-surface transition"
                >
                  <span class="font-medium">{faq.q}</span>
                  <svg
                    class={`w-5 h-5 text-xcord-landing-text-muted transition-transform ${openFaq() === i() ? 'rotate-180' : ''}`}
                    fill="none" viewBox="0 0 24 24" stroke="currentColor"
                  >
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
                  </svg>
                </button>
                {openFaq() === i() && (
                  <div class="px-6 pb-4 text-sm text-xcord-landing-text-muted">{faq.a}</div>
                )}
              </div>
            )}
          </For>
        </div>
      </section>
    </>
  );
}
