using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novalist.Sync.Server.Migrations
{
    /// <inheritdoc />
    public partial class InvitationDeviceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_invitations_activated_device_id",
                table: "invitations",
                column: "activated_device_id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitations_devices_activated_device_id",
                table: "invitations",
                column: "activated_device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invitations_devices_activated_device_id",
                table: "invitations");

            migrationBuilder.DropIndex(
                name: "ix_invitations_activated_device_id",
                table: "invitations");
        }
    }
}
