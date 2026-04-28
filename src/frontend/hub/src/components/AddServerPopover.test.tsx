import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import AddServerPopover from './AddServerPopover';
import { instanceStore } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('AddServerPopover', () => {
  beforeEach(() => {
    instanceStore.reset();
    localStorage.clear();
  });

  it('renders nothing when closed', () => {
    const { container } = render(() => (
      <AddServerPopover open={false} onClose={() => {}} />
    ));
    expect(container.querySelector('input')).toBeNull();
  });

  it('renders the search input when open', () => {
    const { getByPlaceholderText } = render(() => (
      <AddServerPopover open={true} onClose={() => {}} />
    ));
    expect(getByPlaceholderText('Enter server address or search...')).toBeInTheDocument();
  });

  it('shows "Searching..." after typing >=2 chars', async () => {
    mockFetch({
      'GET /api/v1/discover/instances': () => new Promise(() => {}),
    });
    const { container, findByText } = render(() => (
      <AddServerPopover open={true} onClose={() => {}} />
    ));
    const input = container.querySelector('input')!;
    fireEvent.input(input, { target: { value: 'al' } });
    expect(await findByText('Searching...')).toBeInTheDocument();
  });

  it('shows search results returned by the discover API', async () => {
    mockFetch({
      'GET /api/v1/discover/instances': () => [
        {
          id: '1',
          name: 'AlphaServer',
          description: '',
          url: 'https://alpha.example.com',
          memberCount: 42,
          isPublic: true,
        },
      ],
    });
    const { container, findByText } = render(() => (
      <AddServerPopover open={true} onClose={() => {}} />
    ));
    const input = container.querySelector('input')!;
    fireEvent.input(input, { target: { value: 'alpha' } });
    expect(await findByText('AlphaServer')).toBeInTheDocument();
  });

  it('adds an instance and calls onClose when Enter is pressed with a custom URL', async () => {
    const onClose = vi.fn();
    mockFetch({
      'GET /api/v1/discover/instances': () => [],
    });
    const { container, findByText } = render(() => (
      <AddServerPopover open={true} onClose={onClose} />
    ));
    const input = container.querySelector('input')!;
    fireEvent.input(input, { target: { value: 'my.example.com' } });
    // wait until the debounced search resolves with no results
    await findByText(/No servers found/);
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(instanceStore.connectedInstances()[0]?.url).toBe('https://my.example.com');
  });

  it('shows "Already added" when adding a duplicate', async () => {
    instanceStore.addInstance({ url: 'https://dup.example.com', name: 'Dup' });
    mockFetch({
      'GET /api/v1/discover/instances': () => [],
    });
    const { container, findByText } = render(() => (
      <AddServerPopover open={true} onClose={() => {}} />
    ));
    const input = container.querySelector('input')!;
    fireEvent.input(input, { target: { value: 'dup.example.com' } });
    await findByText(/No servers found/);
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(await findByText('Already added')).toBeInTheDocument();
  });
});
