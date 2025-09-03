using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class TimeSlotReservationMinMaxDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "ReserveAfter",
                table: "TimeSlots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "ReserveBefore",
                table: "TimeSlots",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReserveAfter",
                table: "TimeSlots");

            migrationBuilder.DropColumn(
                name: "ReserveBefore",
                table: "TimeSlots");
        }
    }
}
