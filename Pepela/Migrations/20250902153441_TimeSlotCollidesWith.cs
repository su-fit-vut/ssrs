using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class TimeSlotCollidesWith : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeSlotEntityTimeSlotEntity",
                columns: table => new
                {
                    CollidesWithId = table.Column<int>(type: "integer", nullable: false),
                    TimeSlotEntityId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeSlotEntityTimeSlotEntity", x => new { x.CollidesWithId, x.TimeSlotEntityId });
                    table.ForeignKey(
                        name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_CollidesWithId",
                        column: x => x.CollidesWithId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_TimeSlotEntityId",
                        column: x => x.TimeSlotEntityId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeSlotEntityTimeSlotEntity_TimeSlotEntityId",
                table: "TimeSlotEntityTimeSlotEntity",
                column: "TimeSlotEntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeSlotEntityTimeSlotEntity");
        }
    }
}
