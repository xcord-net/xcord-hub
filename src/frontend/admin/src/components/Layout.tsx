import { JSX } from 'solid-js';
import { useAuth } from '../stores/auth.store';

interface LayoutProps {
  children: JSX.Element;
  currentPage: 'instances' | 'instance-detail';
  onNavigate: (page: string) => void;
}

export function Layout(props: LayoutProps) {
  const auth = useAuth();

  const handleLogout = async () => {
    await auth.logout();
    window.location.href = '/login';
  };

  return (
    <div class="min-h-screen bg-gray-50">
      <nav class="bg-blue-600 text-white p-4 shadow-md">
        <div class="container mx-auto flex items-center justify-between">
          <h1 class="text-xl font-bold">Xcord Hub Admin</h1>
          <div class="flex items-center gap-4">
            <span class="text-sm">{auth.username}</span>
            <button
              onClick={handleLogout}
              class="px-3 py-1 bg-blue-700 hover:bg-blue-800 rounded text-sm"
            >
              Logout
            </button>
          </div>
        </div>
      </nav>

      <div class="container mx-auto py-6 px-4">
        <div class="flex gap-6">
          <aside class="w-48 bg-white rounded-lg shadow p-4">
            <nav class="space-y-2">
              <button
                onClick={() => props.onNavigate('instances')}
                class={`w-full text-left px-3 py-2 rounded ${
                  props.currentPage === 'instances' || props.currentPage === 'instance-detail'
                    ? 'bg-blue-100 text-blue-700'
                    : 'hover:bg-gray-100'
                }`}
              >
                Instances
              </button>
            </nav>
          </aside>

          <main class="flex-1">{props.children}</main>
        </div>
      </div>
    </div>
  );
}
