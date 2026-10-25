using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novalist.Sync.Server.Migrations
{
    /// <inheritdoc />
    public partial class VerifiedDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "linked_by_device_id",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "linking_signature",
                table: "devices",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "receiving_public_key",
                table: "devices",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "signing_public_key",
                table: "devices",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex("ix_devices_signing_public_key", "devices", "signing_public_key", unique: true);
            migrationBuilder.CreateIndex("ix_devices_receiving_public_key", "devices", "receiving_public_key", unique: true);
            migrationBuilder.CreateIndex("ix_devices_linked_by_device_id", "devices", "linked_by_device_id");
            migrationBuilder.AddForeignKey("fk_devices_devices_linked_by_device_id", "devices", "linked_by_device_id",
                "devices", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("fk_devices_devices_linked_by_device_id", "devices");
            migrationBuilder.DropIndex("ix_devices_linked_by_device_id", "devices");
            migrationBuilder.DropIndex("ix_devices_signing_public_key", "devices");
            migrationBuilder.DropIndex("ix_devices_receiving_public_key", "devices");
            migrationBuilder.DropColumn(
                name: "linked_by_device_id",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "linking_signature",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "receiving_public_key",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "signing_public_key",
                table: "devices");
        }
    }
}
