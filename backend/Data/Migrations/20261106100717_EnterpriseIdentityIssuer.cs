using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseIdentityIssuer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalIssuer",
                table: "AppUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalIssuer",
                table: "AppUsers");
        }
    }
}
