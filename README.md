# xcord-hub

Control plane / gateway module for [Xcord](https://github.com/xcord-net). Handles instance provisioning, lifecycle management, hub user authentication, billing, and instance discovery.

## Structure

```
xcord-hub/
├── src/
│   ├── backend/         # ASP.NET Core gateway API (.NET 9)
│   └── frontend/
│       ├── hub/         # Hub SPA: landing page + shell (SolidJS + Vite)
│       └── admin/       # Gateway admin SPA (SolidJS + Vite)
├── docker/              # Gateway stack (Caddy, Compose, configs)
└── Dockerfile           # Multi-stage production image
```

## Backend

The gateway API provisions and manages federation instances as Docker containers. Key features:

- Hub user auth (register, login, 2FA, password management)
- Instance CRUD and lifecycle (create, suspend, resume, destroy)
- Provisioning pipeline (database, MinIO bucket, DNS, Caddy route, container)
- Billing tiers with resource limits and feature flags
- Instance discovery (public browsing)
- Health monitoring and reconciliation
- Snowflake worker ID allocation (never recycled)

## Frontend

Two SPAs:
- **Hub** -- Landing page, auth flows, instance dashboard, billing, server browser shell with iframe-per-instance
- **Admin** -- Gateway admin panel for instance management and provisioning

## Running Tests

```bash
# Unit + infrastructure + security + architecture tests
dotnet test src/backend/XcordHub.sln --verbosity quiet
```

## License

Apache 2.0 -- see [LICENSE](LICENSE).
