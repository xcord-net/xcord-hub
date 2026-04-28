import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { ProvisionForm } from './ProvisionForm';
import { useInstances } from '../stores/instance.store';
import { mockFetch } from '../tests/helpers/mockFetch';

describe('ProvisionForm', () => {
  beforeEach(() => {
    useInstances().reset();
  });

  it('renders the heading and required fields', () => {
    const { getByText, container } = render(() => (
      <ProvisionForm onCancel={() => {}} onSuccess={() => {}} />
    ));
    expect(getByText('Provision New Instance')).toBeInTheDocument();
    expect(container.querySelector('#subdomain')).not.toBeNull();
    expect(container.querySelector('#displayName')).not.toBeNull();
    expect(container.querySelector('#adminPassword')).not.toBeNull();
  });

  it('updates subdomain preview text as user types', () => {
    const { container, getByText } = render(() => (
      <ProvisionForm onCancel={() => {}} onSuccess={() => {}} />
    ));
    const sub = container.querySelector('#subdomain') as HTMLInputElement;
    fireEvent.input(sub, { target: { value: 'foo' } });
    expect(getByText(/foo\.xcord-dev\.net/)).toBeInTheDocument();
  });

  it('Cancel button invokes onCancel', () => {
    const onCancel = vi.fn();
    const { getByText } = render(() => (
      <ProvisionForm onCancel={onCancel} onSuccess={() => {}} />
    ));
    fireEvent.click(getByText('Cancel'));
    expect(onCancel).toHaveBeenCalled();
  });

  it('submits successfully and calls onSuccess', async () => {
    mockFetch({
      'POST /api/v1/admin/instances': () => ({ status: 200, body: { id: 'i-1' } }),
    });
    const onSuccess = vi.fn();
    const { container } = render(() => (
      <ProvisionForm onCancel={() => {}} onSuccess={onSuccess} />
    ));
    (container.querySelector('#subdomain') as HTMLInputElement).value = 'foo';
    fireEvent.input(container.querySelector('#subdomain')!, { target: { value: 'foo' } });
    fireEvent.input(container.querySelector('#displayName')!, { target: { value: 'Foo' } });
    fireEvent.input(container.querySelector('#adminPassword')!, { target: { value: 'pw12345' } });
    fireEvent.submit(container.querySelector('form')!);
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
  });

  it('shows error when provisioning fails', async () => {
    mockFetch({
      'POST /api/v1/admin/instances': () => ({ status: 400, body: { message: 'subdomain taken' } }),
    });
    const { container, findByText } = render(() => (
      <ProvisionForm onCancel={() => {}} onSuccess={() => {}} />
    ));
    fireEvent.input(container.querySelector('#subdomain')!, { target: { value: 'foo' } });
    fireEvent.input(container.querySelector('#displayName')!, { target: { value: 'Foo' } });
    fireEvent.input(container.querySelector('#adminPassword')!, { target: { value: 'pw12345' } });
    fireEvent.submit(container.querySelector('form')!);
    expect(await findByText(/subdomain taken|Failed to provision/)).toBeInTheDocument();
  });
});
