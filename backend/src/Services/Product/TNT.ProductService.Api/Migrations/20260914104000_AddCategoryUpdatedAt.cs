using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TNT.ProductService.Api.Data;

#nullable disable

namespace TNT.ProductService.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ProductDbContext))]
    [Migration("20260914104000_AddCategoryUpdatedAt")]
    public partial class AddCategoryUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Categories",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Categories");
        }
    }
}
