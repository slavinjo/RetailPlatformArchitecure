using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CartService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCartOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "carts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_carts_OwnerId",
                table: "carts",
                column: "OwnerId",
                unique: true,
                filter: "\"OwnerId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_carts_OwnerId",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "carts");
        }
    }
}
