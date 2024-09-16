using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class TimeSlotAlwaysConsumeOnePerReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AlwaysConsumeOnePerReservation",
                table: "TimeSlots",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlwaysConsumeOnePerReservation",
                table: "TimeSlots");
        }
    }
}
