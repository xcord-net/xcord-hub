export interface InstanceBillingItem {
  instanceId: string;
  domain: string;
  displayName: string;
  tier: string;
  mediaEnabled: boolean;
  priceCents: number;
  billingStatus: string;
}

export interface BillingData {
  instances: InstanceBillingItem[];
}

export interface InvoiceSummary {
  id: string;
  description: string;
  amountCents: number;
  currency: string;
  status: string;
  createdAt: string;
  pdfUrl: string | null;
}

export interface InvoicesData {
  invoices: InvoiceSummary[];
}

export type Tier = 'Free' | 'Basic' | 'Pro' | 'Enterprise';

export const TIER_CONFIG: Record<Tier, { maxUsers: number; label: string }> = {
  Free:       { maxUsers: 10,  label: 'Free' },
  Basic:      { maxUsers: 50,  label: 'Basic' },
  Pro:        { maxUsers: 200, label: 'Pro' },
  Enterprise: { maxUsers: 500, label: 'Enterprise' },
};

export const TIER_PRICE_CENTS: Record<Tier, number> = {
  Free: 0,
  Basic: 6000,
  Pro: 15000,
  Enterprise: 30000,
};

export const TIER_MEDIA_CENTS: Record<Tier, number> = {
  Free: 400,
  Basic: 300,
  Pro: 200,
  Enterprise: 100,
};

export interface UsageData {
  instanceId: string;
  domain: string;
  tier: string;
  isMeteredBilling: boolean;
  totalUptimeMinutes: number;
  totalUptimeHours: number;
  uptimePercentage: number;
  estimatedCostCents: number;
}

export function authHeaders(): HeadersInit {
  const token = localStorage.getItem('xcord_hub_token');
  return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : {};
}

export async function fetchBilling(): Promise<BillingData> {
  const res = await fetch('/api/v1/hub/billing', { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load billing info');
  return res.json();
}

export async function fetchUsage(instanceId: string): Promise<UsageData> {
  const res = await fetch(`/api/v1/hub/instances/${instanceId}/usage`, { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load usage');
  return res.json();
}

export async function fetchInvoices(): Promise<InvoicesData> {
  const res = await fetch('/api/v1/hub/billing/invoices', { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load invoices');
  return res.json();
}

export function formatAmount(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: currency.toUpperCase(),
  }).format(cents / 100);
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

export function statusBadge(status: string): string {
  const classes: Record<string, string> = {
    Active: 'bg-xcord-green/10 text-xcord-green',
    PastDue: 'bg-yellow-500/10 text-yellow-400',
    Suspended: 'bg-xcord-red/10 text-xcord-red',
    Cancelled: 'bg-xcord-bg-tertiary text-xcord-text-muted',
  };
  return classes[status] ?? 'bg-xcord-bg-tertiary text-xcord-text-muted';
}

export function formatPrice(cents: number): string {
  if (cents === 0) return 'Free';
  const dollars = cents / 100;
  const formatted = dollars % 1 === 0 ? `$${dollars}` : `$${dollars.toFixed(2)}`;
  return `${formatted}/mo`;
}

export function formatTier(tier: string, media: boolean): string {
  return media ? `${tier} + Media` : tier;
}

export function computeTotalCents(tier: Tier, mediaEnabled: boolean): number {
  const base = TIER_PRICE_CENTS[tier];
  const maxUsers = TIER_CONFIG[tier].maxUsers;
  const mediaCents = mediaEnabled ? TIER_MEDIA_CENTS[tier] * maxUsers : 0;
  return base + mediaCents;
}
