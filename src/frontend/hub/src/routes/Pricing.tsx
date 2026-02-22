import { A } from '@solidjs/router';
import { createSignal, For } from 'solid-js';

const tiers = [
  {
    name: 'Free',
    price: '$0',
    period: '/month',
    description: 'Perfect for getting started',
    features: ['1 instance', '50 members', '1 GB storage', 'Community support'],
    cta: 'Get Started',
    highlighted: false,
  },
  {
    name: 'Basic',
    price: '$5',
    period: '/month',
    description: 'For growing communities',
    features: ['3 instances', '500 members each', '10 GB storage', 'Email support', 'Custom domain'],
    cta: 'Start Free Trial',
    highlighted: false,
  },
  {
    name: 'Pro',
    price: '$15',
    period: '/month',
    description: 'For serious communities',
    features: ['Unlimited instances', 'Unlimited members', '100 GB storage', 'Priority support', 'Custom domain', 'Custom branding', 'API access'],
    cta: 'Start Free Trial',
    highlighted: true,
  },
  {
    name: 'Self-Hosted',
    price: 'Free',
    period: 'forever',
    description: 'Full control, your infrastructure',
    features: ['Unlimited everything', 'Your own servers', 'Full source access', 'Community support', 'Docker & Kubernetes'],
    cta: 'View Docs',
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

  return (
    <>
      <section class="max-w-7xl mx-auto px-6 py-20 text-center">
        <h1 class="text-4xl font-bold mb-4">Simple, transparent pricing</h1>
        <p class="text-xcord-landing-text-muted max-w-xl mx-auto mb-16">
          Start free, scale as you grow. No hidden fees, no surprises.
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
                <A
                  href="/register"
                  class={`block text-center py-2 rounded-lg font-medium transition ${
                    tier.highlighted
                      ? 'bg-xcord-brand hover:bg-xcord-brand-hover text-white'
                      : 'bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white'
                  }`}
                >
                  {tier.cta}
                </A>
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
