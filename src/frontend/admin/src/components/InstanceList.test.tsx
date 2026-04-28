import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { InstanceList } from './InstanceList';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const INSTANCES_PATH = '/api/v1/admin/instances';
const ROLLOUTS_PATH = '/api/v1/admin/upgrades';

const sampleInstance = {
  id: 'i-1',
  subdomain: 'foo',
  displayName: 'Foo Server',
  ownerUsername: 'alice',
  status: 'Running',
  tier: 'Pro',
  mediaEnabled: false,
  createdAt: '2026-01-01T00:00:00Z',
};

describe('InstanceList', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders the page heading and primary buttons', () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [], total: 0 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const { getByText } = render(() => (
      <InstanceList onSelectInstance={() => {}} onProvisionNew={() => {}} />
    ));
    expect(getByText('Instances')).toBeInTheDocument();
    expect(getByText('Provision New Instance')).toBeInTheDocument();
  });

  it('shows loading initially', () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [], total: 0 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const { getByText } = render(() => (
      <InstanceList onSelectInstance={() => {}} onProvisionNew={() => {}} />
    ));
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders empty placeholder when no instances', async () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [], total: 0 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const { findByText } = render(() => (
      <InstanceList onSelectInstance={() => {}} onProvisionNew={() => {}} />
    ));
    expect(await findByText('No instances found')).toBeInTheDocument();
  });

  it('renders instance rows when data is loaded', async () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [sampleInstance], total: 1 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const { findByText } = render(() => (
      <InstanceList onSelectInstance={() => {}} onProvisionNew={() => {}} />
    ));
    expect(await findByText('foo')).toBeInTheDocument();
    expect(await findByText('Foo Server')).toBeInTheDocument();
  });

  it('clicking a row triggers onSelectInstance with id', async () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [sampleInstance], total: 1 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const onSelect = vi.fn();
    const { findByText } = render(() => (
      <InstanceList onSelectInstance={onSelect} onProvisionNew={() => {}} />
    ));
    const row = await findByText('foo');
    fireEvent.click(row);
    expect(onSelect).toHaveBeenCalledWith('i-1');
  });

  it('Provision New button invokes onProvisionNew', async () => {
    mockFetch({
      [`GET ${INSTANCES_PATH}`]: () => ({ instances: [], total: 0 }),
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const onProvision = vi.fn();
    const { getByText } = render(() => (
      <InstanceList onSelectInstance={() => {}} onProvisionNew={onProvision} />
    ));
    fireEvent.click(getByText('Provision New Instance'));
    await waitFor(() => expect(onProvision).toHaveBeenCalled());
  });
});
