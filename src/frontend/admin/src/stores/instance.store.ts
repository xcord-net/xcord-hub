import { createSignal, createRoot } from 'solid-js';
import { api } from '../api/client';
import type {
  InstanceListItem,
  InstanceDetail,
  InstanceListResponse,
  ProvisionInstanceRequest,
  ResourceLimits,
  FeatureFlags,
  LogEntry,
  InstanceStatus,
} from '../types/instance';

const store = createRoot(() => {
  const [instances, setInstances] = createSignal<InstanceListItem[]>([]);
  const [selectedInstance, setSelectedInstance] = createSignal<InstanceDetail | null>(null);
  const [logs, setLogs] = createSignal<LogEntry[]>([]);
  const [total, setTotal] = createSignal(0);
  const [page, setPage] = createSignal(1);
  const [pageSize] = createSignal(20);
  const [statusFilter, setStatusFilter] = createSignal<InstanceStatus | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);

  return {
    instances,
    setInstances,
    selectedInstance,
    setSelectedInstance,
    logs,
    setLogs,
    total,
    setTotal,
    page,
    setPage,
    pageSize,
    statusFilter,
    setStatusFilter,
    isLoading,
    setIsLoading,
  };
});

export function useInstances() {
  return {
    get instances() { return store.instances(); },
    get selectedInstance() { return store.selectedInstance(); },
    get logs() { return store.logs(); },
    get total() { return store.total(); },
    get page() { return store.page(); },
    get pageSize() { return store.pageSize(); },
    get statusFilter() { return store.statusFilter(); },
    get isLoading() { return store.isLoading(); },

    setPage(p: number) {
      store.setPage(p);
    },

    setStatusFilter(status: InstanceStatus | null) {
      store.setStatusFilter(status);
      store.setPage(1);
    },

    async fetchInstances(): Promise<void> {
      store.setIsLoading(true);
      try {
        let url = `/api/v1/admin/instances?page=${store.page()}&pageSize=${store.pageSize()}`;
        if (store.statusFilter()) {
          url += `&status=${store.statusFilter()}`;
        }

        const response = await api.get<InstanceListResponse>(url);
        store.setInstances(response.instances);
        store.setTotal(response.total);
      } finally {
        store.setIsLoading(false);
      }
    },

    async fetchInstanceDetail(id: string): Promise<void> {
      store.setIsLoading(true);
      try {
        const instance = await api.get<InstanceDetail>(`/api/v1/admin/instances/${id}`);
        store.setSelectedInstance(instance);
      } finally {
        store.setIsLoading(false);
      }
    },

    async fetchInstanceLogs(id: string, limit = 100): Promise<void> {
      try {
        const logs = await api.get<LogEntry[]>(`/api/v1/admin/instances/${id}/logs?limit=${limit}`);
        store.setLogs(logs);
      } catch (error) {
        console.error('Failed to fetch logs:', error);
        store.setLogs([]);
      }
    },

    async provisionInstance(request: ProvisionInstanceRequest): Promise<InstanceDetail> {
      const instance = await api.post<InstanceDetail>('/api/v1/admin/instances', request);
      return instance;
    },

    async suspendInstance(id: string): Promise<void> {
      await api.post(`/api/v1/admin/instances/${id}/suspend`);
      await this.fetchInstanceDetail(id);
    },

    async resumeInstance(id: string): Promise<void> {
      await api.post(`/api/v1/admin/instances/${id}/resume`);
      await this.fetchInstanceDetail(id);
    },

    async destroyInstance(id: string): Promise<void> {
      await api.delete(`/api/v1/admin/instances/${id}`);
      await this.fetchInstances();
      store.setSelectedInstance(null);
    },

    async updateResourceLimits(id: string, limits: ResourceLimits): Promise<void> {
      await api.patch(`/api/v1/admin/instances/${id}/resource-limits`, limits);
      await this.fetchInstanceDetail(id);
    },

    async updateFeatureFlags(id: string, flags: FeatureFlags): Promise<void> {
      await api.patch(`/api/v1/admin/instances/${id}/feature-flags`, flags);
      await this.fetchInstanceDetail(id);
    },

    clearSelectedInstance() {
      store.setSelectedInstance(null);
      store.setLogs([]);
    },
  };
}
