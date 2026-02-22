#!/bin/sh
set -e

CONFIG_PATH="/run/secrets/gateway-config"
APPSETTINGS_ENV="${ASPNETCORE_ENVIRONMENT:-Production}"
APPSETTINGS_PATH="/app/appsettings.${APPSETTINGS_ENV}.json"

# Check if config secret exists
if [ -f "$CONFIG_PATH" ]; then
    echo "Reading configuration from Docker secret..."

    # Transform gateway-config JSON to appsettings.Production.json using jq
    jq '{
        Database: {
            ConnectionString: .database.connectionString
        },
        Redis: {
            ConnectionString: .redis.connectionString,
            ChannelPrefix: (.redis.channelPrefix // "hub:")
        },
        Jwt: {
            Issuer: .jwt.issuer,
            Audience: .jwt.audience,
            SecretKey: .jwt.signingKey
        },
        Storage: {
            Endpoint: .storage.endpoint,
            AccessKey: .storage.accessKey,
            SecretKey: .storage.secretKey,
            BucketName: .storage.bucketName,
            UseSSL: (.storage.useSsl // false)
        },
        Docker: {
            SocketProxyUrl: (.docker.socketProxyUrl // "http://docker-socket-proxy:2375"),
            UseReal: (.docker.useReal // false),
            InstanceImage: (.docker.instanceImage // "xcord-fed:latest")
        },
        Caddy: {
            AdminUrl: (.caddy.adminUrl // "http://caddy:2019"),
            UseReal: (.caddy.useReal // false)
        },
        Admin: {
            Username: .admin.username,
            Email: .admin.email,
            Password: .admin.password
        },
        Encryption: {
            Key: .secretEncryption.key
        },
        Cors: {
            AllowedOrigins: (.cors.allowedOrigins // [])
        },
        RateLimiting: {
            TokenLimit: (.rateLimiting.tokenLimit // 100),
            ReplenishmentPeriodSeconds: (.rateLimiting.replenishmentPeriodSeconds // 10),
            TokensPerPeriod: (.rateLimiting.tokensPerPeriod // 20)
        },
        Email: {
            SmtpHost: (.email.smtpHost // "mailpit"),
            SmtpPort: (.email.smtpPort // 1025),
            SmtpUsername: (.email.smtpUsername // ""),
            SmtpPassword: (.email.smtpPassword // ""),
            FromAddress: (.email.fromAddress // "noreply@xcord.local"),
            FromName: (.email.fromName // "Xcord"),
            UseSsl: (.email.useSsl // false),
            DevMode: (.email.devMode // false),
            HubBaseUrl: (.email.hubBaseUrl // "")
        }
    }' "$CONFIG_PATH" > "$APPSETTINGS_PATH"

    echo "Configuration generated at $APPSETTINGS_PATH"
else
    echo "No Docker secret found at $CONFIG_PATH, using existing configuration"
fi

# Run database migrations if --migrate flag is passed
if [ "$1" = "--migrate" ]; then
    echo "Running database migrations..."
    # Migrations are applied via the app startup in production
    export ASPNETCORE_ENVIRONMENT=Production
    shift
fi

# Set production environment
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="http://+:80"

echo "Starting Xcord Hub Gateway..."
exec dotnet XcordHub.Api.dll "$@"
