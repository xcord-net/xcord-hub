import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { BackupPolicyEditor } from './BackupPolicyEditor';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const POLICY_PATH = '/api/v1/admin/instances/inst-1/backup-policy';

const samplePolicy = {
  enabled: true,
  frequency: 'Daily' as const,
  retentionDays: 7,
  backupDatabase: true,
  backupFiles: false,
  backupRedis: false,
};

describe('BackupPolicyEditor', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('shows loading state then renders policy fields', async () => {
    mockFetch({
      [`GET ${POLICY_PATH}`]: () => samplePolicy,
    });
    const { getByText, findByText } = render(() => <BackupPolicyEditor instanceId="inst-1" />);
    expect(getByText('Loading policy...')).toBeInTheDocument();
    expect(await findByText('Save Policy')).toBeInTheDocument();
  });

  it('renders error placeholder when fetch fails', async () => {
    mockFetch({
      [`GET ${POLICY_PATH}`]: () => ({ status: 500, body: { message: 'boom' } }),
    });
    const { findByText } = render(() => <BackupPolicyEditor instanceId="inst-1" />);
    expect(await findByText('Could not load backup policy.')).toBeInTheDocument();
  });

  it('toggles enabled flag and disables frequency when off', async () => {
    mockFetch({
      [`GET ${POLICY_PATH}`]: () => samplePolicy,
    });
    const { container, findByText } = render(() => <BackupPolicyEditor instanceId="inst-1" />);
    await findByText('Save Policy');
    const toggle = container.querySelector('button[class*="rounded-full"]') as HTMLButtonElement;
    fireEvent.click(toggle);
    const select = container.querySelector('select') as HTMLSelectElement;
    await waitFor(() => expect(select.disabled).toBe(true));
  });

  it('saves policy and shows success message', async () => {
    mockFetch({
      [`GET ${POLICY_PATH}`]: () => samplePolicy,
      [`PUT ${POLICY_PATH}`]: () => ({ status: 200, body: {} }),
    });
    const { getByText, findByText } = render(() => <BackupPolicyEditor instanceId="inst-1" />);
    await findByText('Save Policy');
    fireEvent.click(getByText('Save Policy'));
    expect(await findByText('Policy saved.')).toBeInTheDocument();
  });

  it('shows error when save fails', async () => {
    mockFetch({
      [`GET ${POLICY_PATH}`]: () => samplePolicy,
      [`PUT ${POLICY_PATH}`]: () => ({ status: 500, body: { message: 'fail' } }),
    });
    const { getByText, findByText } = render(() => <BackupPolicyEditor instanceId="inst-1" />);
    await findByText('Save Policy');
    fireEvent.click(getByText('Save Policy'));
    expect(await findByText('Failed to save policy.')).toBeInTheDocument();
  });
});
