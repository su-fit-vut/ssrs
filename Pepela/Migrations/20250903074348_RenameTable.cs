using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pepela.Migrations
{
    /// <inheritdoc />
    public partial class RenameTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_CollidesWithId",
                table: "TimeSlotEntityTimeSlotEntity");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_TimeSlotEntityId",
                table: "TimeSlotEntityTimeSlotEntity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeSlotEntityTimeSlotEntity",
                table: "TimeSlotEntityTimeSlotEntity");

            migrationBuilder.RenameTable(
                name: "TimeSlotEntityTimeSlotEntity",
                newName: "TimeSlotCollisions");

            migrationBuilder.RenameIndex(
                name: "IX_TimeSlotEntityTimeSlotEntity_TimeSlotEntityId",
                table: "TimeSlotCollisions",
                newName: "IX_TimeSlotCollisions_TimeSlotEntityId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeSlotCollisions",
                table: "TimeSlotCollisions",
                columns: new[] { "CollidesWithId", "TimeSlotEntityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TimeSlotCollisions_TimeSlots_CollidesWithId",
                table: "TimeSlotCollisions",
                column: "CollidesWithId",
                principalTable: "TimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeSlotCollisions_TimeSlots_TimeSlotEntityId",
                table: "TimeSlotCollisions",
                column: "TimeSlotEntityId",
                principalTable: "TimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeSlotCollisions_TimeSlots_CollidesWithId",
                table: "TimeSlotCollisions");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeSlotCollisions_TimeSlots_TimeSlotEntityId",
                table: "TimeSlotCollisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TimeSlotCollisions",
                table: "TimeSlotCollisions");

            migrationBuilder.RenameTable(
                name: "TimeSlotCollisions",
                newName: "TimeSlotEntityTimeSlotEntity");

            migrationBuilder.RenameIndex(
                name: "IX_TimeSlotCollisions_TimeSlotEntityId",
                table: "TimeSlotEntityTimeSlotEntity",
                newName: "IX_TimeSlotEntityTimeSlotEntity_TimeSlotEntityId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TimeSlotEntityTimeSlotEntity",
                table: "TimeSlotEntityTimeSlotEntity",
                columns: new[] { "CollidesWithId", "TimeSlotEntityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_CollidesWithId",
                table: "TimeSlotEntityTimeSlotEntity",
                column: "CollidesWithId",
                principalTable: "TimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeSlotEntityTimeSlotEntity_TimeSlots_TimeSlotEntityId",
                table: "TimeSlotEntityTimeSlotEntity",
                column: "TimeSlotEntityId",
                principalTable: "TimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
