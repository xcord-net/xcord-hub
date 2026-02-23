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

// Nested types not yet in generated spec — AdminGetInstanceResponse returns
// resourceLimits/featureFlags/health/infrastructure as untyped JSON.
// These stay local until the backend OpenAPI spec properly types those fields.
export interface ResourceLimits {
  maxMembers: number;
  maxServers: number;
  maxChannelsPerServer: number;
  maxFileUploadMb: number;
  maxStorageGb: number;
  maxMonthlyBandwidthGb: number;
}

export interface FeatureFlags {
  allowCustomEmoji: boolean;
  allowVoiceChannels: boolean;
  allowVideoStreaming: boolean;
  allowBots: boolean;
  allowWebhooks: boolean;
  allowAutomod: boolean;
  allowServerDiscovery: boolean;
}

export interface HealthStatus {
  isHealthy: boolean;
  lastCheckAt: string;
  cpu: number;
  memory: number;
  diskUsage: number;
  activeConnections: number;
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
