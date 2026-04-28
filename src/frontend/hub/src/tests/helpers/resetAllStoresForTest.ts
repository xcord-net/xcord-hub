import { useAuth } from '../../stores/auth.store';
import { instanceStore } from '../../stores/instance.store';
import { unreadStore } from '../../stores/unread.store';

export function resetAllStoresForTest(): void {
  useAuth().reset();
  instanceStore.reset();
  unreadStore.reset();
}
