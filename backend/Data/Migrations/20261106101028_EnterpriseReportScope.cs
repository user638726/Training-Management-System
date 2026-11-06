using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseReportScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeFingerprint",
                table: "ReportDeliveries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScopeFingerprint",
                table: "ReportDeliveries");
        }
    }
}
