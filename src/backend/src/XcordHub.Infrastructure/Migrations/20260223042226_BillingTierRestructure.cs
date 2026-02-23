using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XcordHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillingTierRestructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove billing fields from hub_users (billing is now per-instance)
            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "hub_users");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "hub_users");

            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "hub_users");

            // Add new billing columns to instance_billing
            migrationBuilder.AddColumn<int>(
                name: "FeatureTier",
                table: "instance_billing",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserCountTier",
                table: "instance_billing",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            // Migrate existing data: map old Tier values to new FeatureTier + UserCountTier
            // Old: Free=0, Basic=1, Pro=2
            // New: Free→(Chat=0, Tier10=10), Basic→(Audio=1, Tier50=50), Pro→(Video=2, Tier100=100)
            migrationBuilder.Sql("""
                UPDATE instance_billing SET "FeatureTier" = 0, "UserCountTier" = 10 WHERE "Tier" = 0;
                UPDATE instance_billing SET "FeatureTier" = 1, "UserCountTier" = 50 WHERE "Tier" = 1;
                UPDATE instance_billing SET "FeatureTier" = 2, "UserCountTier" = 100 WHERE "Tier" = 2;
                """);

            // Drop old Tier column
            migrationBuilder.DropColumn(
                name: "Tier",
                table: "instance_billing");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore old Tier column
            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "instance_billing",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Reverse data migration
            migrationBuilder.Sql("""
                UPDATE instance_billing SET "Tier" = 0 WHERE "FeatureTier" = 0 AND "UserCountTier" = 10;
                UPDATE instance_billing SET "Tier" = 1 WHERE "FeatureTier" = 1 AND "UserCountTier" = 50;
                UPDATE instance_billing SET "Tier" = 2 WHERE "FeatureTier" = 2 AND "UserCountTier" = 100;
                """);

            // Drop new columns
            migrationBuilder.DropColumn(
                name: "FeatureTier",
                table: "instance_billing");

            migrationBuilder.DropColumn(
                name: "UserCountTier",
                table: "instance_billing");

            // Restore hub_users billing fields
            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "hub_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "hub_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionTier",
                table: "hub_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
