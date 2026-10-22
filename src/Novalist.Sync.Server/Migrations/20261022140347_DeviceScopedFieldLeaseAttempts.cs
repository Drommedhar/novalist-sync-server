using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novalist.Sync.Server.Migrations
{
    /// <inheritdoc />
    public partial class DeviceScopedFieldLeaseAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_field_lease_attempts",
                table: "field_lease_attempts");

            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                table: "field_lease_attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_field_lease_attempts",
                table: "field_lease_attempts",
                columns: new[] { "project_id", "device_id", "lease_id" });

            migrationBuilder.CreateIndex(
                name: "ix_field_lease_attempts_device_id",
                table: "field_lease_attempts",
                column: "device_id");

            migrationBuilder.AddForeignKey(
                name: "fk_field_lease_attempts_devices_device_id",
                table: "field_lease_attempts",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_field_lease_attempts_devices_device_id",
                table: "field_lease_attempts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_field_lease_attempts",
                table: "field_lease_attempts");

            migrationBuilder.DropIndex(
                name: "ix_field_lease_attempts_device_id",
                table: "field_lease_attempts");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "field_lease_attempts");

            migrationBuilder.AddPrimaryKey(
                name: "pk_field_lease_attempts",
                table: "field_lease_attempts",
                columns: new[] { "project_id", "lease_id" });
        }
    }
}
