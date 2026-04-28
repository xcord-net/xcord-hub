import { useAuth } from '../../stores/auth.store';
import { useInstances } from '../../stores/instance.store';
import { useMailingList } from '../../stores/mailing-list.store';
import { useSystemConfig } from '../../stores/system-config.store';

export function resetAllStoresForTest(): void {
  useAuth().reset();
  useInstances().reset();
  useMailingList().reset();
  useSystemConfig().reset();
}
