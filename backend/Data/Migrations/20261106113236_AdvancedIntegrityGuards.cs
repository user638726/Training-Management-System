using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedIntegrityGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HrSyncSchedules_IsActive",
                table: "HrSyncSchedules",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HrSyncSchedules_IsActive",
                table: "HrSyncSchedules");
        }
    }
}
