using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetKickBot.Migrations
{
    /// <inheritdoc />
    public partial class Howlgg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HowlggBets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BetId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    Bet = table.Column<long>(type: "INTEGER", nullable: false),
                    Profit = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HowlggBets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HowlggBets");
        }
    }
}
