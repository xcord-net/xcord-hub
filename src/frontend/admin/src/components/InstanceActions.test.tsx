import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { InstanceActions } from './InstanceActions';
import { useInstances } from '../stores/instance.store';
import { InstanceStatus } from '../types/instance';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('InstanceActions', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders Suspend and Destroy when status is Running', () => {
    const { getByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Running} />
    ));
    expect(getByText('Suspend Instance')).toBeInTheDocument();
    expect(getByText('Destroy Instance')).toBeInTheDocument();
  });

  it('renders Resume button when status is Suspended', () => {
    const { getByText, queryByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Suspended} />
    ));
    expect(getByText('Resume Instance')).toBeInTheDocument();
    expect(queryByText('Suspend Instance')).toBeNull();
  });

  it('hides Destroy button when status is Destroyed', () => {
    const { queryByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Destroyed} />
    ));
    expect(queryByText('Destroy Instance')).toBeNull();
  });

  it('shows confirmation modal after clicking Suspend', () => {
    const { getByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Running} />
    ));
    fireEvent.click(getByText('Suspend Instance'));
    expect(getByText('Confirm Action')).toBeInTheDocument();
    expect(getByText('Confirm')).toBeInTheDocument();
  });

  it('cancel button closes the confirmation modal', () => {
    const { getByText, queryByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Running} />
    ));
    fireEvent.click(getByText('Suspend Instance'));
    fireEvent.click(getByText('Cancel'));
    expect(queryByText('Confirm Action')).toBeNull();
  });

  it('confirm calls suspend endpoint and closes modal', async () => {
    mockFetch({
      'POST /api/v1/admin/instances/inst-1/suspend': () => ({ status: 200, body: {} }),
      'GET /api/v1/admin/instances/inst-1': () => ({ status: 200, body: {} }),
    });
    const { getByText, queryByText } = render(() => (
      <InstanceActions instanceId="inst-1" status={InstanceStatus.Running} />
    ));
    fireEvent.click(getByText('Suspend Instance'));
    fireEvent.click(getByText('Confirm'));
    await waitFor(() => expect(queryByText('Confirm Action')).toBeNull());
  });
});
