using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basket.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BasketModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "baskets",
                schema: "basket",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baskets", x => x.id);
                    table.CheckConstraint("ck_baskets_exactly_one_owner", "(user_id IS NULL) <> (anonymous_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "basket_lines",
                schema: "basket",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    added_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_basket_lines_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "basket",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_basket_lines_basket_id_variant_id",
                schema: "basket",
                table: "basket_lines",
                columns: new[] { "basket_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_baskets_anonymous_id",
                schema: "basket",
                table: "baskets",
                column: "anonymous_id",
                unique: true,
                filter: "anonymous_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_baskets_user_id",
                schema: "basket",
                table: "baskets",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "basket_lines",
                schema: "basket");

            migrationBuilder.DropTable(
                name: "baskets",
                schema: "basket");
        }
    }
}
