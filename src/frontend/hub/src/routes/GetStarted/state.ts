import type { Stripe, StripeElements } from '@stripe/stripe-js';

// Must match backend ValidationHelpers.ReservedSubdomains
export const RESERVED = new Set([
  'www', 'mail', 'smtp', 'imap', 'pop', 'ftp',
  'docker', 'registry',
  'api', 'admin', 'hub', 'auth',
  'ns1', 'ns2', 'ns3', 'ns4',
  'caddy', 'proxy', 'lb',
  'pg', 'postgres', 'redis', 'minio', 's3',
  'livekit', 'rtc', 'turn', 'stun',
  'status', 'monitor', 'grafana', 'prometheus',
  '_dmarc', 'autoconfig', 'autodiscover',
]);

export interface StripeContext {
  stripe: Stripe;
  elements: StripeElements;
}

export type Tier = 'Free' | 'Basic' | 'Pro';
export type SubdomainStatus = 'idle' | 'checking' | 'available' | 'taken';
export type NotifyStatus = 'idle' | 'loading' | 'success' | 'error';

// Tier base prices in dollars
export const TIER_BASE_PRICE: Record<string, number> = {
  Basic: 60,
  Pro: 150,
};

// Media addon total price (base + per-user * max users)
export const TIER_MEDIA_ADDON: Record<string, number> = {
  Basic: 150,  // $3/user * 50 users
  Pro: 400,    // $2/user * 200 users
};

export interface FeaturesResponse {
  paymentsEnabled: boolean;
  paidServersDisabled?: boolean;
  stripePublishableKey?: string;
}

export interface CheckSubdomainResponse {
  available: boolean;
  reason?: string;
}

export interface CreatePaymentIntentResponse {
  clientSecret: string;
}

export interface MailingListResponse {
  message: string;
}

export interface BillingListResponse {
  instances?: Array<unknown>;
}

export interface ApiError {
  message?: string;
}
