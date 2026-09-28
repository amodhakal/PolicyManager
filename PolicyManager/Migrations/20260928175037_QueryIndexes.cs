using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManager.Migrations
{
    /// <inheritdoc />
    public partial class QueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PolicyHolders_LastName_Id",
                table: "PolicyHolders",
                columns: new[] { "LastName", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyHolderId_Id",
                table: "Policies",
                columns: new[] { "PolicyHolderId", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status_Id",
                table: "Policies",
                columns: new[] { "Status", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Claimed",
                table: "OutboxMessages",
                column: "LockedUntil",
                filter: "[LockToken] IS NOT NULL");
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "CreatedAt",
                filter: "[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
            migrationBuilder.CreateIndex(
                name: "IX_Claims_Coverage",
                table: "Claims",
                columns: new[] { "PolicyId", "Amount" },
                filter: "[Status] <> 2");
            migrationBuilder.CreateIndex(
                name: "IX_Claims_FiledAt_Id",
                table: "Claims",
                columns: new[] { "FiledAt", "Id" });

            // Dropped only once the replacements exist, so a query running during the migration
            // never finds itself with no supporting index at all.
            migrationBuilder.DropIndex(
                name: "IX_Policies_PolicyHolderId",
                table: "Policies");
            migrationBuilder.DropIndex(
                name: "IX_Policies_Status",
                table: "Policies");
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt",
                table: "OutboxMessages");
            migrationBuilder.DropIndex(
                name: "IX_Claims_PolicyId",
                table: "Claims");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolicyHolders_LastName_Id",
                table: "PolicyHolders");
            migrationBuilder.DropIndex(
                name: "IX_Policies_PolicyHolderId_Id",
                table: "Policies");
            migrationBuilder.DropIndex(
                name: "IX_Policies_Status_Id",
                table: "Policies");
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Claimed",
                table: "OutboxMessages");
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");
            migrationBuilder.DropIndex(
                name: "IX_Claims_Coverage",
                table: "Claims");
            migrationBuilder.DropIndex(
                name: "IX_Claims_FiledAt_Id",
                table: "Claims");
            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyHolderId",
                table: "Policies",
                column: "PolicyHolderId");
            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status",
                table: "Policies",
                column: "Status");
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "NextAttemptAt" },
                filter: "[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt",
                table: "OutboxMessages",
                column: "ProcessedAt");
            migrationBuilder.CreateIndex(
                name: "IX_Claims_PolicyId",
                table: "Claims",
                column: "PolicyId");
        }
    }
}
