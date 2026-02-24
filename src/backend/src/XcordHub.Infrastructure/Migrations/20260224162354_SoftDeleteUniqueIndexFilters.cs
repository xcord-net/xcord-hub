using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteUniqueIndexFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_instances_Domain",
                table: "managed_instances");

            migrationBuilder.DropIndex(
                name: "IX_hub_users_EmailHash",
                table: "hub_users");

            migrationBuilder.DropIndex(
                name: "IX_hub_users_Username",
                table: "hub_users");

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_Domain",
                table: "managed_instances",
                column: "Domain",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_hub_users_EmailHash",
                table: "hub_users",
                column: "EmailHash",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_hub_users_Username",
                table: "hub_users",
                column: "Username",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_instances_Domain",
                table: "managed_instances");

            migrationBuilder.DropIndex(
                name: "IX_hub_users_EmailHash",
                table: "hub_users");

            migrationBuilder.DropIndex(
                name: "IX_hub_users_Username",
                table: "hub_users");

            migrationBuilder.CreateIndex(
                name: "IX_managed_instances_Domain",
                table: "managed_instances",
                column: "Domain",
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
        }
    }
}
