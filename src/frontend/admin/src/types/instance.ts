export enum InstanceStatus {
  Provisioning = 'Provisioning',
  Running = 'Running',
  Suspended = 'Suspended',
  Destroyed = 'Destroyed',
  Failed = 'Failed',
}

export enum BillingTier {
  Free = 'Free',
  Basic = 'Basic',
  Pro = 'Pro',
}

export interface InstanceListItem {
  id: string;
  subdomain: string;
  displayName: string;
  status: InstanceStatus;
  tier: BillingTier;
  createdAt: string;
  ownerUsername: string;
}

export interface InstanceDetail {
  id: string;
  subdomain: string;
  displayName: string;
  domain: string;
  status: InstanceStatus;
  tier: BillingTier;
  createdAt: string;
  suspendedAt?: string;
  destroyedAt?: string;
  ownerId: string;
  ownerUsername: string;
  resourceLimits: ResourceLimits;
  featureFlags: FeatureFlags;
  health?: HealthStatus;
  infrastructure?: Infrastructure;
}

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

export interface ProvisionInstanceRequest {
  ownerId: number;
  domain: string;
  displayName: string;
  adminPassword: string;
}

export interface InstanceListResponse {
  instances: InstanceListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  source: string;
}
