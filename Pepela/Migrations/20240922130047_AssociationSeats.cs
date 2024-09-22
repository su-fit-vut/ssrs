using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class AssociationSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationEntityTimeSlotEntity");

            migrationBuilder.CreateTable(
                name: "ReservationTimeSlotAssociation",
                columns: table => new
                {
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    TakenTimeSlotSeats = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationTimeSlotAssociation", x => new { x.ReservationId, x.TimeSlotId });
                    table.ForeignKey(
                        name: "FK_ReservationTimeSlotAssociation_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReservationTimeSlotAssociation_TimeSlots_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationTimeSlotAssociation_TimeSlotId",
                table: "ReservationTimeSlotAssociation",
                column: "TimeSlotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationTimeSlotAssociation");

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
        }
    }
}
