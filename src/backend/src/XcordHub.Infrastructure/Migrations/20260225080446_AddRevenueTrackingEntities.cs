using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevenueTrackingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_revenue_configs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    DefaultRevenueSharePercent = table.Column<int>(type: "integer", nullable: false),
                    MinPlatformCutPercent = table.Column<int>(type: "integer", nullable: false),
                    StripeConnectedAccountId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_revenue_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_revenue_configs_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_revenues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagedInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    PlatformFeeCents = table.Column<int>(type: "integer", nullable: false),
                    OwnerPayoutCents = table.Column<int>(type: "integer", nullable: false),
                    StripeTransferId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_revenues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platform_revenues_managed_instances_ManagedInstanceId",
                        column: x => x.ManagedInstanceId,
                        principalTable: "managed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instance_revenue_configs_ManagedInstanceId",
                table: "instance_revenue_configs",
                column: "ManagedInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_revenues_ManagedInstanceId",
                table: "platform_revenues",
                column: "ManagedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_platform_revenues_ManagedInstanceId_PeriodStart_PeriodEnd",
                table: "platform_revenues",
                columns: new[] { "ManagedInstanceId", "PeriodStart", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_revenue_configs");

            migrationBuilder.DropTable(
                name: "platform_revenues");
        }
    }
}
