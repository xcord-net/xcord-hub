# ===== Stage 1: Build Hub SPA =====
FROM node:24.11.1-alpine3.22 AS build-hub-spa
WORKDIR /app
ARG VERSION=0.0.0-dev
ENV VITE_APP_VERSION=$VERSION

# Copy hub frontend source
COPY src/frontend/hub/ .

# Copy generated types (resolved as ../generated from /app)
COPY src/frontend/generated/ /generated/

# Build if package.json exists, otherwise create placeholder
RUN if [ -f package.json ]; then \
        npm ci && npm run build:docker; \
    else \
        mkdir -p dist && \
        echo '<!DOCTYPE html><html><head><title>Hub</title></head><body><h1>Hub SPA - Coming Soon</h1></body></html>' > dist/index.html; \
    fi

# ===== Stage 2: Build Admin SPA =====
FROM node:24.11.1-alpine3.22 AS build-admin-spa
WORKDIR /app
ARG VERSION=0.0.0-dev
ENV VITE_APP_VERSION=$VERSION

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
ARG VERSION=0.0.0-dev

# Copy solution and project files for restore
COPY src/backend/Directory.Build.props src/backend/
COPY src/backend/XcordHub.sln src/backend/
COPY src/backend/src/XcordHub.Api/XcordHub.Api.csproj src/backend/src/XcordHub.Api/
COPY src/backend/src/XcordHub.Features/XcordHub.Features.csproj src/backend/src/XcordHub.Features/
COPY src/backend/src/XcordHub.Infrastructure/XcordHub.Infrastructure.csproj src/backend/src/XcordHub.Infrastructure/
COPY src/backend/src/XcordHub.Shared/XcordHub.Shared.csproj src/backend/src/XcordHub.Shared/
COPY xcord-common/src/Xcord.Common/Xcord.Common.csproj xcord-common/src/Xcord.Common/
COPY xcord-common/src/Xcord.Captcha/Xcord.Captcha.csproj xcord-common/src/Xcord.Captcha/

# The RID comes from the build platform, not from a literal. `linux-musl-x64`
# was hardcoded here and in the publish below, which fails on arm64 before it
# fails usefully: the restore succeeds, and then the OpenAPI document generator
# (Microsoft.Extensions.ApiDescription.Server) tries to RUN the freshly built
# x64 assembly on an aarch64 host and dies with MSB3073 exit code 2, naming a
# targets file rather than the architecture.
#
# Derived from `uname -m` rather than from BuildKit's TARGETARCH, because
# `ARG TARGETARCH=amd64` SHADOWS the automatic value — a declared default wins
# over the platform arg, so the first attempt at this fix silently produced
# linux-musl-x64 again on an aarch64 host. TARGETARCH is still honoured when
# explicitly passed, for a cross-build.
ARG TARGETARCH
RUN arch="${TARGETARCH:-$(uname -m)}"; \
    case "$arch" in \
      amd64|x86_64)  rid=linux-musl-x64 ;; \
      arm64|aarch64) rid=linux-musl-arm64 ;; \
      *) echo "unsupported build architecture: $arch" >&2; exit 1 ;; \
    esac; \
    echo "$rid" > /tmp/rid; \
    echo "building for $arch -> $rid"

# Restore dependencies
RUN dotnet restore src/backend/src/XcordHub.Api/XcordHub.Api.csproj -r "$(cat /tmp/rid)" -p:PublishReadyToRun=true

# Copy full source
COPY xcord-common/ xcord-common/
COPY src/backend/ src/backend/

# Publish
# RID + ReadyToRun match xcord-fed/Dockerfile for cold-start parity - see kanban #120
RUN dotnet publish src/backend/src/XcordHub.Api/XcordHub.Api.csproj \
    -c Release \
    -o /app/publish \
    -p:Version=$VERSION \
    -r "$(cat /tmp/rid)" \
    -p:PublishReadyToRun=true \
    --self-contained false \
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

# start-period accommodates cold .NET startup including EF migrations + DI; the
# hub starts during stack up so this is rarely tight, but slow CI runners need
# the same grace window as the federation instance image.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:80/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
