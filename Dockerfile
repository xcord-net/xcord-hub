# ===== Stage 1: Build Hub SPA =====
FROM node:22-alpine AS build-hub-spa
WORKDIR /app

# Copy hub frontend source
COPY src/frontend/hub/ .

# Copy generated types (resolved as ../generated from /app)
COPY src/frontend/generated/ /generated/

# Build if package.json exists, otherwise create placeholder
RUN if [ -f package.json ]; then \
        npm ci && npm run build; \
    else \
        mkdir -p dist && \
        echo '<!DOCTYPE html><html><head><title>Hub</title></head><body><h1>Hub SPA - Coming Soon</h1></body></html>' > dist/index.html; \
    fi

# ===== Stage 2: Build Admin SPA =====
FROM node:22-alpine AS build-admin-spa
WORKDIR /app

# Copy admin frontend source
COPY src/frontend/admin/ .

# Copy generated types (resolved as ../generated from /app)
COPY src/frontend/generated/ /generated/

# Build if package.json exists, otherwise create placeholder
RUN if [ -f package.json ]; then \
        npm ci && npm run build; \
    else \
        mkdir -p dist && \
        echo '<!DOCTYPE html><html><head><title>Admin</title></head><body><h1>Admin SPA - Coming Soon</h1></body></html>' > dist/index.html; \
    fi

# ===== Stage 3: Build backend =====
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build-backend
WORKDIR /src

# Copy solution and project files for restore
COPY src/backend/Directory.Build.props src/backend/
COPY src/backend/XcordHub.sln src/backend/
COPY src/backend/src/XcordHub.Api/XcordHub.Api.csproj src/backend/src/XcordHub.Api/
COPY src/backend/src/XcordHub.Features/XcordHub.Features.csproj src/backend/src/XcordHub.Features/
COPY src/backend/src/XcordHub.Infrastructure/XcordHub.Infrastructure.csproj src/backend/src/XcordHub.Infrastructure/
COPY src/backend/src/XcordHub.Shared/XcordHub.Shared.csproj src/backend/src/XcordHub.Shared/

# Restore dependencies
RUN dotnet restore src/backend/src/XcordHub.Api/XcordHub.Api.csproj

# Copy full source
COPY src/backend/ src/backend/

# Publish
RUN dotnet publish src/backend/src/XcordHub.Api/XcordHub.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ===== Stage 4: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Install jq and wget for config processing and health checks
RUN apk add --no-cache jq wget

# Create non-root user
RUN addgroup -g 1001 xcord-hub && \
    adduser -u 1001 -G xcord-hub -s /bin/sh -D xcord-hub

# Copy published backend
COPY --from=build-backend /app/publish .

# Copy Hub SPA to wwwroot
COPY --from=build-hub-spa /app/dist ./wwwroot

# Copy Admin SPA to wwwroot/admin
COPY --from=build-admin-spa /app/dist ./wwwroot/admin

# Copy entrypoint
COPY docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Set ownership
RUN chown -R xcord-hub:xcord-hub /app

USER xcord-hub

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:80/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
