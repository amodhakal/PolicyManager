using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManager.Migrations
{
    /// <summary>
    ///     First phase of protecting policyholder email addresses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two phases, deliberately, because the data cannot be converted in T-SQL. AES-GCM has no
    ///     SQL Server equivalent, and the blind index is an HMAC rather than a plain hash precisely
    ///     because a plain hash of an email address is reversible by brute force. So the existing rows
    ///     are converted by <c>PiiBackfillService</c>, which the application runs on start-up, and the
    ///     NOT NULL constraint and the unique index on the blind index arrive in the second phase,
    ///     <c>EnforcePiiBlindIndex</c>.
    ///     </para>
    ///     <para>
    ///     Doing it in one migration would mean creating the unique index over a column full of NULLs,
    ///     which succeeds but enforces nothing, or over a default empty string, which fails on the
    ///     second existing row. The first is a silent loss of the duplicate-address guarantee the API
    ///     depends on; the second is a failed deployment.
    ///     </para>
    ///     <para>
    ///     Everything here is additive. <c>EmailHash</c> is nullable and the old unique index on the
    ///     plaintext address is left in place, so a deployment of this migration on its own still
    ///     behaves. New rows written by the application carry both the ciphertext and the blind index
    ///     from the moment it is deployed, and the service checks duplicates against the blind index
    ///     in the meantime.
    ///     </para>
    /// </remarks>
    public partial class PiiProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Widened for ciphertext: base64 of (12-byte nonce + the UTF-8 address + a 16-byte
            // authentication tag). A 254-character address is 254 bytes of UTF-8, so the stored form
            // is 282 bytes and 376 characters of base64; 1024 leaves generous headroom and still
            // bounds the column.
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "PolicyHolders",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<string>(
                name: "EmailHash",
                table: "PolicyHolders",
                type: "nvarchar(450)",
                maxLength: 450,
                // Nullable on purpose: the backfill fills it, and NOT NULL is applied in the second
                // phase once every row has one.
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PiiAccessAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyHolderId = table.Column<int>(type: "int", nullable: false),
                    ReadBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReadByRoles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Disclosed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PiiAccessAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PiiAccessAudits_Holder_OccurredAt",
                table: "PiiAccessAudits",
                columns: new[] { "PolicyHolderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PiiAccessAudits_OccurredAt",
                table: "PiiAccessAudits",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders",
                column: "EmailHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PiiAccessAudits");

            migrationBuilder.DropIndex(
                name: "IX_PolicyHolder_EmailHash",
                table: "PolicyHolders");

            migrationBuilder.DropColumn(
                name: "EmailHash",
                table: "PolicyHolders");

            // Narrowing back is not a loss: the original width is an upper bound, and any address
            // that fitted it is unchanged by the round trip through ciphertext.
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "PolicyHolders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024);
        }
    }
}
