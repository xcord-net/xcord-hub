import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@solidjs/testing-library';
import { MailingListPage } from './MailingListPage';
import { useMailingList } from '../stores/mailing-list.store';
import { mockFetch } from '../tests/helpers/mockFetch';

const LIST_PATH = '/api/v1/admin/mailing-list';

const sampleEntries = {
  entries: [
    { email: 'a@example.com', tier: 'Pro', createdAt: '2026-01-01T00:00:00Z' },
    { email: 'b@example.com', tier: 'Basic', createdAt: '2026-01-02T00:00:00Z' },
  ],
  total: 2,
};

describe('MailingListPage', () => {
  beforeEach(() => {
    useMailingList().reset();
  });

  it('renders the page heading', () => {
    mockFetch({ [`GET ${LIST_PATH}`]: () => sampleEntries });
    const { getByText } = render(() => <MailingListPage />);
    expect(getByText('Mailing List')).toBeInTheDocument();
  });

  it('shows loading indicator initially', () => {
    mockFetch({ [`GET ${LIST_PATH}`]: () => sampleEntries });
    const { getByText } = render(() => <MailingListPage />);
    expect(getByText('Loading...')).toBeInTheDocument();
  });

  it('renders subscriber rows once entries load', async () => {
    mockFetch({ [`GET ${LIST_PATH}`]: () => sampleEntries });
    const { findByText } = render(() => <MailingListPage />);
    expect(await findByText('a@example.com')).toBeInTheDocument();
    expect(await findByText('b@example.com')).toBeInTheDocument();
  });

  it('renders empty placeholder when there are no entries', async () => {
    mockFetch({ [`GET ${LIST_PATH}`]: () => ({ entries: [], total: 0 }) });
    const { findByText } = render(() => <MailingListPage />);
    expect(await findByText('No subscribers found')).toBeInTheDocument();
  });

  it('clicking a tier filter sets the store filter', async () => {
    mockFetch({ [`GET ${LIST_PATH}`]: () => sampleEntries });
    const { findByText, getAllByRole } = render(() => <MailingListPage />);
    await findByText('a@example.com');
    const proButton = getAllByRole('button').find(b => b.textContent === 'Pro')!;
    fireEvent.click(proButton);
    await waitFor(() => expect(useMailingList().tierFilter).toBe('Pro'));
  });
});
