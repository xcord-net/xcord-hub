import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { BackupHistory } from './BackupHistory';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const RECORDS_PATH = '/api/v1/admin/instances/inst-1/backups';
const TRIGGER_PATH = '/api/v1/admin/instances/inst-1/backups/trigger';

const sampleRecord = {
  id: 'b-1',
  kind: 'Full',
  status: 'Completed' as const,
  startedAt: '2026-01-01T00:00:00Z',
  sizeBytes: 1024,
};

describe('BackupHistory', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders heading and trigger button', async () => {
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => [],
    });
    const { getByText } = render(() => <BackupHistory instanceId="inst-1" />);
    expect(getByText('Backup History')).toBeInTheDocument();
    expect(getByText('Trigger Backup')).toBeInTheDocument();
  });

  it('shows loading message before records load', () => {
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => new Promise(() => {}),
    });
    const { getByText } = render(() => <BackupHistory instanceId="inst-1" />);
    expect(getByText('Loading backups...')).toBeInTheDocument();
  });

  it('renders empty placeholder when no backups exist', async () => {
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => [],
    });
    const { findByText } = render(() => <BackupHistory instanceId="inst-1" />);
    expect(await findByText('No backups found.')).toBeInTheDocument();
  });

  it('renders backup record rows', async () => {
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => [sampleRecord],
    });
    const { findByText, findAllByText } = render(() => <BackupHistory instanceId="inst-1" />);
    expect(await findByText('Completed')).toBeInTheDocument();
    // 'Full' appears twice — once in the kind <option>, once in the row's <td>
    const matches = await findAllByText('Full');
    expect(matches.length).toBeGreaterThan(1);
  });

  it('shows confirm dialog when Delete is clicked', async () => {
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => [sampleRecord],
    });
    const { findByText, getByText } = render(() => <BackupHistory instanceId="inst-1" />);
    const deleteBtn = await findByText('Delete');
    fireEvent.click(deleteBtn);
    expect(getByText('Confirm Action')).toBeInTheDocument();
    expect(getByText(/cannot be undone/i)).toBeInTheDocument();
  });

  it('triggers a new backup via POST when Trigger Backup is clicked', async () => {
    let triggered = false;
    mockFetch({
      [`GET ${RECORDS_PATH}`]: () => [],
      [`POST ${TRIGGER_PATH}`]: () => {
        triggered = true;
        return { status: 200, body: sampleRecord };
      },
    });
    const { findByText, getByText } = render(() => <BackupHistory instanceId="inst-1" />);
    await findByText('No backups found.');
    fireEvent.click(getByText('Trigger Backup'));
    await waitFor(() => expect(triggered).toBe(true));
  });
});
