using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_submissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedMemberCount = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_submissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hub_users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<byte[]>(type: "bytea", nullable: false),
                    EmailHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorFailureCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TwoFactorLockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TwoFactorSecret = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SubscriptionTier = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StripeCustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hub_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "login_attempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "managed_instances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MemberCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OnlineCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SnowflakeWorkerId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_managed_instances_hub_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "hub_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HubUserId = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_hub_users_HubUserId",
                        column: x => x.HubUserId,
                        principalTable: "hub_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HubUserId = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_hub_users_HubUserId",
                        column: x => x.HubUserId,
                        principalTable: "hub_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "federation_tokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_federation_tokens_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_billing",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    MediaEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BillingStatus = table.Column<int>(type: "integer", nullable: false),
                    BillingExempt = table.Column<bool>(type: "boolean", nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextBillingDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_billing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_billing_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_configs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    ResourceLimitsJson = table.Column<string>(type: "text", nullable: false),
                    FeatureFlagsJson = table.Column<string>(type: "text", nullable: false),
                    BatchUpgradesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_configs_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_health",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    IsHealthy = table.Column<bool>(type: "boolean", nullable: false),
                    LastCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_health", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_health_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_infrastructure",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    DockerNetworkId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DockerContainerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DockerSecretId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    DatabaseName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DatabasePassword = table.Column<byte[]>(type: "bytea", nullable: false),
                    RedisDb = table.Column<int>(type: "integer", nullable: false),
                    MinioAccessKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MinioSecretKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    CaddyRouteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LiveKitApiKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LiveKitSecretKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    BootstrapTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeployedImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AdminPasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PaymentMethodId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PlacedInPool = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: "default"),
                    PlacedInDataPool = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    PlacementRegion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_infrastructure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_infrastructure_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provisioning_events_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "backup_policies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    BackupDatabase = table.Column<bool>(type: "boolean", nullable: false),
                    BackupFiles = table.Column<bool>(type: "boolean", nullable: false),
                    BackupRedis = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_policies_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "backup_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backup_records_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "available_versions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Image = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReleaseNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsMinimumVersion = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumEnforcementDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_available_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_available_versions_hub_users_PublishedBy",
                        column: x => x.PublishedBy,
                        principalTable: "hub_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upgrade_rollouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalInstances = table.Column<int>(type: "integer", nullable: false),
                    CompletedInstances = table.Column<int>(type: "integer", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    MaxFailures = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedInstances = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TargetPool = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InitiatedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upgrade_rollouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_upgrade_rollouts_hub_users_InitiatedBy",
                        column: x => x.InitiatedBy,
                        principalTable: "hub_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upgrade_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UpgradeRolloutId = table.Column<long>(type: "bigint", nullable: true),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TargetImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PreviousVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NewVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upgrade_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_upgrade_events_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_upgrade_events_upgrade_rollouts_UpgradeRolloutId",
                        column: x => x.UpgradeRolloutId,
                        principalTable: "upgrade_rollouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "worker_id_registry",
                columns: table => new
                {
                    worker_id = table.Column<int>(type: "integer", nullable: false),
                    managed_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    is_tombstoned = table.Column<bool>(type: "boolean", nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_id_registry", x => x.worker_id);
                    table.ForeignKey(
                        name: "FK_worker_id_registry_managed_instances_managed_instance_id",
                        column: x => x.managed_instance_id,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_policies_ManagedInstanceId",
                table: "backup_policies",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_backup_records_ManagedInstanceId_StartedAt",
                table: "backup_records",
                columns: new[] { "ManagedInstanceId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_federation_tokens_ManagedInstanceId",
                table: "federation_tokens",
                column: "ManagedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_federation_tokens_TokenHash",
                table: "federation_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hub_users_EmailHash",
                table: "hub_users",
                column: "EmailHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hub_users_Username",
                table: "hub_users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_CreatedAt",
                table: "login_attempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_IpAddress",
                table: "login_attempts",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_instance_billing_ManagedInstanceId",
                table: "instance_billing",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_configs_ManagedInstanceId",
                table: "instance_configs",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_health_ManagedInstanceId",
                table: "instance_health",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_infrastructure_ManagedInstanceId",
                table: "instance_infrastructure",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_Domain",
                table: "managed_instances",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_OwnerId",
                table: "managed_instances",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_SnowflakeWorkerId",
                table: "managed_instances",
                column: "SnowflakeWorkerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_HubUserId",
                table: "password_reset_tokens",
                column: "HubUserId");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_events_ManagedInstanceId",
                table: "provisioning_events",
                column: "ManagedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_available_versions_Version",
                table: "available_versions",
                column: "Version",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_available_versions_PublishedBy",
                table: "available_versions",
                column: "PublishedBy");

            migrationBuilder.CreateIndex(
                name: "IX_upgrade_rollouts_InitiatedBy",
                table: "upgrade_rollouts",
                column: "InitiatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_upgrade_rollouts_Status",
                table: "upgrade_rollouts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_upgrade_events_ManagedInstanceId",
                table: "upgrade_events",
                column: "ManagedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_upgrade_events_UpgradeRolloutId",
                table: "upgrade_events",
                column: "UpgradeRolloutId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_HubUserId",
                table: "refresh_tokens",
                column: "HubUserId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_worker_id_registry_is_tombstoned",
                table: "worker_id_registry",
                column: "is_tombstoned");

            migrationBuilder.CreateIndex(
                name: "IX_worker_id_registry_managed_instance_id",
                table: "worker_id_registry",
                column: "managed_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_submissions");

            migrationBuilder.DropTable(
                name: "backup_policies");

            migrationBuilder.DropTable(
                name: "backup_records");

            migrationBuilder.DropTable(
                name: "upgrade_events");

            migrationBuilder.DropTable(
                name: "upgrade_rollouts");

            migrationBuilder.DropTable(
                name: "available_versions");

            migrationBuilder.DropTable(
                name: "federation_tokens");

            migrationBuilder.DropTable(
                name: "instance_billing");

            migrationBuilder.DropTable(
                name: "login_attempts");

            migrationBuilder.DropTable(
                name: "instance_configs");

            migrationBuilder.DropTable(
                name: "instance_health");

            migrationBuilder.DropTable(
                name: "instance_infrastructure");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "provisioning_events");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "worker_id_registry");

            migrationBuilder.DropTable(
                name: "managed_instances");

            migrationBuilder.DropTable(
                name: "hub_users");
        }
    }
}
