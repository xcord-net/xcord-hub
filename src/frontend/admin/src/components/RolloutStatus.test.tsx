import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { RolloutStatus } from './RolloutStatus';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const ROLLOUTS_PATH = '/api/v1/admin/upgrades';

const inProgressRollout = {
  id: 'r-1',
  toImage: 'fed:1.2.3',
  fromImage: 'fed:1.2.2',
  targetPool: null,
  status: 'InProgress',
  totalInstances: 10,
  completedInstances: 4,
  failedInstances: 0,
  batchSize: 2,
  maxFailures: 1,
  scheduledAt: null,
  startedAt: '2026-01-01T00:00:00Z',
  completedAt: null,
};

describe('RolloutStatus', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders nothing when there are no active rollouts', async () => {
    mockFetch({ [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }) });
    const { container } = render(() => <RolloutStatus />);
    await waitFor(() => expect(useInstances().activeRollouts.length).toBe(0));
    expect(container.querySelector('[data-testid="rollout-status-banner"]')).toBeNull();
  });

  it('renders the banner when there is an active rollout', async () => {
    mockFetch({ [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [inProgressRollout] }) });
    const { findByTestId } = render(() => <RolloutStatus />);
    expect(await findByTestId('rollout-status-banner')).toBeInTheDocument();
    expect(await findByTestId('rollout-item-r-1')).toBeInTheDocument();
  });

  it('renders the status badge text', async () => {
    mockFetch({ [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [inProgressRollout] }) });
    const { findByTestId } = render(() => <RolloutStatus />);
    const badge = await findByTestId('rollout-status-r-1');
    expect(badge.textContent).toContain('InProgress');
  });

  it('shows pause button for InProgress rollouts', async () => {
    mockFetch({ [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [inProgressRollout] }) });
    const { findByTestId } = render(() => <RolloutStatus />);
    expect(await findByTestId('rollout-pause-r-1')).toBeInTheDocument();
  });

  it('clicking pause calls pause endpoint', async () => {
    mockFetch({
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [inProgressRollout] }),
      'POST /api/v1/admin/upgrades/r-1/pause': () => ({ status: 200, body: {} }),
    });
    const { findByTestId } = render(() => <RolloutStatus />);
    const pauseBtn = await findByTestId('rollout-pause-r-1');
    fireEvent.click(pauseBtn);
    await waitFor(() => expect((globalThis.fetch as any).mock.calls.some(
      (c: any[]) => String(c[0]).includes('/upgrades/r-1/pause')
    )).toBe(true));
  });

  it('renders progress bar with correct width', async () => {
    mockFetch({ [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [inProgressRollout] }) });
    const { findByTestId } = render(() => <RolloutStatus />);
    const bar = await findByTestId('rollout-progress-r-1') as HTMLElement;
    expect(bar.style.width).toBe('40%');
  });
});
