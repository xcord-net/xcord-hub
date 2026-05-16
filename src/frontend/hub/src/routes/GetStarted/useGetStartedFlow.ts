import { createSignal, createEffect, onMount } from 'solid-js';
import type { Setter } from 'solid-js';
import { useNavigate, useSearchParams } from '@solidjs/router';
import { loadStripe } from '@stripe/stripe-js';
import { useAuth } from '../../stores/auth.store';
import { instanceStore } from '../../stores/instance.store';
import { api } from '../../api/client';
import {
  RESERVED,
  type StripeContext,
  type Tier,
  type SubdomainStatus,
  type NotifyStatus,
  type FeaturesResponse,
  type CheckSubdomainResponse,
  type CreatePaymentIntentResponse,
  type MailingListResponse,
  type BillingListResponse,
  type ApiError,
} from './state';

export function useGetStartedFlow() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const queryTier = searchParams.tier;
  const initialTier: Tier = (queryTier === 'Free' || queryTier === 'Basic' || queryTier === 'Pro')
    ? queryTier
    : 'Free';

  // Wizard
  const [step, setStep] = createSignal(1);
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [ready, setReady] = createSignal(false);

  // Features
  const [paymentsEnabled, setPaymentsEnabled] = createSignal(false);
  const [paidServersDisabled, setPaidServersDisabled] = createSignal(false);
  const [stripePublishableKey, setStripePublishableKey] = createSignal('');

  // Step 1
  const [subdomain, setSubdomain] = createSignal('');
  const [serverName, setServerName] = createSignal('');
  const [selectedTier, setSelectedTier] = createSignal<Tier>(initialTier);
  const [mediaEnabled, setMediaEnabled] = createSignal(false);
  const [subdomainStatus, setSubdomainStatus] = createSignal<SubdomainStatus>('idle');
  const [subdomainReason, setSubdomainReason] = createSignal('');

  // Step 2
  const [clientSecret, setClientSecret] = createSignal('');
  const [paymentMethodId, setPaymentMethodId] = createSignal('');
  const [stripeCtx, setStripeCtx] = createSignal<StripeContext | null>(null);

  // Step 3
  const [email, setEmail] = createSignal('');
  const [username, setUsername] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [agreed, setAgreed] = createSignal(false);
  const [ageConfirmed, setAgeConfirmed] = createSignal(false);
  const [jurisdictionConfirmed, setJurisdictionConfirmed] = createSignal(false);
  const [captchaId, setCaptchaId] = createSignal('');
  const [captchaAnswer, setCaptchaAnswer] = createSignal('');

  // Modals
  const [showContact, setShowContact] = createSignal(false);
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<NotifyStatus>('idle');
  const [notifyMessage, setNotifyMessage] = createSignal('');

  const isLoggedIn = () => auth.isAuthenticated;
  const paidTierAvailable = () => paymentsEnabled() && !paidServersDisabled();
  const isPaidTier = () => (selectedTier() !== 'Free' || mediaEnabled()) && paidTierAvailable();
  const totalSteps = () => isLoggedIn() ? (isPaidTier() ? 2 : 1) : (isPaidTier() ? 3 : 2);
  const accountStep = () => isPaidTier() ? 3 : 2;

  onMount(async () => {
    try {
      const feat = await api.get<FeaturesResponse>('/api/v1/hub/features');
      setPaymentsEnabled(feat.paymentsEnabled);
      setPaidServersDisabled(!!feat.paidServersDisabled);
      if (feat.stripePublishableKey) setStripePublishableKey(feat.stripePublishableKey);
    } catch { /* default false */ }

    const hasToken = !!localStorage.getItem('xcord_hub_token');
    const restored = hasToken ? await auth.restoreSession() : false;
    if (restored) {
      try {
        const data = await api.get<BillingListResponse>('/api/v1/hub/billing');
        if (data.instances && data.instances.length > 0) {
          navigate('/dashboard', { replace: true });
          return;
        }
      } catch { /* allow form - backend enforces */ }
    }
    setReady(true);
  });

  createEffect(async () => {
    const secret = clientSecret();
    if (!secret) return;
    const key = stripePublishableKey();
    if (!key) return;
    const stripe = await loadStripe(key);
    if (!stripe) return;

    const elements = stripe.elements({
      clientSecret: secret,
      appearance: {
        theme: 'night',
        variables: {
          colorPrimary: '#d4943a',
          colorBackground: '#1e1f22',
          colorText: '#dbdee1',
          colorTextSecondary: '#949ba4',
          borderRadius: '6px',
        },
      },
    });
    const paymentElement = elements.create('payment', { layout: { type: 'tabs', defaultCollapsed: false } });
    paymentElement.mount('#payment-element');
    setStripeCtx({ stripe, elements });
  });

  const subdomainError = () => {
    const s = subdomain();
    if (!s) return '';
    if (s.length < 6) return 'Must be at least 6 characters';
    if (s.startsWith('-') || s.endsWith('-')) return 'Cannot start or end with a hyphen';
    if (s.includes('--')) return 'Cannot contain consecutive hyphens';
    if (RESERVED.has(s)) return `'${s}' is reserved for infrastructure use`;
    if (subdomainStatus() === 'taken') return subdomainReason() || 'Already taken';
    return '';
  };
  const subdomainValid = () => subdomain().length >= 6 && !subdomainError();

  let checkTimer: ReturnType<typeof setTimeout>;
  const handleSubdomainInput = (value: string) => {
    const clean = value.toLowerCase().replace(/[^a-z0-9-]/g, '');
    setSubdomain(clean);
    setSubdomainStatus('idle');
    setSubdomainReason('');

    clearTimeout(checkTimer);
    if (clean.length >= 6 && !RESERVED.has(clean) && !clean.startsWith('-') && !clean.endsWith('-') && !clean.includes('--')) {
      setSubdomainStatus('checking');
      checkTimer = setTimeout(async () => {
        try {
          const data = await api.get<CheckSubdomainResponse>(`/api/v1/hub/check-subdomain?subdomain=${encodeURIComponent(clean)}`);
          setSubdomainStatus(data.available ? 'available' : 'taken');
          setSubdomainReason(data.reason ?? '');
        } catch {
          setSubdomainStatus('idle');
        }
      }, 500);
    }
  };

  const canProceedStep1 = () => subdomainValid() && subdomainStatus() === 'available' && serverName().trim().length > 0;

  const handleSubmitLoggedIn = async () => {
    setError('');
    setLoading(true);
    try {
      await instanceStore.createInstance(subdomain(), serverName(), '', selectedTier(), mediaEnabled());
      navigate('/dashboard', { replace: true });
    } catch (err: unknown) {
      const msg = (err as ApiError | null)?.message;
      if (msg?.includes('SUBDOMAIN_TAKEN')) setSubdomainStatus('taken');
      setError(msg ?? 'Failed to create server');
    } finally {
      setLoading(false);
    }
  };

  const handleNextToPayment = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await api.post<CreatePaymentIntentResponse>('/api/v1/hub/billing/create-payment-intent', {
        tier: selectedTier(),
        mediaEnabled: mediaEnabled(),
      });
      setClientSecret(data.clientSecret);
      setStep(2);
    } catch (err: unknown) {
      const msg = (err as ApiError | null)?.message;
      setError(msg ?? 'Failed to initialize payment');
    } finally {
      setLoading(false);
    }
  };

  const handleNext = () => {
    if (isPaidTier()) handleNextToPayment();
    else if (isLoggedIn()) handleSubmitLoggedIn();
    else { setStep(2); setError(''); }
  };

  const handleBack = () => { setStep(1); setError(''); };
  const handleBackFromPayment = () => { setStep(1); setError(''); setClientSecret(''); setStripeCtx(null); };

  const handleConfirmPayment = async () => {
    const ctx = stripeCtx();
    if (!ctx) { setError('Payment form not ready'); return; }
    setLoading(true);
    setError('');

    const { error: stripeError, setupIntent } = await ctx.stripe.confirmSetup({
      elements: ctx.elements,
      redirect: 'if_required',
    });

    if (stripeError) { setError(stripeError.message || 'Card validation failed'); setLoading(false); return; }
    if (!setupIntent?.payment_method) { setError('Card setup did not complete'); setLoading(false); return; }

    setPaymentMethodId(typeof setupIntent.payment_method === 'string'
      ? setupIntent.payment_method
      : setupIntent.payment_method.id);
    setLoading(false);

    if (isLoggedIn()) handleSubmitLoggedIn();
    else { setStep(3); setError(''); }
  };

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');
    if (password() !== confirmPassword()) { setError('Passwords do not match'); return; }
    if (password().length < 8) { setError('Password must be at least 8 characters'); return; }
    if (!agreed() || !ageConfirmed() || !jurisdictionConfirmed()) { setError('You must agree to all terms'); return; }

    setLoading(true);
    const result = await auth.signupWithInstance(
      email(), password(), displayName() || username(), username(),
      subdomain(), serverName(), selectedTier(), mediaEnabled(),
      captchaId(), captchaAnswer(), paymentMethodId() || undefined
    );

    setLoading(false);
    if (result) {
      navigate('/dashboard', { replace: true });
    } else if (auth.error?.includes('SUBDOMAIN_TAKEN')) {
      setStep(1);
      setSubdomainStatus('taken');
      setError('This subdomain was taken while you were signing up. Please choose another.');
    }
  };

  const handleNotify = async (e: Event) => {
    e.preventDefault();
    const tier = notifyTier();
    if (!tier) return;
    setNotifyStatus('loading');
    try {
      const data = await api.post<MailingListResponse>('/api/v1/mailing-list', { email: notifyEmail(), tier });
      setNotifyStatus('success');
      setNotifyMessage(data.message);
      setTimeout(() => {
        setNotifyTier(null);
        setNotifyEmail('');
        setNotifyStatus('idle');
        setNotifyMessage('');
      }, 3000);
    } catch (err: unknown) {
      setNotifyStatus('error');
      const msg = (err as ApiError | null)?.message;
      setNotifyMessage(msg ?? 'Network error. Please try again.');
    }
  };

  const handleNotifyOpen = (tier: string) => {
    setNotifyTier(tier);
    setNotifyStatus('idle');
    setNotifyMessage('');
    setNotifyEmail('');
  };

  const handleCaptchaSolved = (id: string, ans: string) => { setCaptchaId(id); setCaptchaAnswer(ans); };

  return {
    auth, step, setStep, loading, error, setError, ready,
    paidTierAvailable, isPaidTier, isLoggedIn, totalSteps, accountStep,
    subdomain, subdomainStatus, subdomainError, subdomainValid, handleSubdomainInput,
    serverName, setServerName, selectedTier, setSelectedTier, mediaEnabled, setMediaEnabled,
    stripeCtx,
    email, setEmail, username, setUsername, displayName, setDisplayName,
    password, setPassword, confirmPassword, setConfirmPassword,
    agreed, setAgreed, ageConfirmed, setAgeConfirmed,
    jurisdictionConfirmed, setJurisdictionConfirmed,
    showContact, setShowContact,
    notifyTier, setNotifyTier, notifyEmail, setNotifyEmail,
    notifyStatus, setNotifyStatus, notifyMessage, setNotifyMessage,
    canProceedStep1, handleNext, handleBack, handleBackFromPayment,
    handleConfirmPayment, handleSubmit, handleNotify, handleNotifyOpen, handleCaptchaSolved,
  };
}
