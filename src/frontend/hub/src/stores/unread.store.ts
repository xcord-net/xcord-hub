import { createSignal } from 'solid-js';
import { parseHubMessage, HubMessageType } from '../protocol/hubProtocol';

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

// Validation lives in the protocol module so the hub and every instance apply
// the same rules to the same messages.
window.addEventListener('message', (event) => {
  const message = parseHubMessage(event, {
    isTrustedOrigin: (origin) => trustedOrigins.has(origin),
  });
  if (message?.type !== HubMessageType.Unread) return;
  unreadStore.setUnreadCount(message.instanceUrl, message.count);
});
