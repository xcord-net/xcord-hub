import { createSignal, createEffect, Show, onMount } from 'solid-js';
import { useAuth } from './stores/auth.store';
import { useInstances } from './stores/instance.store';
import { Login } from './components/Login';
import { Layout } from './components/Layout';
import { InstanceList } from './components/InstanceList';
import { InstanceDetail } from './components/InstanceDetail';
import { ProvisionForm } from './components/ProvisionForm';
import { MailingListPage } from './components/MailingListPage';
import { SystemConfigPage } from './components/SystemConfigPage';
import { SetupWizard } from './components/SetupWizard';

type Page = 'login' | 'instances' | 'instance-detail' | 'provision' | 'mailing-list' | 'settings' | 'setup';

export function App() {
  const auth = useAuth();
  const instanceStore = useInstances();
  const [currentPage, setCurrentPage] = createSignal<Page>('login');
  const [selectedInstanceId, setSelectedInstanceId] = createSignal<string | null>(null);

  createEffect(() => {
    const titles: Record<Page, string> = {
      'login': 'Login - Xcord Admin',
      'instances': 'Instances - Xcord Admin',
      'instance-detail': 'Instance Details - Xcord Admin',
      'provision': 'New Instance - Xcord Admin',
      'mailing-list': 'Mailing List - Xcord Admin',
      'settings': 'Settings - Xcord Admin',
      'setup': 'Setup - Xcord Admin',
    };
    document.title = titles[currentPage()] ?? 'Xcord Admin';
  });

  onMount(async () => {
    // Check if first-boot setup is needed before attempting auth validation
    try {
      const res = await fetch('/api/v1/setup/status');
      const data = await res.json();
      if (data.needsSetup) {
        setCurrentPage('setup');
        return;
      }
    } catch {
      // If setup endpoint fails, proceed normally
    }

    const isValid = await auth.validateAuth();
    if (isValid) {
      setCurrentPage('instances');
    } else {
      setCurrentPage('login');
    }
  });

  const handleNavigate = (page: string) => {
    if (page === 'instances') {
      instanceStore.clearSelectedInstance();
      setSelectedInstanceId(null);
      setCurrentPage('instances');
    } else if (page === 'mailing-list') {
      setCurrentPage('mailing-list');
    } else if (page === 'settings') {
      setCurrentPage('settings');
    }
  };

  const handleSelectInstance = (id: string) => {
    setSelectedInstanceId(id);
    setCurrentPage('instance-detail');
  };

  const handleProvisionNew = () => {
    setCurrentPage('provision');
  };

  const handleProvisionSuccess = () => {
    setCurrentPage('instances');
    instanceStore.fetchInstances();
  };

  const handleBackToInstances = () => {
    instanceStore.clearSelectedInstance();
    setSelectedInstanceId(null);
    setCurrentPage('instances');
  };

  return (
    <>
      <Show when={currentPage() === 'setup'}>
        <SetupWizard onComplete={() => {
          // After setup, reload to go through normal auth flow
          window.location.reload();
        }} />
      </Show>

      <Show when={currentPage() !== 'setup'}>
        <Show
          when={auth.isLoading}
          fallback={
            <Show
              when={auth.isAuthenticated && auth.isAdmin}
              fallback={<Login />}
            >
              <Layout
                currentPage={
                  currentPage() === 'mailing-list' ? 'mailing-list'
                  : currentPage() === 'settings' ? 'settings'
                  : currentPage() === 'instance-detail' ? 'instance-detail'
                  : 'instances'
                }
                onNavigate={handleNavigate}
              >
                <Show when={currentPage() === 'instances'}>
                  <InstanceList
                    onSelectInstance={handleSelectInstance}
                    onProvisionNew={handleProvisionNew}
                  />
                </Show>

                <Show when={currentPage() === 'instance-detail' && selectedInstanceId()}>
                  <InstanceDetail
                    instanceId={selectedInstanceId()!}
                    onBack={handleBackToInstances}
                  />
                </Show>

                <Show when={currentPage() === 'provision'}>
                  <ProvisionForm
                    onCancel={handleBackToInstances}
                    onSuccess={handleProvisionSuccess}
                  />
                </Show>

                <Show when={currentPage() === 'mailing-list'}>
                  <MailingListPage />
                </Show>

                <Show when={currentPage() === 'settings'}>
                  <SystemConfigPage />
                </Show>
              </Layout>
            </Show>
          }
        >
          <div class="min-h-screen flex items-center justify-center">
            <div class="text-lg">Loading...</div>
          </div>
        </Show>
      </Show>
    </>
  );
}
