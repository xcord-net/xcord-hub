# Xcord Hub Docker Deployment

This directory contains the Docker configuration for deploying the Xcord Hub gateway.

## Quick Start

1. Copy the example configuration:
   ```bash
   cp gateway.example.json gateway.json
   ```

2. Edit `gateway.json` with your configuration:
   - Update database credentials
   - Set secure JWT signing key (minimum 32 characters)
   - Set secure admin password
   - Generate a 32-byte base64 key for secret encryption
   - Configure storage endpoints

3. Set environment variables (optional):
   ```bash
   export HUB_DOMAIN=hub.example.com
   export GATEWAY_CONFIG_FILE=./gateway.json
   ```

4. Start the services:
   ```bash
   docker compose up -d
   ```

5. Run database migrations:
   ```bash
   docker compose exec gateway /app/entrypoint.sh --migrate
   ```

## Services

### gateway
The Xcord Hub API server (XcordHub.Api).

- Port: 8080 (mapped from container port 80)
- Health: http://localhost:8080/health
- Depends on: gateway-pg, redis, minio, docker-socket-proxy

### gateway-pg
PostgreSQL 17 database for the hub.

- Internal port: 5432
- Database: xcord_hub
- Volume: gateway-pg-data

### redis
Redis 7 cache (shared with federation instances).

- Internal port: 6379
- Volume: redis-data

### minio
MinIO object storage (shared with federation instances).

- API Port: 9000
- Console Port: 9001
- Console URL: http://localhost:9001
- Volume: minio-data

### caddy
Reverse proxy for the hub and federation instances.

- HTTP Port: 80
- HTTPS Port: 443
- Volumes: caddy-data, caddy-config

### docker-socket-proxy
Secure proxy for Docker socket (used by gateway to provision instances).

- Internal only (no port exposure)
- Read-only access to Docker API
- Permissions: CONTAINERS=1, NETWORKS=1, IMAGES=1, POST=1

### livekit
LiveKit server for voice/video calls.

- Port: 7880 (HTTP API)
- Port: 7881 (WebRTC)
- Configuration: livekit.yaml

## Configuration

The gateway expects configuration as a Docker secret at `/run/secrets/gateway-config`.

The `entrypoint.sh` script transforms the JSON config into `appsettings.Production.json`.

### Configuration Sections

- **database**: PostgreSQL connection string
- **redis**: Redis connection string and channel prefix
- **jwt**: Issuer, audience, and signing key for JWT tokens
- **storage**: MinIO endpoint, credentials, and bucket name
- **docker**: Docker socket proxy URL
- **caddy**: Caddy admin API URL
- **admin**: Seed admin user credentials
- **secretEncryption**: AES-256 key for encrypting instance secrets
- **cors**: Allowed origins
- **rateLimiting**: Token bucket rate limiting configuration

## Development

To rebuild the gateway image:

```bash
docker compose build gateway
```

To view logs:

```bash
docker compose logs -f gateway
```

## Security Notes

1. **Never commit `gateway.json`** - it contains secrets
2. Replace all placeholder keys/passwords in production
3. The docker-socket-proxy limits Docker API access to read-only operations plus container creation
4. The gateway runs as non-root user (UID 1001)
5. Caddy admin API is bound to localhost only

## Networks

All services run on the `xcord-shared-net` bridge network, allowing:
- Gateway to communicate with infrastructure services
- Gateway to provision and manage federation instance containers
- Federation instances to share Redis, MinIO, and LiveKit

## Volumes

- `gateway-pg-data`: PostgreSQL data
- `redis-data`: Redis persistence
- `minio-data`: Object storage
- `caddy-data`: Caddy certificates and storage
- `caddy-config`: Caddy runtime configuration
