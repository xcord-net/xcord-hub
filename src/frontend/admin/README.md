# Xcord Hub Admin SPA

Admin interface for managing Xcord instances on the Hub gateway.

## Features

- Admin-only authentication with JWT claim validation
- Instance list with status filtering and pagination
- Instance detail view with health metrics, configuration, and logs
- Provision new instances
- Suspend/resume/destroy instances
- Edit resource limits and feature flags

## Development

```bash
npm install
npm run dev
```

Runs on http://localhost:3002 with API proxy to localhost:5100.

## Build

```bash
npm run build
```

## Tech Stack

- SolidJS 1.9
- TypeScript 5.7
- Vite 6.0
- Tailwind CSS 4.0

## API Endpoints

- POST /api/v1/auth/login - Admin login
- POST /api/v1/auth/refresh - Refresh token
- POST /api/v1/auth/logout - Logout
- GET /api/v1/admin/instances - List all instances (admin)
- GET /api/v1/admin/instances/:id - Get instance detail (admin)
- POST /api/v1/hub/instances - Provision new instance
- POST /api/v1/admin/instances/:id/suspend - Suspend instance (admin)
- POST /api/v1/admin/instances/:id/resume - Resume instance (admin)
- DELETE /api/v1/admin/instances/:id - Destroy instance (admin)
- PATCH /api/v1/admin/instances/:id/resource-limits - Update limits (admin)
- PATCH /api/v1/admin/instances/:id/feature-flags - Update flags (admin)
- GET /api/v1/admin/instances/:id/logs - Get instance logs (admin)
