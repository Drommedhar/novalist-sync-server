using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novalist.Sync.Server.Migrations
{
    /// <inheritdoc />
    public partial class CollaborationAccess : Migration
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

            migrationBuilder.CreateTable(
                name: "field_lease_attempts",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_lease_attempts", x => new { x.project_id, x.device_id, x.lease_id });
                    table.ForeignKey(
                        name: "fk_field_lease_attempts_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_field_lease_attempts_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "field_leases",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_leases", x => new { x.project_id, x.resource_id });
                    table.ForeignKey(
                        name: "fk_field_leases_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_field_leases_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    certificate_hash = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitations_devices_activated_device_id",
                        column: x => x.activated_device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitations_users_redeemed_by",
                        column: x => x.redeemed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_device_authorizations",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    certificate = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_device_authorizations", x => new { x.project_id, x.device_id });
                    table.ForeignKey(
                        name: "fk_project_device_authorizations_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_device_authorizations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_epoch_key_grants",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    first_server_seq = table.Column<long>(type: "bigint", nullable: false),
                    grantor_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_epoch_key_grants", x => new { x.project_id, x.device_id, x.epoch });
                    table.ForeignKey(
                        name: "fk_project_epoch_key_grants_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_epoch_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_epoch = table.Column<long>(type: "bigint", nullable: false),
                    previous_server_seq = table.Column<long>(type: "bigint", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_epoch_stages", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_epoch_stages_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_epoch_device_grants",
                columns: table => new
                {
                    rotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_epoch_device_grants", x => new { x.rotation_id, x.device_id });
                    table.ForeignKey(
                        name: "fk_project_epoch_device_grants_project_epoch_stages_rotation_id",
                        column: x => x.rotation_id,
                        principalTable: "project_epoch_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_epoch_envelopes",
                columns: table => new
                {
                    rotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<long>(type: "bigint", nullable: false),
                    sender_seq = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_epoch_envelopes", x => new { x.rotation_id, x.index });
                    table.ForeignKey(
                        name: "fk_project_epoch_envelopes_project_epoch_stages_rotation_id",
                        column: x => x.rotation_id,
                        principalTable: "project_epoch_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_field_lease_attempts_device_id",
                table: "field_lease_attempts",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_lease_attempts_expires_at",
                table: "field_lease_attempts",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_field_leases_device_id",
                table: "field_leases",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_activated_device_id",
                table: "invitations",
                column: "activated_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_project_id",
                table: "invitations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_redeemed_by",
                table: "invitations",
                column: "redeemed_by");

            migrationBuilder.CreateIndex(
                name: "ix_project_device_authorizations_device_id",
                table: "project_device_authorizations",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_epoch_envelopes_rotation_id_sender_seq",
                table: "project_epoch_envelopes",
                columns: new[] { "rotation_id", "sender_seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_epoch_stages_project_id",
                table: "project_epoch_stages",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_lease_attempts");

            migrationBuilder.DropTable(
                name: "field_leases");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "project_device_authorizations");

            migrationBuilder.DropTable(
                name: "project_epoch_device_grants");

            migrationBuilder.DropTable(
                name: "project_epoch_envelopes");

            migrationBuilder.DropTable(
                name: "project_epoch_key_grants");

            migrationBuilder.DropTable(
                name: "project_epoch_stages");

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
