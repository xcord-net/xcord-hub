import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { VersionTab } from './VersionTab';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const VERSIONS_PATH = '/api/v1/admin/versions';
const UPGRADE_PATH = '/api/v1/hub/instances/i-1/upgrade';

const v100 = {
  id: 'v-100',
  version: '1.0.0',
  image: 'docker.xcord.net/fed:1.0.0',
  releaseNotes: null,
  isMinimumVersion: false,
  minimumEnforcementDate: null,
  publishedAt: '2026-01-01T00:00:00Z',
};
const v110 = {
  id: 'v-110',
  version: '1.1.0',
  image: 'docker.xcord.net/fed:1.1.0',
  releaseNotes: JSON.stringify({
    version: '1.1.0',
    features: [{ summary: 'Added widgets', commit: 'abcdef1234' }],
    fixes: [],
    other: [],
    breakingChanges: [],
    migrationNotes: '',
    knownIssues: '',
  }),
  isMinimumVersion: false,
  minimumEnforcementDate: null,
  publishedAt: '2026-02-01T00:00:00Z',
};

describe('VersionTab', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('shows loading text initially', () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => new Promise(() => {}),
    });
    const { getByText } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion="1.0.0"
        currentImage={v100.image}
        instanceStatus="Running"
      />
    ));
    expect(getByText('Loading versions...')).toBeInTheDocument();
  });

  it('shows "No versions available" when empty', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [] }),
    });
    const { findByText } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion={undefined}
        currentImage={undefined}
        instanceStatus="Running"
      />
    ));
    expect(await findByText('No versions available')).toBeInTheDocument();
  });

  it('renders the version list and selects the current version', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [v100, v110] }),
    });
    const { findByText, getByText } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion="1.0.0"
        currentImage={v100.image}
        instanceStatus="Running"
      />
    ));
    expect(await findByText('Version 1.0.0')).toBeInTheDocument();
    expect(getByText('current')).toBeInTheDocument();
  });

  it('disables the Update button while on the current version', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [v100] }),
    });
    const { findByTestId } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion="1.0.0"
        currentImage={v100.image}
        instanceStatus="Running"
      />
    ));
    const btn = (await findByTestId('update-button')) as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(btn.textContent).toContain('Current Version');
  });

  it('selects another version when clicked and enables the Update button', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [v100, v110] }),
    });
    const { findByTestId } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion="1.0.0"
        currentImage={v100.image}
        instanceStatus="Running"
      />
    ));
    const item = await findByTestId('version-item-1.1.0');
    fireEvent.click(item);
    const btn = (await findByTestId('update-button')) as HTMLButtonElement;
    await waitFor(() => expect(btn.disabled).toBe(false));
    expect(btn.textContent).toContain('Update to This Version');
  });

  it('shows success message after upgrade succeeds', async () => {
    mockFetch({
      [`GET ${VERSIONS_PATH}`]: () => ({ versions: [v100, v110] }),
      [`POST ${UPGRADE_PATH}`]: () => ({ status: 200, body: {} }),
    });
    const { findByTestId, findByText } = render(() => (
      <VersionTab
        instanceId="i-1"
        currentVersion="1.0.0"
        currentImage={v100.image}
        instanceStatus="Running"
      />
    ));
    const item = await findByTestId('version-item-1.1.0');
    fireEvent.click(item);
    const btn = await findByTestId('update-button');
    fireEvent.click(btn);
    expect(await findByText('Upgrade initiated.')).toBeInTheDocument();
  });
});
