import { A } from '@solidjs/router';
import { createSignal, For, Show } from 'solid-js';
import PageMeta from '../components/PageMeta';

type UserCount = 10 | 50 | 100 | 500;

interface FeatureCard {
  name: string;
  price: string;
  isFree: boolean;
  hdAddon?: string;
  features: { label: string; included: boolean }[];
}

const userCounts: UserCount[] = [10, 50, 100, 500];

const featureList = [
  'Text messaging',
  'Bots & webhooks',
  'Custom emoji',
  'Threads & forums',
  'Voice channels',
  'Video & screen share',
];

function getCards(users: UserCount): FeatureCard[] {
  // Prices must match backend TierDefaults.GetPriceCents and PRICING.md
  const prices: Record<UserCount, [string, string, string, string]> = {
    10: ['Free', '$20', '$40', '+$25'],
    50: ['$20', '$45', '$70', '+$50'],
    100: ['$60', '$110', '$160', '+$75'],
    500: ['$200', '$400', '$550', '+$150'],
  };
  const [chat, audio, video, hd] = prices[users];

  return [
    {
      name: 'Chat',
      price: chat,
      isFree: chat === 'Free',
      features: [true, true, true, true, false, false].map((v, i) => ({
        label: featureList[i],
        included: v,
      })),
    },
    {
      name: 'Chat + Audio',
      price: audio,
      isFree: false,
      features: [true, true, true, true, true, false].map((v, i) => ({
        label: featureList[i],
        included: v,
      })),
    },
    {
      name: 'Chat + Audio + Video',
      price: video,
      isFree: false,
      hdAddon: hd,
      features: [true, true, true, true, true, true].map((v, i) => ({
        label: featureList[i],
        included: v,
      })),
    },
  ];
}

const faqs = [
  { q: 'Can I switch plans later?', a: 'Yes, you can upgrade or downgrade at any time. Changes take effect immediately and billing is prorated.' },
  { q: 'What happens to my data if I cancel?', a: 'Your data remains available for 30 days after cancellation. You can export everything at any time.' },
  { q: 'Is the self-hosted version really free?', a: 'Yes. Xcord is open source. You only pay for your own infrastructure costs.' },
  { q: 'Can I connect a self-hosted instance to the hub?', a: 'Absolutely. Self-hosted instances can connect to the hub for discovery and SSO, or run completely standalone.' },
  { q: 'This is amazing! Can I still donate even if I have the FREE plan?', a: 'Unfortunately, no one has ever asked this question. Then again, nobody\'s ever asked the other questions, either.' },
];

export default function Pricing() {
  const [selectedUsers, setSelectedUsers] = createSignal<UserCount>(10);
  const [openFaq, setOpenFaq] = createSignal<number | null>(null);
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

  const cards = () => getCards(selectedUsers());

  return (
    <>
      <PageMeta
        title="Pricing - Xcord Hub"
        description="Choose a plan based on your community size. Free tier for up to 10 users. Voice, video, and text included."
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

        {/* User count tabs */}
        <div class="flex justify-center gap-2 mb-12">
          <For each={userCounts}>
            {(count) => (
              <button
                onClick={() => setSelectedUsers(count)}
                class={`px-5 py-2 rounded-lg text-sm font-medium transition ${
                  selectedUsers() === count
                    ? 'bg-xcord-brand text-white'
                    : 'bg-xcord-landing-surface text-xcord-landing-text-muted hover:text-white hover:bg-xcord-landing-surface/80'
                }`}
              >
                {count} users
              </button>
            )}
          </For>
        </div>

        {/* Feature tier cards */}
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-4xl mx-auto mb-6">
          <For each={cards()}>
            {(card) => (
              <div class="bg-xcord-landing-surface border border-xcord-landing-border rounded-xl p-6 flex flex-col text-left">
                <h3 class="text-lg font-semibold text-white">{card.name}</h3>
                <div class="mt-3 mb-6">
                  <Show
                    when={!card.isFree}
                    fallback={<span class="text-3xl font-bold text-xcord-brand">Free</span>}
                  >
                    <span class="text-3xl font-bold text-white">{card.price}</span>
                    <span class="text-sm text-xcord-landing-text-muted">/mo</span>
                  </Show>
                  <Show when={card.hdAddon}>
                    <span class="ml-2 text-sm font-medium text-xcord-brand">{card.hdAddon} HD</span>
                  </Show>
                </div>

                <ul class="space-y-3 flex-1 mb-6">
                  <For each={card.features}>
                    {(feat) => (
                      <li class="flex items-center gap-2 text-sm">
                        <Show
                          when={feat.included}
                          fallback={
                            <svg class="w-4 h-4 text-xcord-landing-text-muted/40 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                            </svg>
                          }
                        >
                          <svg class="w-4 h-4 text-xcord-brand shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                          </svg>
                        </Show>
                        <span class={feat.included ? 'text-xcord-landing-text' : 'text-xcord-landing-text-muted/40 line-through'}>
                          {feat.label}
                        </span>
                      </li>
                    )}
                  </For>
                </ul>

                <Show
                  when={card.isFree}
                  fallback={
                    <Show
                      when={notifyCardKey() === `${card.name} (${selectedUsers()} users)`}
                      fallback={
                        <button
                          onClick={() => {
                            setNotifyCardKey(`${card.name} (${selectedUsers()} users)`);
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
                  }
                >
                  <A
                    href="/register"
                    class="block text-center py-2.5 rounded-lg font-medium transition bg-xcord-brand hover:bg-xcord-brand-hover text-white"
                  >
                    Get Started
                  </A>
                </Show>
              </div>
            )}
          </For>
        </div>

        <p class="text-sm text-xcord-landing-text-muted mb-4">
          All plans are billed monthly. No long-term contracts.
        </p>
        <p class="text-sm text-xcord-landing-text-muted mb-20">
          Need more than 500 users?{' '}
          <a href="mailto:sales@xcord.net" class="text-xcord-brand hover:underline">
            Contact us
          </a>{' '}
          for a custom plan.
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
    </>
  );
}
