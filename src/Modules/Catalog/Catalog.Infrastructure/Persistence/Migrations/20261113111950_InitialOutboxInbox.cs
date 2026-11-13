using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialOutboxInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "catalog",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                consumer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.consumer });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                content = table.Column<string>(type: "jsonb", nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_occurred_on_utc",
            schema: "catalog",
            table: "outbox_messages",
            column: "occurred_on_utc",
            filter: "processed_on_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "catalog");
    }
}
