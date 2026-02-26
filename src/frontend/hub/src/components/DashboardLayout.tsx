import type { ParentProps } from 'solid-js';
import HubGuard from './HubGuard';
import AppShell from './AppShell';
import PageMeta from './PageMeta';

export default function DashboardLayout(props: ParentProps) {
  return (
    <HubGuard>
      <PageMeta
        title="Dashboard - Xcord Hub"
        description="Manage your Xcord instances."
        path="/dashboard"
        noindex
      />
      <AppShell>{props.children}</AppShell>
    </HubGuard>
  );
}
