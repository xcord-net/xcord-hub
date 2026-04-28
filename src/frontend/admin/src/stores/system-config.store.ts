import { createSignal, createRoot } from 'solid-js';
import { api } from '../api/client';

export interface SystemConfig {
  paidServersDisabled: boolean;
  updatedAt: string;
}

const store = createRoot(() => {
  const [config, setConfig] = createSignal<SystemConfig | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);
  const [isSaving, setIsSaving] = createSignal(false);

  return { config, setConfig, isLoading, setIsLoading, isSaving, setIsSaving };
});

export function useSystemConfig() {
  return {
    get config() { return store.config(); },
    get isLoading() { return store.isLoading(); },
    get isSaving() { return store.isSaving(); },

    async fetch(): Promise<void> {
      store.setIsLoading(true);
      try {
        const response = await api.get<SystemConfig>('/api/v1/admin/system-config');
        store.setConfig(response);
      } finally {
        store.setIsLoading(false);
      }
    },

    async setPaidServersDisabled(disabled: boolean): Promise<void> {
      store.setIsSaving(true);
      try {
        const response = await api.put<SystemConfig>('/api/v1/admin/system-config', {
          paidServersDisabled: disabled,
        });
        store.setConfig(response);
      } finally {
        store.setIsSaving(false);
      }
    },

    reset(): void {
      store.setConfig(null);
      store.setIsLoading(false);
      store.setIsSaving(false);
    },
  };
}
