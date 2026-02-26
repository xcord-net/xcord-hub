using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FilterWorkerIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_instances_SnowflakeWorkerId",
                table: "managed_instances");

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_SnowflakeWorkerId",
                table: "managed_instances",
                column: "SnowflakeWorkerId",
                unique: true,
                filter: "\"SnowflakeWorkerId\" > 0 AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_instances_SnowflakeWorkerId",
                table: "managed_instances");

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_SnowflakeWorkerId",
                table: "managed_instances",
                column: "SnowflakeWorkerId",
                unique: true);
        }
    }
}
