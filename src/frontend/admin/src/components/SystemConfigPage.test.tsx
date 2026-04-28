import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { SystemConfigPage } from './SystemConfigPage';
import { useSystemConfig } from '../stores/system-config.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const CONFIG_PATH = '/api/v1/admin/system-config';

const sampleConfig = {
  paidServersDisabled: false,
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('SystemConfigPage', () => {
  beforeEach(() => {
    useSystemConfig().reset();
  });

  it('renders the page heading', () => {
    mockFetch({ [`GET ${CONFIG_PATH}`]: () => sampleConfig });
    const { getByText } = render(() => <SystemConfigPage />);
    expect(getByText('System Settings')).toBeInTheDocument();
  });

  it('shows loading state on first render', () => {
    mockFetch({ [`GET ${CONFIG_PATH}`]: () => sampleConfig });
    const { getByText } = render(() => <SystemConfigPage />);
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders the toggle once config is loaded', async () => {
    mockFetch({ [`GET ${CONFIG_PATH}`]: () => sampleConfig });
    const { findByTestId } = render(() => <SystemConfigPage />);
    const toggle = (await findByTestId('paid-servers-disabled-toggle')) as HTMLInputElement;
    expect(toggle.checked).toBe(false);
  });

  it('toggles paidServersDisabled on change', async () => {
    mockFetch({
      [`GET ${CONFIG_PATH}`]: () => sampleConfig,
      [`PUT ${CONFIG_PATH}`]: () => ({ ...sampleConfig, paidServersDisabled: true }),
    });
    const { findByTestId } = render(() => <SystemConfigPage />);
    const toggle = (await findByTestId('paid-servers-disabled-toggle')) as HTMLInputElement;
    fireEvent.change(toggle, { target: { checked: true } });
    await waitFor(() => expect(useSystemConfig().config?.paidServersDisabled).toBe(true));
  });

  it('renders updated-at timestamp text', async () => {
    mockFetch({ [`GET ${CONFIG_PATH}`]: () => sampleConfig });
    const { findByText } = render(() => <SystemConfigPage />);
    expect(await findByText(/Last updated:/)).toBeInTheDocument();
  });
});
