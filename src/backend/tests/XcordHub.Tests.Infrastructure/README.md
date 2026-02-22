# XcordHub Infrastructure Tests

This project contains infrastructure tests for the Xcord Hub provisioning system using Docker-in-Docker (DinD) via Testcontainers.

## Purpose

These tests verify the full provisioning pipeline end-to-end by:

1. Starting a Docker-in-Docker environment
2. Provisioning shared services (PostgreSQL, Redis, MinIO, Caddy)
3. Triggering real instance provisioning workflows
4. Asserting outcomes via the ProvisioningEvent log (system is its own oracle)

## Test Categories

### ProvisioningPipelineTests
- Full provisioning end-to-end
- Pipeline resumption from last completed step
- Fresh provisioning from beginning

### FailureRecoveryTests
- Step failures with retry logic (up to 3 attempts)
- Successful retry after intermittent failure
- Verify phase failures
- Exception handling

### ReconcilerTests
- Missing container detection and restart
- Unhealthy container recovery
- Drift detection across all instances
- Network drift reconciliation

### HealthMonitorTests
- Healthy instance checks
- Consecutive failure detection
- Escalation triggers
- Health counter resets
- Full instance scans

### LifecycleTests
- Suspend/resume operations
- Destroy with cleanup
- Graceful shutdown
- Worker ID tombstoning

### IsolationTests
- Network isolation between instances
- Database isolation
- Redis key namespacing
- MinIO bucket isolation
- Concurrent provisioning without conflicts

## Running Tests

These tests require a Docker environment and will be skipped in environments without Docker:

```bash
# Run all infrastructure tests
dotnet test --filter "Category=Infrastructure"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ProvisioningPipelineTests"
```

## Implementation Status

Currently, all tests are marked with `Skip` because:

1. They require Docker-in-Docker which is not available in all CI/CD environments
2. They perform actual infrastructure operations (container provisioning, networking)
3. They are resource-intensive and time-consuming

The tests demonstrate the test patterns and validate that the code compiles correctly. In a full Docker environment, the skip attributes should be removed to enable execution.

## Test Oracle

These tests use the **ProvisioningEvent** table as the test oracle:

- Each provisioning step logs its execution and verification phases
- Tests assert on the event log rather than mocking infrastructure
- This ensures tests verify the actual system behavior, not test doubles

## Infrastructure

The `InfrastructureTestFixture` sets up:

- Docker-in-Docker container for isolated provisioning
- PostgreSQL 17 for hub database
- Redis 7 for caching
- MinIO for object storage
- Caddy for reverse proxy

All resources are ephemeral and automatically cleaned up after tests complete.
