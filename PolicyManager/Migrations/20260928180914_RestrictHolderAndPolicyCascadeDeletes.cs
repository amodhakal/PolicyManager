using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManager.Migrations
{
    /// <inheritdoc />
    public partial class RestrictHolderAndPolicyCascadeDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Claims_Policies_PolicyId",
                table: "Claims");

            migrationBuilder.DropForeignKey(
                name: "FK_Policies_PolicyHolders_PolicyHolderId",
                table: "Policies");

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_Policies_PolicyId",
                table: "Claims",
                column: "PolicyId",
                principalTable: "Policies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Policies_PolicyHolders_PolicyHolderId",
                table: "Policies",
                column: "PolicyHolderId",
                principalTable: "PolicyHolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Claims_Policies_PolicyId",
                table: "Claims");

            migrationBuilder.DropForeignKey(
                name: "FK_Policies_PolicyHolders_PolicyHolderId",
                table: "Policies");

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_Policies_PolicyId",
                table: "Claims",
                column: "PolicyId",
                principalTable: "Policies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Policies_PolicyHolders_PolicyHolderId",
                table: "Policies",
                column: "PolicyHolderId",
                principalTable: "PolicyHolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
