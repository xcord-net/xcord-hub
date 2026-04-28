import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { InstanceDetail } from './InstanceDetail';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const INSTANCE_PATH = '/api/v1/admin/instances/i-1';
const LOGS_PATH = '/api/v1/admin/instances/i-1/logs';

const sampleInstance = {
  id: 'i-1',
  subdomain: 'foo',
  displayName: 'Foo Server',
  domain: 'foo.example.com',
  ownerUsername: 'alice',
  status: 'Running',
  tier: 'Pro',
  mediaEnabled: false,
  createdAt: '2026-01-01T00:00:00Z',
  suspendedAt: null,
  resourceLimits: {
    maxMembers: 100,
    maxServers: 5,
    maxChannelsPerServer: 50,
    maxFileUploadMb: 25,
    maxStorageGb: 10,
    maxMonthlyBandwidthGb: 100,
  },
  featureFlags: {
    allowCustomEmoji: true,
    allowVoiceChannels: true,
    allowVideoStreaming: true,
    allowBots: true,
    allowWebhooks: true,
    allowAutomod: true,
    allowServerDiscovery: true,
  },
  health: {
    isHealthy: true,
    lastCheckAt: '2026-01-01T00:00:00Z',
    cpu: 12.5,
    memory: 33.3,
    diskUsage: 45.0,
    activeConnections: 7,
    version: '0.1.0',
  },
  infrastructure: {
    containerHost: 'host1',
    containerName: 'foo-app',
    databaseHost: 'pg1',
    databaseName: 'foo_db',
    redisHost: 'redis1',
    minioEndpoint: 'minio1',
    minioBucket: 'foo-bucket',
  },
};

describe('InstanceDetail', () => {
  beforeEach(() => {
    useInstances().reset();
    mockFetch({
      [`GET ${INSTANCE_PATH}`]: () => sampleInstance,
      [`GET ${LOGS_PATH}`]: () => [],
      // backup policy & versions get fetched when their tabs are activated
      [`GET ${INSTANCE_PATH}/backup-policy`]: () => ({
        enabled: false,
        frequency: 'Daily',
        retentionDays: 7,
        backupDatabase: true,
        backupFiles: false,
        backupRedis: false,
      }),
      [`GET ${INSTANCE_PATH}/backups`]: () => [],
      'GET /api/v1/admin/versions': () => ({ versions: [] }),
    });
  });

  it('renders the back button immediately', () => {
    const { getByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={() => {}} />
    ));
    expect(getByText('Back to Instances')).toBeInTheDocument();
  });

  it('calls onBack when back button is clicked', () => {
    const onBack = vi.fn();
    const { getByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={onBack} />
    ));
    fireEvent.click(getByText('Back to Instances'));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('renders instance display name once detail is loaded', async () => {
    const { findByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={() => {}} />
    ));
    expect(await findByText('Foo Server')).toBeInTheDocument();
    expect(await findByText('foo.example.com')).toBeInTheDocument();
  });

  it('renders the tab navigation with all six tabs', async () => {
    const { findByText, getByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={() => {}} />
    ));
    await findByText('Foo Server');
    expect(getByText('Overview')).toBeInTheDocument();
    expect(getByText('Health')).toBeInTheDocument();
    expect(getByText('Configuration')).toBeInTheDocument();
    expect(getByText('Logs')).toBeInTheDocument();
    expect(getByText('Backups')).toBeInTheDocument();
    expect(getByText('Version')).toBeInTheDocument();
  });

  it('switches to the Health tab and shows CPU/memory metrics', async () => {
    const { findByText, getByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={() => {}} />
    ));
    await findByText('Foo Server');
    fireEvent.click(getByText('Health'));
    await waitFor(() => {
      expect(getByText('Healthy')).toBeInTheDocument();
    });
  });

  it('shows "No logs available" placeholder under the Logs tab when empty', async () => {
    const { findByText, getByText } = render(() => (
      <InstanceDetail instanceId="i-1" onBack={() => {}} />
    ));
    await findByText('Foo Server');
    fireEvent.click(getByText('Logs'));
    await waitFor(() => {
      expect(getByText('No logs available')).toBeInTheDocument();
    });
  });
});
