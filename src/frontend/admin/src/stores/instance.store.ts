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
  AvailableVersion,
  UpgradeRollout,
  StartRolloutRequest,
} from '../types/instance';

export interface BackupPolicy {
  enabled: boolean;
  frequency: 'Hourly' | 'Daily' | 'Weekly';
  retentionDays: number;
  backupDatabase: boolean;
  backupFiles: boolean;
  backupRedis: boolean;
}

export interface BackupRecord {
  id: string;
  managedInstanceId: string;
  status: 'InProgress' | 'Completed' | 'Failed';
  kind: 'Database' | 'Files' | 'Redis' | 'Full';
  sizeBytes: number;
  storagePath: string;
  errorMessage?: string;
  startedAt: string;
  completedAt?: string;
}

const store = createRoot(() => {
  const [instances, setInstances] = createSignal<InstanceListItem[]>([]);
  const [selectedInstance, setSelectedInstance] = createSignal<InstanceDetail | null>(null);
  const [logs, setLogs] = createSignal<LogEntry[]>([]);
  const [total, setTotal] = createSignal(0);
  const [page, setPage] = createSignal(1);
  const [pageSize] = createSignal(20);
  const [statusFilter, setStatusFilter] = createSignal<InstanceStatus | null>(null);
  const [isLoading, setIsLoading] = createSignal(false);
  const [availableVersions, setAvailableVersions] = createSignal<AvailableVersion[]>([]);
  const [activeRollouts, setActiveRollouts] = createSignal<UpgradeRollout[]>([]);

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
    availableVersions,
    setAvailableVersions,
    activeRollouts,
    setActiveRollouts,
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
    get availableVersions() { return store.availableVersions(); },
    get activeRollouts() { return store.activeRollouts(); },

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

    async fetchBackupPolicy(id: string): Promise<BackupPolicy> {
      return await api.get<BackupPolicy>(`/api/v1/admin/instances/${id}/backup-policy`);
    },

    async updateBackupPolicy(id: string, policy: BackupPolicy): Promise<void> {
      await api.put(`/api/v1/admin/instances/${id}/backup-policy`, policy);
    },

    async fetchBackupRecords(id: string, page = 1, pageSize = 20): Promise<BackupRecord[]> {
      return await api.get<BackupRecord[]>(
        `/api/v1/admin/instances/${id}/backups?page=${page}&pageSize=${pageSize}`
      );
    },

    async triggerBackup(id: string, kind: string): Promise<BackupRecord> {
      return await api.post<BackupRecord>(`/api/v1/admin/instances/${id}/backups/trigger`, { kind });
    },

    async triggerRestore(id: string, backupId: string): Promise<void> {
      await api.post(`/api/v1/admin/instances/${id}/backups/${backupId}/restore`);
    },

    async deleteBackup(id: string, backupId: string): Promise<void> {
      await api.delete(`/api/v1/admin/instances/${id}/backups/${backupId}`);
    },

    async fetchVersions(): Promise<void> {
      const response = await api.get<{ versions: AvailableVersion[] }>('/api/v1/admin/versions');
      store.setAvailableVersions(response.versions);
    },

    async upgradeInstance(instanceId: string, targetImage: string): Promise<void> {
      await api.post(`/api/v1/hub/instances/${instanceId}/upgrade`, { targetImage });
    },

    async startRollout(request: StartRolloutRequest): Promise<UpgradeRollout> {
      return await api.post<UpgradeRollout>('/api/v1/admin/upgrades', request);
    },

    async pauseRollout(rolloutId: string): Promise<void> {
      await api.post(`/api/v1/admin/upgrades/${rolloutId}/pause`);
    },

    async resumeRollout(rolloutId: string): Promise<void> {
      await api.post(`/api/v1/admin/upgrades/${rolloutId}/resume`);
    },

    async cancelRollout(rolloutId: string): Promise<void> {
      await api.post(`/api/v1/admin/upgrades/${rolloutId}/cancel`);
    },

    async fetchActiveRollouts(): Promise<void> {
      const response = await api.get<{ rollouts: UpgradeRollout[] }>('/api/v1/admin/upgrades');
      store.setActiveRollouts(response.rollouts.filter((r: UpgradeRollout) =>
        r.status === 'InProgress' || r.status === 'Paused' || r.status === 'Pending'));
    },

    async updateBatchUpgrades(instanceId: string, enabled: boolean): Promise<void> {
      await api.patch(`/api/v1/hub/instances/${instanceId}/batch-upgrades`, { enabled });
    },

    clearSelectedInstance() {
      store.setSelectedInstance(null);
      store.setLogs([]);
    },

    reset(): void {
      store.setInstances([]);
      store.setSelectedInstance(null);
      store.setLogs([]);
      store.setTotal(0);
      store.setPage(1);
      store.setStatusFilter(null);
      store.setIsLoading(false);
      store.setAvailableVersions([]);
      store.setActiveRollouts([]);
    },
  };
}
