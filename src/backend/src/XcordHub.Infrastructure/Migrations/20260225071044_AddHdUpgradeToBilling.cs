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
                name: "DockerKekSecretId",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "DatabaseUsername",
                table: "instance_infrastructure");

            migrationBuilder.DropColumn(
                name: "HdUpgrade",
                table: "instance_billing");
        }
    }
}
