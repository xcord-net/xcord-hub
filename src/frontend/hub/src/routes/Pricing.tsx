import { A } from '@solidjs/router';
import { createSignal, For, Show } from 'solid-js';
import PageMeta from '../components/PageMeta';
import ContactModal from '../components/ContactModal';

interface TierFeature {
  label: string;
  comingSoon?: boolean;
}

interface Tier {
  name: string;
  basePrice: string;
  basePriceNote?: string;
  memberLimit: string;
  mediaAddon: string;
  mediaTotal: string;
  fullPrice: string;
  isFree: boolean;
  isEnterprise: boolean;
  features: TierFeature[];
  cta: 'get-started' | 'notify' | 'contact';
}

const tiers: Tier[] = [
  {
    name: 'Free',
    basePrice: 'Free',
    memberLimit: 'Up to 10 members',
    mediaAddon: '+$4 per user for voice & video',
    mediaTotal: '$40/mo total with media',
    fullPrice: '$40',
    isFree: true,
    isEnterprise: false,
    features: [
      { label: 'Text messaging' },
      { label: 'Bots & webhooks' },
      { label: 'Custom emoji' },
    ],
    cta: 'get-started',
  },
  {
    name: 'Basic',
    basePrice: '$60',
    basePriceNote: '/mo',
    memberLimit: 'Up to 50 members',
    mediaAddon: '+$3 per user for voice & video',
    mediaTotal: '$210/mo total with media',
    fullPrice: '$210',
    isFree: false,
    isEnterprise: false,
    features: [
      { label: 'Text messaging' },
      { label: 'Bots & webhooks' },
      { label: 'Custom emoji' },
    ],
    cta: 'notify',
  },
  {
    name: 'Pro',
    basePrice: '$150',
    basePriceNote: '/mo',
    memberLimit: 'Up to 200 members',
    mediaAddon: '+$2 per user for voice & video',
    mediaTotal: '$550/mo total with media',
    fullPrice: '$550',
    isFree: false,
    isEnterprise: false,
    features: [
      { label: 'Text messaging' },
      { label: 'Bots & webhooks' },
      { label: 'Custom emoji' },
      { label: 'Monetization tools', comingSoon: true },
    ],
    cta: 'notify',
  },
  {
    name: 'Enterprise',
    basePrice: '$300+',
    basePriceNote: '/mo',
    memberLimit: '500+ members',
    mediaAddon: '+$1 per user for voice & video',
    mediaTotal: '$800+/mo total with media',
    fullPrice: '$800+',
    isFree: false,
    isEnterprise: true,
    features: [
      { label: 'Text messaging' },
      { label: 'Bots & webhooks' },
      { label: 'Custom emoji' },
      { label: 'Monetization tools', comingSoon: true },
    ],
    cta: 'contact',
  },
];

const faqs = [
  { q: 'Can I switch plans later?', a: 'Yes, you can upgrade or downgrade at any time. Changes take effect immediately and billing is prorated.' },
  { q: 'What happens to my data if I cancel?', a: 'Your data remains available for 30 days after cancellation. You can export everything at any time.' },
  { q: 'Is the self-hosted version really free?', a: 'Yes. Xcord is open source. You only pay for your own infrastructure costs.' },
  { q: 'Can I connect a self-hosted instance to the hub?', a: 'Absolutely. Self-hosted instances can connect to the hub for discovery and SSO, or run completely standalone.' },
  { q: 'This is amazing! Can I still donate even if I have the FREE plan?', a: 'Unfortunately, no one has ever asked this question. Then again, nobody\'s ever asked the other questions, either.' },
];

