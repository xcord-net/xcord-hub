import type { components } from '@generated/api-types';

// Re-exported from generated OpenAPI types
export type InstanceListItem = components['schemas']['AdminInstanceListItem'];
export type InstanceListResponse = components['schemas']['AdminListInstancesResponse'];
export type ProvisionInstanceRequest = components['schemas']['ProvisionInstanceCommand'];

// Enum as const object (openapi-typescript generates strings, not TS enums)
export const InstanceStatus = {
  Provisioning: 'Provisioning',
  Running: 'Running',
  Suspended: 'Suspended',
  Destroyed: 'Destroyed',
  Failed: 'Failed',
} as const;
export type InstanceStatus = (typeof InstanceStatus)[keyof typeof InstanceStatus];

// Nested types not yet in generated spec - AdminGetInstanceResponse returns
// resourceLimits/featureFlags/health/infrastructure as untyped JSON.
// These stay local until the backend OpenAPI spec properly types those fields.
/**
 * Mirrors the fields UpdateResourceLimitsCommand accepts, which is the subset of
 * XcordHub.Entities.ResourceLimits an operator can edit.
 *
 * As with FeatureFlags, the console previously invented its own names
 * (maxMembers, maxStorageGb, maxChannelsPerServer, ...). None of them bound, so
 * the editor rendered blanks and every save wrote zeros over the real limits.
 */
export interface ResourceLimits {
  maxUsers: number;
  maxServers: number;
  maxStorageMb: number;
  maxCpuPercent: number;
  maxMemoryMb: number;
  maxRateLimit: number;
  maxVoiceConcurrency: number;
  maxVideoConcurrency: number;
}

/**
 * Mirrors XcordHub.Entities.FeatureFlags, which is what
 * PATCH /api/v1/admin/instances/{id}/feature-flags binds and what provisioning
 * reads when it configures an instance container.
 *
 * These names are not cosmetic. The console previously declared a different set
 * entirely (allowCustomEmoji, allowBots, ...); none of those names bound to the
 * command, so every save wrote five defaulted `false` flags and quietly stripped
 * voice, video, simulcast, member tiers, and broadcast from the instance.
 */
export interface FeatureFlags {
  canUseVoiceChannels: boolean;
  canUseVideoChannels: boolean;
  canUseSimulcast: boolean;
  canUseMemberTiers: boolean;
  canBroadcast: boolean;
}

export interface HealthStatus {
  isHealthy: boolean;
  lastCheckAt: string;
  cpu: number;
  memory: number;
  diskUsage: number;
  activeConnections: number;
  version?: string;
  errors?: string[];
}

export interface Infrastructure {
  containerHost: string;
  containerName: string;
  databaseHost: string;
  databaseName: string;
  redisHost: string;
  minioEndpoint: string;
  minioBucket: string;
  deployedImage?: string;
}

export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  source: string;
}

// Augmented version of the generated type with properly typed nested fields
export type InstanceDetail = Omit<
  components['schemas']['AdminGetInstanceResponse'],
  'resourceLimits' | 'featureFlags' | 'health' | 'infrastructure'
> & {
  resourceLimits: ResourceLimits;
  featureFlags: FeatureFlags;
  health?: HealthStatus;
  infrastructure?: Infrastructure;
};

export interface AvailableVersion {
  id: string;
  version: string;
  image: string;
  releaseNotes: string | null;
  isMinimumVersion: boolean;
  minimumEnforcementDate: string | null;
  publishedAt: string;
}

export interface ReleaseNotes {
  version: string;
  features: { summary: string; commit: string }[];
  fixes: { summary: string; commit: string }[];
  other: { summary: string; commit: string; type: string }[];
  breakingChanges: string[];
  migrationNotes: string;
  knownIssues: string;
}

export interface UpgradeRollout {
  id: string;
  toImage: string;
  fromImage: string | null;
  targetPool: string | null;
  status: string;
  totalInstances: number;
  completedInstances: number;
  failedInstances: number;
  batchSize: number;
  maxFailures: number;
  scheduledAt: string | null;
  startedAt: string;
  completedAt: string | null;
}

export interface StartRolloutRequest {
  toImage: string;
  fromImage?: string;
  targetPool?: string;
  force?: boolean;
  batchSize?: number;
  maxFailures?: number;
  scheduledAt?: string;
}
