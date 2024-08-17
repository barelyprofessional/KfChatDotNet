using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class Chipsgg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChipsggBets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    Win = table.Column<bool>(type: "INTEGER", nullable: false),
                    Winnings = table.Column<double>(type: "REAL", nullable: false),
                    GameTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    Multiplier = table.Column<float>(type: "REAL", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    CurrencyPrice = table.Column<float>(type: "REAL", nullable: false),
                    BetId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChipsggBets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChipsggBets");
        }
    }
}
