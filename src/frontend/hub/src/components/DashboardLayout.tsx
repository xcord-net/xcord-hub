import type { ParentProps } from 'solid-js';
import HubGuard from './HubGuard';
import AppShell from './AppShell';

export default function DashboardLayout(props: ParentProps) {
  return (
    <HubGuard>
      <AppShell>{props.children}</AppShell>
    </HubGuard>
  );
}
