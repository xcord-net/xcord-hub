import { A } from '@solidjs/router';
import { createSignal, For, Show } from 'solid-js';

interface Tier {
  name: string;
  price: string | null;
  period: string;
  description: string;
  features: string[];
  cta: string;
  ctaAction: 'notify' | 'link';
  ctaHref?: string;
  highlighted: boolean;
}

const tiers: Tier[] = [
  {
    name: 'Free',
    price: '$0',
    period: '/month',
    description: 'Perfect for getting started',
    features: ['1 instance', '50 members', '1 GB storage', 'Community support'],
    cta: 'Get Started',
    ctaAction: 'link',
    ctaHref: '/register',
    highlighted: false,
  },
  {
    name: 'Basic',
    price: 'TBD',
    period: '',
    description: 'For growing communities',
    features: ['3 instances', '500 members each', '10 GB storage', 'Email support', 'Custom domain'],
    cta: 'Notify Me',
    ctaAction: 'notify',
    highlighted: false,
  },
  {
    name: 'Pro',
    price: 'TBD',
    period: '',
    description: 'For serious communities',
    features: ['Unlimited instances', 'Unlimited members', '100 GB storage', 'Priority support', 'Custom domain', 'Custom branding', 'API access'],
    cta: 'Notify Me',
    ctaAction: 'notify',
    highlighted: true,
  },
  {
    name: 'Self-Hosted',
    price: null,
    period: '',
    description: 'Full control, your infrastructure',
    features: ['Unlimited everything', 'Your own servers', 'Full source access', 'Community support', 'Docker & Kubernetes'],
    cta: 'View Docs',
    ctaAction: 'link',
    ctaHref: '/docs/getting-started',
    highlighted: false,
  },
];

const faqs = [
  { q: 'Can I switch plans later?', a: 'Yes, you can upgrade or downgrade at any time. Changes take effect immediately and billing is prorated.' },
  { q: 'What happens to my data if I cancel?', a: 'Your data remains available for 30 days after cancellation. You can export everything at any time.' },
  { q: 'Is the self-hosted version really free?', a: 'Yes. Xcord is open source. You only pay for your own infrastructure costs.' },
  { q: 'Can I connect a self-hosted instance to the hub?', a: 'Absolutely. Self-hosted instances can connect to the hub for discovery and SSO, or run completely standalone.' },
];

export default function Pricing() {
  const [openFaq, setOpenFaq] = createSignal<number | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifySubmitted, setNotifySubmitted] = createSignal<Set<string>>(new Set());
  const [notifyError, setNotifyError] = createSignal<string | null>(null);

  const handleNotify = async (tierName: string) => {
    const email = notifyEmail().trim();
    if (!email) {
      setNotifyError('Please enter your email address.');
      return;
    }
    try {
      const res = await fetch('/api/v1/mailing-list', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, tier: tierName }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        setNotifyError(body?.message ?? 'Something went wrong. Please try again.');
        return;
      }
      setNotifySubmitted((prev) => new Set(prev).add(tierName));
      setNotifyTier(null);
      setNotifyEmail('');
      setNotifyError(null);
    } catch {
      setNotifyError('Unable to reach the server. Please try again.');
    }
  };

  return (
    <>
      <section class="max-w-7xl mx-auto px-6 py-20 text-center">
        <h1 class="text-4xl font-bold mb-4">Simple, transparent pricing</h1>
        <p class="text-xcord-landing-text-muted max-w-xl mx-auto mb-16">
          Pricing is coming soon. Sign up to be notified when plans are available.
        </p>

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
                  <Show when={tier.price !== null}>
                    <span class="text-3xl font-bold text-white">{tier.price}</span>
                  </Show>
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
                <Show when={tier.ctaAction === 'notify'}>
                  <Show when={notifySubmitted().has(tier.name)}>
                    <p class="text-sm text-xcord-green text-center py-2">Subscribed! We'll let you know.</p>
                  </Show>
                  <Show when={!notifySubmitted().has(tier.name)}>
                    <Show when={notifyTier() === tier.name}>
                      <div class="space-y-2">
                        <input
                          type="email"
                          placeholder="you@example.com"
                          value={notifyEmail()}
                          onInput={(e) => { setNotifyEmail(e.currentTarget.value); setNotifyError(null); }}
                          onKeyDown={(e) => { if (e.key === 'Enter') handleNotify(tier.name); }}
                          class="w-full px-3 py-2 rounded-lg bg-xcord-bg-primary border border-xcord-landing-border text-sm text-white placeholder-xcord-landing-text-muted focus:outline-none focus:border-xcord-brand"
                        />
                        <Show when={notifyError()}>
                          <p class="text-xs text-red-400">{notifyError()}</p>
                        </Show>
                        <div class="flex gap-2">
                          <button
                            onClick={() => { setNotifyTier(null); setNotifyEmail(''); setNotifyError(null); }}
                            class="flex-1 py-2 rounded-lg text-sm font-medium bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white transition"
                          >
                            Cancel
                          </button>
                          <button
                            onClick={() => handleNotify(tier.name)}
                            class={`flex-1 py-2 rounded-lg text-sm font-medium transition ${
                              tier.highlighted
                                ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
                                : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
                            }`}
                          >
                            Submit
                          </button>
                        </div>
                      </div>
                    </Show>
                    <Show when={notifyTier() !== tier.name}>
                      <button
                        onClick={() => { setNotifyTier(tier.name); setNotifyError(null); }}
                        class={`block w-full text-center py-2 rounded-lg font-medium transition ${
                          tier.highlighted
                            ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
                            : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
                        }`}
                      >
                        {tier.cta}
                      </button>
                    </Show>
                  </Show>
                </Show>
                <Show when={tier.ctaAction === 'link'}>
                  <A
                    href={tier.ctaHref ?? '/register'}
                    class="block text-center py-2 rounded-lg font-medium bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white transition"
                  >
                    {tier.cta}
                  </A>
                </Show>
              </div>
            )}
          </For>
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