export default function Pricing() {
  const [openFaq, setOpenFaq] = createSignal<number | null>(null);
  const [showContact, setShowContact] = createSignal(false);
  const [notifyCardKey, setNotifyCardKey] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [notifyMessage, setNotifyMessage] = createSignal('');

  async function handleNotify(e: Event) {
    e.preventDefault();
    const tier = notifyCardKey();
    if (!tier) return;
    setNotifyStatus('loading');
    try {
      const res = await fetch('/api/v1/mailing-list', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: notifyEmail(), tier }),
      });
      const data = await res.json();
      if (!res.ok) {
        setNotifyStatus('error');
        setNotifyMessage(data.message ?? 'Something went wrong.');
      } else {
        setNotifyStatus('success');
        setNotifyMessage(data.message);
        setTimeout(() => {
          setNotifyCardKey(null);
          setNotifyEmail('');
          setNotifyStatus('idle');
          setNotifyMessage('');
        }, 3000);
      }
    } catch {
      setNotifyStatus('error');
      setNotifyMessage('Network error. Please try again.');
    }
  }

  return (
    <>
      <PageMeta
        title="Pricing - Xcord Hub"
        description="Choose a plan based on your community size. Free tier for up to 10 members. Voice, video, and text included."
        path="/pricing"
      />
      <section class="max-w-7xl mx-auto px-6 py-20 text-center">
        <h1 class="text-4xl font-bold mb-4">
          Pricing
          <span class="ml-2 align-middle text-xs font-bold uppercase tracking-widest bg-xcord-brand/20 text-xcord-brand px-2.5 py-1 rounded-full">beta</span>
        </h1>
        <p class="text-xcord-landing-text-muted mb-12">
          Choose a plan based on your community size and the features you need.
        </p>

        {/* Tier cards */}
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 max-w-6xl mx-auto mb-6">
          <For each={tiers}>
            {(tier) => (
              <div class="bg-xcord-landing-surface border border-xcord-landing-border rounded-xl p-6 flex flex-col text-left">

                <h3 class="text-lg font-semibold text-white">{tier.name}</h3>

                {/* Base price */}
                <div class="mt-3 mb-1">
                  <Show
                    when={!tier.isFree}
                    fallback={<span class="text-3xl font-bold text-xcord-brand">Free</span>}
                  >
                    <span class="text-3xl font-bold text-white">{tier.basePrice}</span>
                    <span class="text-sm text-xcord-landing-text-muted">{tier.basePriceNote}</span>
                  </Show>
                </div>

                {/* Member limit */}
                <p class="text-sm text-xcord-landing-text-muted mb-2">{tier.memberLimit}</p>

                {/* Media add-on */}
                <p class="text-xs text-xcord-brand font-medium mb-6">{tier.mediaAddon}</p>

                {/* Feature list */}
                <ul class="space-y-3 flex-1 mb-6">
                  <For each={tier.features}>
                    {(feat) => (
                      <li class="flex items-center gap-2 text-sm">
                        <svg class="w-4 h-4 text-xcord-brand shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                        </svg>
                        <span class="text-xcord-landing-text">
                          {feat.label}
                        </span>
                        <Show when={feat.comingSoon}>
                          <span class="text-xs font-medium bg-xcord-brand/20 text-xcord-brand px-1.5 py-0.5 rounded-full leading-none shrink-0">
                            Soon
                          </span>
                        </Show>
                      </li>
                    )}
                  </For>
                </ul>

                {/* CTA */}
                <Show when={tier.cta === 'get-started'}>
                  <A
                    href="/register"
                    class="block text-center py-2.5 rounded-lg font-medium transition bg-xcord-brand hover:bg-xcord-brand-hover text-white"
                  >
                    Get Started
                  </A>
                </Show>

                <Show when={tier.cta === 'contact'}>
                  <button
                    onClick={() => setShowContact(true)}
                    class="w-full block text-center py-2.5 rounded-lg font-medium transition border border-xcord-brand text-xcord-brand hover:bg-xcord-brand hover:text-white"
                  >
                    Contact Us
                  </button>
                </Show>

                <Show when={tier.cta === 'notify'}>
                  <Show
                    when={notifyCardKey() === tier.name}
                    fallback={
                      <button
                        onClick={() => {
                          setNotifyCardKey(tier.name);
                          setNotifyStatus('idle');
                          setNotifyMessage('');
                          setNotifyEmail('');
                        }}
                        class="w-full py-2.5 rounded-lg font-medium transition border border-xcord-brand text-xcord-brand hover:bg-xcord-brand hover:text-white"
                      >
                        Notify Me
                      </button>
                    }
                  >
                    <Show
                      when={notifyStatus() !== 'success'}
                      fallback={<p class="text-sm text-green-400 text-center py-2.5">{notifyMessage()}</p>}
                    >
                      <form onSubmit={handleNotify} class="flex gap-2">
                        <input
                          type="email"
                          required
                          placeholder="you@example.com"
                          value={notifyEmail()}
                          onInput={(e) => setNotifyEmail(e.currentTarget.value)}
                          class="flex-1 px-3 py-2 rounded-lg bg-xcord-landing-bg border border-xcord-landing-border text-white text-sm placeholder:text-xcord-landing-text-muted/50 focus:outline-none focus:border-xcord-brand"
                        />
                        <button
                          type="submit"
                          disabled={notifyStatus() === 'loading'}
                          class="px-4 py-2 rounded-lg font-medium bg-xcord-brand text-white text-sm hover:bg-xcord-brand-hover disabled:opacity-50"
                        >
                          {notifyStatus() === 'loading' ? '...' : 'Go'}
                        </button>
                      </form>
                      <Show when={notifyStatus() === 'error'}>
                        <p class="text-xs text-red-400 mt-1">{notifyMessage()}</p>
                      </Show>
                    </Show>
                  </Show>
                </Show>
              </div>
            )}
          </For>
        </div>

        <p class="text-sm text-xcord-landing-text-muted mb-20">
          All plans are billed monthly. No long-term contracts.
        </p>

        {/* Split card: Self-Hosted */}
        <div class="max-w-3xl mx-auto rounded-xl text-left flex flex-col border border-xcord-landing-border overflow-hidden mb-20">
          <div class="flex-1 p-6 bg-xcord-landing-surface flex flex-col">
            <h3 class="text-lg font-semibold text-white">Self-Hosted</h3>
            <p class="text-sm text-xcord-brand font-medium mt-2">Full control</p>
            <p class="text-sm text-xcord-landing-text-muted mt-2 mb-6 flex-1">
              Unlimited everything, your servers
            </p>
            <A
              href="/docs/self-hosting"
              class="block text-center py-2 rounded-lg font-medium bg-xcord-bg-accent hover:bg-xcord-bg-primary text-white transition"
            >
              View Docs
            </A>
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

      <ContactModal open={showContact()} onClose={() => setShowContact(false)} />
    </>
  );
}
