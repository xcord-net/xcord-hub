import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { FleetUpgrade } from './FleetUpgrade';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const VERSIONS_PATH = '/api/v1/admin/versions';
const ROLLOUTS_PATH = '/api/v1/admin/upgrades';

const sampleVersion = {
  id: 'v-1',
  version: '1.2.3',
  image: 'docker.xcord.net/fed:1.2.3',
  releaseNotes: null,
  isMinimumVersion: false,
  minimumEnforcementDate: null,
  publishedAt: '2026-01-01T00:00:00Z',
};

describe('FleetUpgrade', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders nothing when isOpen is false', () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [] }),
    });
    const { container } = render(() => (
      <FleetUpgrade isOpen={false} onClose={() => {}} />
    ));
    expect(container.querySelector('[data-testid="fleet-upgrade-dialog"]')).toBeNull();
  });

  it('renders the dialog with heading when open', () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [] }),
    });
    const { getByText } = render(() => (
      <FleetUpgrade isOpen={true} onClose={() => {}} />
    ));
    expect(getByText('Start Fleet Upgrade')).toBeInTheDocument();
    expect(getByText('Cancel')).toBeInTheDocument();
  });

  it('renders the version dropdown after versions load', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [sampleVersion] }),
    });
    const { findByText } = render(() => (
      <FleetUpgrade isOpen={true} onClose={() => {}} />
    ));
    expect(await findByText(/1\.2\.3 - docker\.xcord\.net\/fed:1\.2\.3/)).toBeInTheDocument();
  });

  it('calls onClose when Cancel is clicked', () => {
    const onClose = vi.fn();
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [] }),
    });
    const { getByText } = render(() => (
      <FleetUpgrade isOpen={true} onClose={onClose} />
    ));
    fireEvent.click(getByText('Cancel'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('disables Start Rollout button when no target image is set', () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [] }),
    });
    const { getByTestId } = render(() => (
      <FleetUpgrade isOpen={true} onClose={() => {}} />
    ));
    const submit = getByTestId('fleet-upgrade-submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
  });

  it('submits a rollout and calls onClose on success', async () => {
    const onClose = vi.fn();
    let posted = false;
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [sampleVersion] }),
      [`POST ${ROLLOUTS_PATH}`]: () => {
        posted = true;
        return {
          status: 200,
          body: {
            id: 'r-1',
            toImage: sampleVersion.image,
            fromImage: null,
            targetPool: null,
            status: 'Pending',
            totalInstances: 1,
            completedInstances: 0,
            failedInstances: 0,
            batchSize: 5,
            maxFailures: 1,
            scheduledAt: null,
            startedAt: '2026-01-01T00:00:00Z',
            completedAt: null,
          },
        };
      },
      [`GET ${ROLLOUTS_PATH}`]: () => ({ rollouts: [] }),
    });
    const { findByTestId } = render(() => (
      <FleetUpgrade isOpen={true} onClose={onClose} />
    ));
    const submit = (await findByTestId('fleet-upgrade-submit')) as HTMLButtonElement;
    await waitFor(() => expect(submit.disabled).toBe(false));
    fireEvent.click(submit);
    await waitFor(() => expect(posted).toBe(true));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });
});
