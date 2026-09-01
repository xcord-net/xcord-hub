import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { ResourceLimitsEditor } from './ResourceLimitsEditor';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';
import type { ResourceLimits } from '../types/instance';

const limits: ResourceLimits = {
  maxUsers: 100,
  maxServers: 5,
  maxStorageMb: 10_240,
  maxCpuPercent: 50,
  maxMemoryMb: 2048,
  maxRateLimit: 100,
  maxVoiceConcurrency: 10,
  maxVideoConcurrency: 5,
};

describe('ResourceLimitsEditor', () => {
  it('sends the limit names the hub API actually binds', async () => {
    // Regression: the editor used to declare maxMembers / maxChannelsPerServer /
    // maxFileUploadMb / maxStorageGb / maxMonthlyBandwidthGb. None of those bound
    // to UpdateResourceLimitsCommand, so the fields rendered blank and every save
    // wrote zeros over the instance's real limits.
    let sent: Record<string, unknown> | undefined;
    mockFetch({
      'PATCH /api/v1/admin/instances/inst-1/resource-limits': ({ body }) => {
        sent = body as Record<string, unknown>;
        return { status: 200, body: {} };
      },
      'GET /api/v1/admin/instances/inst-1': () => ({ status: 200, body: {} }),
    });

    const { getByText, getByTestId } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.input(getByTestId('resource-limit-maxServers'), { target: { value: '9' } });
    fireEvent.click(getByText('Save Changes'));

    await waitFor(() => expect(sent).toBeDefined());
    expect(sent!.maxServers).toBe(9);
    expect(sent!.maxUsers).toBe(100);
    expect(sent!.maxStorageMb).toBe(10_240);
    expect(sent).not.toHaveProperty('maxMembers');
  });

  it('reads the PascalCase limits the hub API returns', () => {
    const { getByTestId } = render(() => (
      <ResourceLimitsEditor
        instanceId="inst-1"
        initialLimits={{ MaxUsers: 42, MaxServers: 3 } as never}
      />
    ));
    expect(getByTestId('resource-limit-maxUsers')).toHaveValue(42);
    expect(getByTestId('resource-limit-maxServers')).toHaveValue(3);
  });

  beforeEach(() => {
    useInstances().reset();
  });

  it('renders heading and labels', () => {
    const { getByText } = render(() => (
      <ResourceLimitsEditor instanceId="inst-1" initialLimits={limits} />
    ));
    expect(getByText('Resource Limits')).toBeInTheDocument();
    expect(getByText('Max Users')).toBeInTheDocument();
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
