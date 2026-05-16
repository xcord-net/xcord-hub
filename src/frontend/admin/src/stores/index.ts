import { useAuth } from './auth.store';
import { useInstances } from './instance.store';
import { useMailingList } from './mailing-list.store';
import { useSystemConfig } from './system-config.store';

/**
 * Resets all admin application stores to their initial state.
 * Called during logout to prevent data leakage between sessions.
 */
export function resetAllStores(): void {
  useAuth().reset();
  useInstances().reset();
  useMailingList().reset();
  useSystemConfig().reset();
}
