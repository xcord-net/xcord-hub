import { createSignal } from 'solid-js';
import type { components } from '@generated/api-types';

export interface ConnectedInstance {
  url: string;
  name: string;
  iconUrl?: string;
}

export interface DiscoverableInstance {
  id: string;
  name: string;
  description: string;
  url: string;
  iconUrl?: string;
  memberCount: number;
  isPublic: boolean;
}

const STORAGE_KEY = 'xcord_connected_instances';

// Load from localStorage
const loadConnectedInstances = (): ConnectedInstance[] => {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? JSON.parse(stored) : [];
  } catch {
    return [];
  }
};

// Save to localStorage
const saveConnectedInstances = (instances: ConnectedInstance[]) => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(instances));
  } catch (error) {
    console.error('Failed to save connected instances:', error);
  }
};

const [connectedInstances, setConnectedInstances] = createSignal<ConnectedInstance[]>(loadConnectedInstances());
const [selectedInstanceUrl, setSelectedInstanceUrl] = createSignal<string | null>(null);

export const instanceStore = {
  connectedInstances,
  selectedInstanceUrl,

  addInstance(instance: ConnectedInstance) {
    const current = connectedInstances();
    if (current.some(i => i.url === instance.url)) return;

    const updated = [...current, instance];
    setConnectedInstances(updated);
    saveConnectedInstances(updated);
    setSelectedInstanceUrl(instance.url);
  },

  removeInstance(url: string) {
    const updated = connectedInstances().filter(i => i.url !== url);
    setConnectedInstances(updated);
    saveConnectedInstances(updated);

    if (selectedInstanceUrl() === url) {
      setSelectedInstanceUrl(null);
    }
  },

  selectInstance(url: string | null) {
    setSelectedInstanceUrl(url);
  },

  async searchInstances(query: string): Promise<DiscoverableInstance[]> {
    try {
      const response = await fetch(`/api/v1/discover/instances?search=${encodeURIComponent(query)}`);
      if (!response.ok) return [];
      return await response.json();
    } catch {
      return [];
    }
  },

  async listPublicInstances(): Promise<DiscoverableInstance[]> {
    try {
      const response = await fetch('/api/v1/discover/instances');
      if (!response.ok) return [];
      return await response.json();
    } catch {
      return [];
    }
  },

  async createInstance(subdomain: string, displayName: string, adminPassword: string, featureTier: string = 'Chat', userCountTier: string = 'Tier10'): Promise<components['schemas']['CreateInstanceResponse']> {
    try {
      const response = await fetch('/api/v1/hub/instances', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ subdomain, displayName, adminPassword, featureTier, userCountTier }),
      });
      if (!response.ok) {
        const err = await response.json().catch(() => ({ message: 'Failed to create instance' }));
        throw new Error(err.detail || err.message || 'Failed to create instance');
      }
      return await response.json();
    } catch (error) {
      throw error;
    }
  },
};
