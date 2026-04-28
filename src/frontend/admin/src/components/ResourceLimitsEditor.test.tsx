import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { ResourceLimitsEditor } from './ResourceLimitsEditor';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';
import type { ResourceLimits } from '../types/instance';

const limits: ResourceLimits = {
  maxMembers: 100,
  maxServers: 5,
  maxChannelsPerServer: 50,
  maxFileUploadMb: 25,
  maxStorageGb: 10,
  maxMonthlyBandwidthGb: 100,
};

describe('ResourceLimitsEditor', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders heading and labels', () => {
    const { getByText } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    expect(getByText('Resource Limits')).toBeInTheDocument();
    expect(getByText('Max Members')).toBeInTheDocument();
  });

  it('inputs are disabled until Edit is clicked', () => {
    const { container } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    const inputs = container.querySelectorAll('input');
    inputs.forEach((i) => expect((i as HTMLInputElement).disabled).toBe(true));
  });

  it('clicking Edit enables inputs and shows Save/Cancel', () => {
    const { container, getByText } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    fireEvent.click(getByText('Edit'));
    const firstInput = container.querySelector('input') as HTMLInputElement;
    expect(firstInput.disabled).toBe(false);
    expect(getByText('Save Changes')).toBeInTheDocument();
  });

  it('Cancel returns view mode', () => {
    const { getByText, queryByText } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Cancel'));
    expect(queryByText('Save Changes')).toBeNull();
  });

  it('Save calls API and exits edit mode on success', async () => {
    mockFetch({
      'PATCH /api/v1/admin/instances/inst-1/resource-limits': () => ({ status: 200, body: {} }),
      'GET /api/v1/admin/instances/inst-1': () => ({ status: 200, body: {} }),
    });
    const { getByText, queryByText } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Save Changes'));
    await waitFor(() => expect(queryByText('Save Changes')).toBeNull());
  });
});
