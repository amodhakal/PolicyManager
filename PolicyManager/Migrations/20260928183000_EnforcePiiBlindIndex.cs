using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManager.Migrations
{
    /// <summary>
    ///     Second phase of protecting policyholder email addresses: tighten the blind index.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Do not apply this as part of the initial deployment.</b> It is written as a migration
    ///     so it is reviewed and versioned with the rest, but it is applied deliberately, after
    ///     <c>PiiBackfillService</c> has reported the backfill complete:
    ///     </para>
    ///     <code>
    ///     dotnet ef database update PiiProtection
    ///     # ... watch the logs for "The PII backfill is complete" ...
    ///     dotnet ef database update EnforcePiiBlindIndex
    ///     </code>
    ///     <para>
    ///     Applying it before the backfill has finished either fails outright — the NOT NULL
    ///     alteration trips over a row that still has no index — or, if the rows happen to be
    ///     converted, succeeds on rows whose index was computed under a key that is about to change.
    ///     The database has no way to tell the difference, which is why the ordering is a documented
    ///     operator step rather than something the migration chain can enforce.
    ///     </para>
    /// </remarks>
    public partial class EnforcePiiBlindIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Any row the backfill could not convert is a hard stop, and deliberately so: an address
            // left in plaintext with a NULL index is exactly the state this whole feature exists to
            // end, and quietly skipping those rows would leave them there permanently.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [PolicyHolders] WHERE [EmailHash] IS NULL)
                BEGIN
                    THROW 51000, 'Policyholders remain without a blind index. Run the PII backfill to completion before applying this migration.', 1;
                END");

            migrationBuilder.DropIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders");

            migrationBuilder.AlterColumn<string>(
                name: "EmailHash",
                table: "PolicyHolders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            // Now safe, and now the only thing enforcing the constraint. From here a duplicate
            // address is a database error rather than a race in the service.
            migrationBuilder.CreateIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders",
                column: "EmailHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders");

            migrationBuilder.AlterColumn<string>(
                name: "EmailHash",
                table: "PolicyHolders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders",
                column: "EmailHash");
        }
    }
}
