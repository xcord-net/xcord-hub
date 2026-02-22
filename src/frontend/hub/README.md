# Xcord Hub SPA

User-facing hub application with two modes: landing page (unauthenticated) and shell (authenticated).

## Architecture

### Two Modes

1. **Landing Mode** (unauthenticated)
   - Marketing page
   - Login/signup forms
   - Feature overview

2. **Shell Mode** (authenticated)
   - Tab bar with hub tab + connected instance tabs
   - Iframe per connected instance (all stay loaded, hidden when not selected)
   - Instance discovery in hub tab
   - Profile & billing panel

### State Management

- `auth.store.ts` - Hub authentication state
- `instance.store.ts` - Connected instances (persisted to localStorage)
- `unread.store.ts` - Unread badges from instance iframes

### Components

- `LandingPage.tsx` - Marketing, login, signup
- `Shell.tsx` - Authenticated shell container
- `TabBar.tsx` - Tab navigation (hub + servers + add + profile)
- `InstanceIframe.tsx` - Iframe wrapper with visibility management
- `InstanceDiscovery.tsx` - Search/browse instances
- `ProfilePanel.tsx` - User profile & billing modal

## postMessage Protocol

### From Instance → Hub

Instance SPAs send unread counts to the hub:

```typescript
window.parent.postMessage({
  type: 'xcord_unread',
  instanceUrl: 'https://instance.example.com',
  count: 5
}, '*');
```

### From Hub → Instance

Hub sends LeaveConversation when iframe is hidden (to reduce idle traffic):

```typescript
iframeWindow.postMessage({
  type: 'xcord_leave_conversation'
}, instanceUrl);
```

## localStorage Keys

- `xcord_hub_token` - JWT for hub authentication
- `xcord_connected_instances` - Array of connected instance objects:
  ```json
  [
    {
      "url": "https://instance.example.com",
      "name": "My Community",
      "iconUrl": "https://..."
    }
  ]
  ```

## Development

```bash
npm install
npm run dev    # Runs on port 3001, proxies /api to :5100
npm run build  # TypeScript check + Vite build
```

## API Endpoints

- `POST /api/v1/auth/login` - Hub login
- `POST /api/v1/auth/register` - Hub signup
- `GET /api/v1/auth/me` - Get current user
- `GET /api/v1/instances/public` - List public instances
- `GET /api/v1/instances/search?q=...` - Search instances

## Instance Integration

Instances should:
1. Send `xcord_unread` postMessage when unread count changes
2. Listen for `xcord_leave_conversation` and disconnect from active channel SignalR groups

## Styling

Uses Tailwind CSS with custom `xcord-*` color tokens defined in `index.css`.
