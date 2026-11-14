using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CatalogModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "categories",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                icon = table.Column<string>(type: "text", nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                spec_schema = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_categories", x => x.id);
                table.ForeignKey(
                    name: "fk_categories_categories_parent_id",
                    column: x => x.parent_id,
                    principalSchema: "catalog",
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "products",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                brand = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                image_keys = table.Column<string[]>(type: "text[]", nullable: false),
                specs = table.Column<string>(type: "jsonb", nullable: false),
                option_axes = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                is_featured = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('english', coalesce(name,'')), 'A') || setweight(to_tsvector('english', coalesce(brand,'')), 'B') || setweight(to_tsvector('english', coalesce(description,'')), 'C')", stored: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_products", x => x.id);
                table.ForeignKey(
                    name: "fk_products_categories_category_id",
                    column: x => x.category_id,
                    principalSchema: "catalog",
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_variants",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "text", nullable: false),
                options = table.Column<string>(type: "jsonb", nullable: false),
                price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                stock_on_hand = table.Column<int>(type: "integer", nullable: false),
                stock_reserved = table.Column<int>(type: "integer", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_variants", x => x.id);
                table.CheckConstraint("ck_product_variants_stock", "stock_reserved >= 0 AND stock_reserved <= stock_on_hand AND stock_on_hand >= 0");
                table.ForeignKey(
                    name: "fk_product_variants_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "catalog",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_categories_parent_id",
            schema: "catalog",
            table: "categories",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_categories_slug",
            schema: "catalog",
            table: "categories",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_variants_price",
            schema: "catalog",
            table: "product_variants",
            column: "price");

        migrationBuilder.CreateIndex(
            name: "ix_product_variants_product_id",
            schema: "catalog",
            table: "product_variants",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_product_variants_sku",
            schema: "catalog",
            table: "product_variants",
            column: "sku",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_products_category_id",
            schema: "catalog",
            table: "products",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "ix_products_search_vector",
            schema: "catalog",
            table: "products",
            column: "search_vector")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "ix_products_slug",
            schema: "catalog",
            table: "products",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_products_specs",
            schema: "catalog",
            table: "products",
            column: "specs")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_products_status_category_id",
            schema: "catalog",
            table: "products",
            columns: new[] { "status", "category_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "product_variants",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "products",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "categories",
            schema: "catalog");
    }
}
