import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { FeatureFlagsEditor } from './FeatureFlagsEditor';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';
import type { FeatureFlags } from '../types/instance';

const flags: FeatureFlags = {
  canUseVoiceChannels: true,
  canUseVideoChannels: false,
  canUseSimulcast: false,
  canUseMemberTiers: true,
  canBroadcast: false,
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
    expect(getByText('Voice Channels')).toBeInTheDocument();
    expect(getByText('Member Tiers')).toBeInTheDocument();
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

  it('sends the flag names the hub API actually binds', async () => {
    // Regression: the editor used to declare its own vocabulary
    // (allowCustomEmoji, allowBots, ...). None of those names bound to
    // UpdateFeatureFlagsCommand, so every save wrote five defaulted `false`
    // flags and silently stripped voice, video, simulcast, member tiers, and
    // broadcast from the instance. Toggling here must reach the API as the
    // canUse*/canBroadcast names the command declares.
    let sent: Record<string, unknown> | undefined;
    mockFetch({
      'PATCH /api/v1/admin/instances/inst-1/feature-flags': ({ body }) => {
        sent = body as Record<string, unknown>;
        return { status: 200, body: {} };
      },
      'GET /api/v1/admin/instances/inst-1': () => ({ status: 200, body: {} }),
    });

    const { getByText, getByTestId } = render(() => (
      <FeatureFlagsEditor instanceId="inst-1" initialFlags={flags} />
    ));
    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByTestId('feature-flag-canBroadcast'));
    fireEvent.click(getByText('Save Changes'));

    await waitFor(() => expect(sent).toBeDefined());
    expect(Object.keys(sent!).sort()).toEqual([
      'canBroadcast',
      'canUseMemberTiers',
      'canUseSimulcast',
      'canUseVideoChannels',
      'canUseVoiceChannels',
    ]);
    // Untouched flags keep their value; the toggled one flips.
    expect(sent!.canUseVoiceChannels).toBe(true);
    expect(sent!.canUseMemberTiers).toBe(true);
    expect(sent!.canBroadcast).toBe(true);
  });

  it('reads the PascalCase flags the hub API returns', () => {
    // FeatureFlagsJson is stored with JsonSerializer defaults and handed back
    // verbatim, so the payload keys are PascalCase. Rendering them as all-off
    // would misreport the instance to the operator.
    const { getByTestId } = render(() => (
      <FeatureFlagsEditor
        instanceId="inst-1"
        initialFlags={{ CanUseVoiceChannels: true, CanUseMemberTiers: true } as never}
      />
    ));
    expect(getByTestId('feature-flag-canUseVoiceChannels')).toHaveAttribute('data-enabled', 'true');
    expect(getByTestId('feature-flag-canUseMemberTiers')).toHaveAttribute('data-enabled', 'true');
    expect(getByTestId('feature-flag-canBroadcast')).toHaveAttribute('data-enabled', 'false');
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
