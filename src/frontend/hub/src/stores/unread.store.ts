import { createSignal } from 'solid-js';

interface UnreadCounts {
  [instanceUrl: string]: number;
}

const [unreadCounts, setUnreadCounts] = createSignal<UnreadCounts>({});

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
};

// Listen for postMessage from instance iframes
// Protocol: { type: "xcord_unread", instanceUrl: string, count: number }
window.addEventListener('message', (event) => {
  if (event.data?.type === 'xcord_unread') {
    const { instanceUrl, count } = event.data;
    if (typeof instanceUrl === 'string' && typeof count === 'number') {
      unreadStore.setUnreadCount(instanceUrl, count);
    }
  }
});
