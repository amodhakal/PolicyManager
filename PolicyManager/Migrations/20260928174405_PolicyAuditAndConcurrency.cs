using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManager.Migrations
{
    /// <inheritdoc />
    public partial class PolicyAuditAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CreatedAt and CreatedBy are added nullable so the backfill below has somewhere to put
            // a truthful value, then tightened. Adding them straight away as NOT NULL would stamp
            // every pre-existing row with the CLR default of 0001-01-01, which reads as a real date
            // in every audit report and is worse than admitting the row predates auditing.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Policies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Policies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Policies",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Policies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Policies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Claims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Claims",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Claims",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Claims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Claims",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // PolicyHolder already carried CreatedAt, so only its new columns need adding.
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PolicyHolders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PolicyHolders",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PolicyHolders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "PolicyHolders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Backfilled from the closest real date each row already carries rather than from "now",
            // which would say every policy was created at the moment of this migration. "system" is
            // the same token the runtime writes for an unattributed change, so backfilled rows are
            // distinguishable from unaudited ones without inventing an author.
            migrationBuilder.Sql(
                "UPDATE [Policies] SET [CreatedAt] = [StartDate], [CreatedBy] = 'system' " +
                "WHERE [CreatedAt] IS NULL;");

            migrationBuilder.Sql(
                "UPDATE [Claims] SET [CreatedAt] = [FiledAt], [CreatedBy] = 'system' " +
                "WHERE [CreatedAt] IS NULL;");

            migrationBuilder.Sql(
                "UPDATE [PolicyHolders] SET [CreatedBy] = 'system' WHERE [CreatedBy] IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Policies",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Claims",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PolicyHolders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PolicyHolders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PolicyHolders");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "PolicyHolders");
        }
    }
}
