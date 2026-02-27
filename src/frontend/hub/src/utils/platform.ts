export type Platform = 'windows' | 'macos' | 'linux' | 'android' | 'ios' | 'unknown';

export function detectPlatform(): Platform {
  const ua = navigator.userAgent;

  // Mobile checks first — Android UA also contains "Linux"
  if (/Android/i.test(ua)) return 'android';
  if (/iPad|iPhone|iPod/.test(ua)) return 'ios';

  // Desktop
  if (/Win/i.test(ua)) return 'windows';
  if (/Mac/i.test(ua)) return 'macos';
  if (/Linux/i.test(ua)) return 'linux';

  return 'unknown';
}

const DESKTOP_BASE = 'https://github.com/xcord-net/xcord-desktop/releases/latest/download';

export interface DownloadLink {
  label: string;
  url: string;
  format: string;
}

export interface PlatformInfo {
  name: string;
  arch: string;
  links: DownloadLink[];
  comingSoon?: boolean;
}

export function getDownloadLinks(): Record<string, PlatformInfo> {
  return {
    windows: {
      name: 'Windows',
      arch: 'x64',
      links: [
        { label: 'Download .exe', url: `${DESKTOP_BASE}/Xcord_x64-setup.exe`, format: '.exe installer' },
      ],
    },
    macos: {
      name: 'macOS',
      arch: 'Universal',
      links: [
        { label: 'Apple Silicon', url: `${DESKTOP_BASE}/Xcord_arm64.dmg`, format: '.dmg (ARM64)' },
        { label: 'Intel', url: `${DESKTOP_BASE}/Xcord_x64.dmg`, format: '.dmg (x64)' },
      ],
    },
    linux: {
      name: 'Linux',
      arch: 'x64',
      links: [
        { label: 'Download .AppImage', url: `${DESKTOP_BASE}/Xcord_amd64.AppImage`, format: '.AppImage' },
        { label: 'Download .deb', url: `${DESKTOP_BASE}/Xcord_amd64.deb`, format: '.deb package' },
      ],
    },
    android: {
      name: 'Android',
      arch: '',
      links: [{ label: 'Google Play', url: '#', format: 'Play Store' }],
      comingSoon: true,
    },
    ios: {
      name: 'iOS',
      arch: '',
      links: [{ label: 'App Store', url: '#', format: 'App Store' }],
      comingSoon: true,
    },
  };
}
