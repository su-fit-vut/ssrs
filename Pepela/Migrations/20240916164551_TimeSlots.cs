using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class TimeSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PubQuizSeats",
                table: "Reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PubQuizTeamName",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SleepOver",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Start = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    TotalSeats = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ActivityId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeSlots_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReservationEntityTimeSlotEntity",
                columns: table => new
                {
                    AssociatedReservationsId = table.Column<int>(type: "integer", nullable: false),
                    AssociatedTimeSlotsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationEntityTimeSlotEntity", x => new { x.AssociatedReservationsId, x.AssociatedTimeSlotsId });
                    table.ForeignKey(
                        name: "FK_ReservationEntityTimeSlotEntity_Reservations_AssociatedRese~",
                        column: x => x.AssociatedReservationsId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReservationEntityTimeSlotEntity_TimeSlots_AssociatedTimeSlo~",
                        column: x => x.AssociatedTimeSlotsId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEntityTimeSlotEntity_AssociatedTimeSlotsId",
                table: "ReservationEntityTimeSlotEntity",
                column: "AssociatedTimeSlotsId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeSlots_ActivityId",
                table: "TimeSlots",
                column: "ActivityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationEntityTimeSlotEntity");

            migrationBuilder.DropTable(
                name: "TimeSlots");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropColumn(
                name: "PubQuizSeats",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PubQuizTeamName",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SleepOver",
                table: "Reservations");
        }
    }
}
