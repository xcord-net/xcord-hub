using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHdUpgradeToBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "InstanceKek",
                table: "instance_infrastructure",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "HdUpgrade",
                table: "instance_billing",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstanceKek",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "HdUpgrade",
                table: "instance_billing");
        }
    }
}
