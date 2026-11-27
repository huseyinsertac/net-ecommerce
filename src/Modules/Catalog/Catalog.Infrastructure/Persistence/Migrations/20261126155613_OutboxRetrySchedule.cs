using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxRetrySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_occurred_on_utc",
                schema: "catalog",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_on_utc",
                schema: "catalog",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_on_utc_occurred_on_utc",
                schema: "catalog",
                table: "outbox_messages",
                columns: new[] { "next_attempt_on_utc", "occurred_on_utc" },
                filter: "processed_on_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_next_attempt_on_utc_occurred_on_utc",
                schema: "catalog",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_on_utc",
                schema: "catalog",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_on_utc",
                schema: "catalog",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");
        }
    }
}
