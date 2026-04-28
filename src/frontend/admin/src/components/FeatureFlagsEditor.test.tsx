import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { FeatureFlagsEditor } from './FeatureFlagsEditor';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';
import type { FeatureFlags } from '../types/instance';

const flags: FeatureFlags = {
  allowCustomEmoji: true,
  allowVoiceChannels: false,
  allowVideoStreaming: false,
  allowBots: true,
  allowWebhooks: true,
  allowAutomod: false,
  allowServerDiscovery: false,
};

describe('FeatureFlagsEditor', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders the heading and feature labels', () => {
    const { getByText } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    expect(getByText('Feature Flags')).toBeInTheDocument();
    expect(getByText('Custom Emoji')).toBeInTheDocument();
    expect(getByText('Bots')).toBeInTheDocument();
  });

  it('shows the Edit button by default and no Save button', () => {
    const { getByText, queryByText } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    expect(getByText('Edit')).toBeInTheDocument();
    expect(queryByText('Save Changes')).toBeNull();
  });

  it('shows Save / Cancel buttons after clicking Edit', () => {
    const { getByText } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    fireEvent.click(getByText('Edit'));
    expect(getByText('Save Changes')).toBeInTheDocument();
    expect(getByText('Cancel')).toBeInTheDocument();
  });

  it('cancel returns the editor to view mode', () => {
    const { getByText, queryByText } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Cancel'));
    expect(queryByText('Save Changes')).toBeNull();
    expect(getByText('Edit')).toBeInTheDocument();
  });

  it('saves and exits edit mode on success', async () => {
    mockFetch({
      'PATCH /api/v1/admin/instances/inst-1/feature-flags': () => ({ status: 200, body: {} }),
      'GET /api/v1/admin/instances/inst-1': () => ({ status: 200, body: {} }),
    });
    const { getByText, queryByText } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Save Changes'));
    await waitFor(() => expect(queryByText('Save Changes')).toBeNull());
  });
});
