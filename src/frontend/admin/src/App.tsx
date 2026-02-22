import { createSignal, Show, onMount } from 'solid-js';
import { useAuth } from './stores/auth.store';
import { useInstances } from './stores/instance.store';
import { Login } from './components/Login';
import { Layout } from './components/Layout';
import { InstanceList } from './components/InstanceList';
import { InstanceDetail } from './components/InstanceDetail';
import { ProvisionForm } from './components/ProvisionForm';

type Page = 'login' | 'instances' | 'instance-detail' | 'provision';

export function App() {
  const auth = useAuth();
  const instanceStore = useInstances();
  const [currentPage, setCurrentPage] = createSignal<Page>('login');
  const [selectedInstanceId, setSelectedInstanceId] = createSignal<string | null>(null);

  onMount(async () => {
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
    <Show
      when={auth.isLoading}
      fallback={
        <Show
          when={auth.isAuthenticated && auth.isAdmin}
          fallback={<Login />}
        >
          <Layout
            currentPage={currentPage() === 'instance-detail' ? 'instance-detail' : 'instances'}
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
          </Layout>
        </Show>
      }
    >
      <div class="min-h-screen flex items-center justify-center">
        <div class="text-lg">Loading...</div>
      </div>
    </Show>
  );
}
