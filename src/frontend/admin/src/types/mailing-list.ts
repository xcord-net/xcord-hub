export interface MailingListEntry {
  id: string;
  email: string;
  tier: string;
  createdAt: string;
}

export interface MailingListResponse {
  entries: MailingListEntry[];
  total: number;
  page: number;
  pageSize: number;
}
