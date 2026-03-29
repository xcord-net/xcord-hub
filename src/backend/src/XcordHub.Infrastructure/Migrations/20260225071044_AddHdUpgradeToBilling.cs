using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHdUpgradeToBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DockerKekSecretId",
                table: "instance_infrastructure",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DatabaseUsername",
                table: "instance_infrastructure",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RedisUsername",
                table: "instance_infrastructure",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "RedisPassword",
                table: "instance_infrastructure",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            // Metered billing columns on instance_billing
            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionItemId",
                table: "instance_billing",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMeteredBilling",
                table: "instance_billing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Uptime intervals table for usage-based billing
            migrationBuilder.CreateTable(
                name: "uptime_intervals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportedToStripe = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uptime_intervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_uptime_intervals_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_uptime_intervals_ManagedInstanceId_EndedAt",
                table: "uptime_intervals",
                columns: new[] { "ManagedInstanceId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_uptime_intervals_ReportedToStripe_EndedAt",
                table: "uptime_intervals",
                columns: new[] { "ReportedToStripe", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "uptime_intervals");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionItemId",
                table: "instance_billing");

            migrationBuilder.DropColumn(
                name: "IsMeteredBilling",
                table: "instance_billing");

            migrationBuilder.DropColumn(
                name: "DockerKekSecretId",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "DatabaseUsername",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "RedisUsername",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "RedisPassword",
                table: "instance_infrastructure");
        }
    }
}
