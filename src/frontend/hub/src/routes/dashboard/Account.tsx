import { createSignal, Show } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { useAuth } from '../../stores/auth.store';

export default function Account() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = createSignal(auth.user?.displayName ?? '');
  const [email, setEmail] = createSignal(auth.user?.email ?? '');
  const [savingProfile, setSavingProfile] = createSignal(false);
  const [profileMessage, setProfileMessage] = createSignal('');

  const [currentPassword, setCurrentPassword] = createSignal('');
  const [newPassword, setNewPassword] = createSignal('');
  const [confirmNewPassword, setConfirmNewPassword] = createSignal('');
  const [changingPassword, setChangingPassword] = createSignal(false);
  const [passwordMessage, setPasswordMessage] = createSignal('');
  const [passwordError, setPasswordError] = createSignal('');

  const [confirmDelete, setConfirmDelete] = createSignal(false);
  const [deleteConfirmText, setDeleteConfirmText] = createSignal('');
  const [deletePassword, setDeletePassword] = createSignal('');
  const [deletingAccount, setDeletingAccount] = createSignal(false);
  const [deleteError, setDeleteError] = createSignal('');

  const handleSaveProfile = async (e: Event) => {
    e.preventDefault();
    setSavingProfile(true);
    setProfileMessage('');
    try {
      const token = localStorage.getItem('xcord_hub_token');
      const response = await fetch('/api/v1/auth/profile', {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({ displayName: displayName(), email: email() }),
      });
      if (response.ok) {
        setProfileMessage('Profile updated');
        setTimeout(() => setProfileMessage(''), 3000);
      }
    } catch {
      setProfileMessage('Failed to update profile');
    } finally {
      setSavingProfile(false);
    }
  };

  const handleDeleteAccount = async () => {
    setDeleteError('');

    if (deleteConfirmText() !== auth.user?.username) {
      setDeleteError('Username does not match. Please type your username exactly to confirm.');
      return;
    }

    if (!deletePassword()) {
      setDeleteError('Password is required to confirm account deletion.');
      return;
    }

    setDeletingAccount(true);
    const success = await auth.deleteAccount(deletePassword());
    setDeletingAccount(false);

    if (success) {
      navigate('/');
    } else {
      setDeleteError(auth.error || 'Failed to delete account. Please try again.');
    }
  };

  const handleChangePassword = async (e: Event) => {
    e.preventDefault();
    setPasswordError('');
    setPasswordMessage('');

    if (newPassword() !== confirmNewPassword()) {
      setPasswordError('Passwords do not match');
      return;
    }
    if (newPassword().length < 8) {
      setPasswordError('New password must be at least 8 characters');
      return;
    }

    setChangingPassword(true);
    const success = await auth.changePassword(currentPassword(), newPassword());
    setChangingPassword(false);

    if (success) {
      setPasswordMessage('Password changed successfully');
      setCurrentPassword('');
      setNewPassword('');
      setConfirmNewPassword('');
      setTimeout(() => setPasswordMessage(''), 3000);
    } else {
      setPasswordError(auth.error || 'Failed to change password');
    }
  };

  return (
    <div class="p-8 max-w-xl">
      <h1 class="text-2xl font-bold text-xcord-text-primary mb-8">Account</h1>

      {/* Profile */}
      <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6">
        <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Profile</h2>
        <form onSubmit={handleSaveProfile} class="space-y-4">
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Username</label>
            <input
              type="text"
              value={auth.user?.username ?? ''}
              disabled
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-muted rounded border-0 cursor-not-allowed"
            />
          </div>
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Display Name</label>
            <input
              type="text"
              value={displayName()}
              onInput={(e) => setDisplayName(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
            />
          </div>
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Email</label>
            <input
              type="email"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
            />
          </div>
          <Show when={profileMessage()}>
            <div class="text-sm text-xcord-green">{profileMessage()}</div>
          </Show>
          <button
            type="submit"
            disabled={savingProfile()}
            class="px-4 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded text-sm font-medium transition"
          >
            {savingProfile() ? 'Saving...' : 'Save Changes'}
          </button>
        </form>
      </div>

      {/* Change Password */}
      <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6">
        <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Change Password</h2>
        <form onSubmit={handleChangePassword} class="space-y-4">
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Current Password</label>
            <input
              type="password"
              value={currentPassword()}
              onInput={(e) => setCurrentPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">New Password</label>
            <input
              type="password"
              value={newPassword()}
              onInput={(e) => setNewPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
              minLength={8}
            />
          </div>
          <div>
            <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Confirm New Password</label>
            <input
              type="password"
              value={confirmNewPassword()}
              onInput={(e) => setConfirmNewPassword(e.currentTarget.value)}
              class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
              required
            />
          </div>
          <Show when={passwordError()}>
            <div class="text-sm text-xcord-red">{passwordError()}</div>
          </Show>
          <Show when={passwordMessage()}>
            <div class="text-sm text-xcord-green">{passwordMessage()}</div>
          </Show>
          <button
            type="submit"
            disabled={changingPassword()}
            class="px-4 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded text-sm font-medium transition"
          >
            {changingPassword() ? 'Changing...' : 'Change Password'}
          </button>
        </form>
      </div>

      {/* Danger Zone */}
      <div class="bg-xcord-bg-secondary rounded-lg p-6 border border-xcord-red/20">
        <h2 class="text-lg font-semibold text-xcord-red mb-4">Danger Zone</h2>
        <Show when={!confirmDelete()}>
          <p class="text-sm text-xcord-text-muted mb-3">
            Permanently delete your account and all associated data. This action cannot be undone.
          </p>
          <button
            onClick={() => { setConfirmDelete(true); setDeleteError(''); }}
            class="px-4 py-2 bg-xcord-red/10 text-xcord-red hover:bg-xcord-red/20 rounded text-sm font-medium transition"
          >
            Delete Account
          </button>
        </Show>
        <Show when={confirmDelete()}>
          <div class="space-y-4">
            <p class="text-sm text-xcord-text-muted">
              This will permanently delete your account and suspend all your instances. This action cannot be undone.
            </p>
            <div>
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
                Type your username <span class="text-xcord-text-primary font-mono">{auth.user?.username}</span> to confirm
              </label>
              <input
                type="text"
                value={deleteConfirmText()}
                onInput={(e) => setDeleteConfirmText(e.currentTarget.value)}
                placeholder={auth.user?.username ?? ''}
                autocomplete="off"
                class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-red"
              />
            </div>
            <div>
              <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Password</label>
              <input
                type="password"
                value={deletePassword()}
                onInput={(e) => setDeletePassword(e.currentTarget.value)}
                placeholder="Enter your password"
                class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-red"
              />
            </div>
            <Show when={deleteError()}>
              <div class="text-sm text-xcord-red">{deleteError()}</div>
            </Show>
            <div class="flex gap-3">
              <button
                onClick={handleDeleteAccount}
                disabled={deletingAccount() || !deleteConfirmText() || !deletePassword()}
                class="px-4 py-2 bg-xcord-red text-white hover:opacity-80 disabled:opacity-50 rounded text-sm font-medium transition"
              >
                {deletingAccount() ? 'Deleting...' : 'Permanently Delete'}
              </button>
              <button
                onClick={() => { setConfirmDelete(false); setDeletePassword(''); setDeleteConfirmText(''); setDeleteError(''); }}
                disabled={deletingAccount()}
                class="px-4 py-2 bg-xcord-bg-accent text-xcord-text-secondary rounded text-sm transition"
              >
                Cancel
              </button>
            </div>
          </div>
        </Show>
      </div>
    </div>
  );
}
