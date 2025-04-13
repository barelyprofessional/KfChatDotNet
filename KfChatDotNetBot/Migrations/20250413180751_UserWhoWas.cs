using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class UserWhoWas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsersWhoWere",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstOccurence = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LatestOccurence = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActivityType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersWhoWere", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsersWhoWere_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsersWhoWere_UserId",
                table: "UsersWhoWere",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsersWhoWere");
        }
    }
}
