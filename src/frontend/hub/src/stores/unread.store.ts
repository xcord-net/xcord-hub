import { createSignal } from 'solid-js';

interface UnreadCounts {
  [instanceUrl: string]: number;
}

const [unreadCounts, setUnreadCounts] = createSignal<UnreadCounts>({});

// Origins of currently mounted instance iframes. Multiple iframes can resolve to
// the same origin; we ref-count so an unmount of one doesn't drop a sibling's trust.
const trustedOrigins = new Map<string, { fullUrl: string; refCount: number }>();

function tryGetOrigin(url: string): string | null {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

export const unreadStore = {
  unreadCounts,

  setUnreadCount(instanceUrl: string, count: number) {
    setUnreadCounts(prev => ({
      ...prev,
      [instanceUrl]: count,
    }));
  },

  getUnreadCount(instanceUrl: string): number {
    return unreadCounts()[instanceUrl] || 0;
  },

  clearUnreadCount(instanceUrl: string) {
    setUnreadCounts(prev => {
      const updated = { ...prev };
      delete updated[instanceUrl];
      return updated;
    });
  },

  getTotalUnread(): number {
    return Object.values(unreadCounts()).reduce((sum, count) => sum + count, 0);
  },

  addTrustedInstance(url: string) {
    const origin = tryGetOrigin(url);
    if (origin === null) return;
    const existing = trustedOrigins.get(origin);
    if (existing) {
      existing.refCount += 1;
    } else {
      trustedOrigins.set(origin, { fullUrl: url, refCount: 1 });
    }
  },

  removeTrustedInstance(url: string) {
    const origin = tryGetOrigin(url);
    if (origin === null) return;
    const existing = trustedOrigins.get(origin);
    if (!existing) return;
    existing.refCount -= 1;
    if (existing.refCount <= 0) {
      trustedOrigins.delete(origin);
    }
  },

  reset(): void {
    setUnreadCounts({});
    trustedOrigins.clear();
  },
};

window.addEventListener('message', (event) => {
  const origin = event.origin;
  if (!trustedOrigins.has(origin)) return;
  const data = event.data;
  if (!data || data.type !== 'xcord_unread') return;
  const { instanceUrl, count } = data;
  if (typeof instanceUrl !== 'string') return;
  if (typeof count !== 'number' || !Number.isFinite(count) || count < 0) return;
  const claimedOrigin = tryGetOrigin(instanceUrl);
  if (claimedOrigin !== origin) return;
  unreadStore.setUnreadCount(instanceUrl, count);
});
