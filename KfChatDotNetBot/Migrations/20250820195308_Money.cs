using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class Money : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gamblers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    RandomSeed = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TotalWagered = table.Column<decimal>(type: "TEXT", nullable: false),
                    NextVipLevelWagerRequirement = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gamblers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gamblers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Exclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GamblerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Expires = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exclusions_Gamblers_GamblerId",
                        column: x => x.GamblerId,
                        principalTable: "Gamblers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Perks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GamblerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PerkName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    PerkType = table.Column<int>(type: "INTEGER", nullable: false),
                    PerkTier = table.Column<int>(type: "INTEGER", nullable: true),
                    Payout = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Perks_Gamblers_GamblerId",
                        column: x => x.GamblerId,
                        principalTable: "Gamblers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GamblerId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventSource = table.Column<int>(type: "INTEGER", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimeUnixEpochSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Effect = table.Column<decimal>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    FromId = table.Column<int>(type: "INTEGER", nullable: true),
                    NewBalance = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Gamblers_FromId",
                        column: x => x.FromId,
                        principalTable: "Gamblers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Gamblers_GamblerId",
                        column: x => x.GamblerId,
                        principalTable: "Gamblers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Wagers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GamblerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimeUnixEpochSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    WagerAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    WagerEffect = table.Column<decimal>(type: "TEXT", nullable: false),
                    Game = table.Column<int>(type: "INTEGER", nullable: false),
                    Multiplier = table.Column<decimal>(type: "TEXT", nullable: false),
                    GameMeta = table.Column<string>(type: "TEXT", nullable: true),
                    IsComplete = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wagers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wagers_Gamblers_GamblerId",
                        column: x => x.GamblerId,
                        principalTable: "Gamblers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exclusions_GamblerId",
                table: "Exclusions",
                column: "GamblerId");

            migrationBuilder.CreateIndex(
                name: "IX_Gamblers_UserId",
                table: "Gamblers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Perks_GamblerId",
                table: "Perks",
                column: "GamblerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_FromId",
                table: "Transactions",
                column: "FromId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_GamblerId",
                table: "Transactions",
                column: "GamblerId");

            migrationBuilder.CreateIndex(
                name: "IX_Wagers_GamblerId",
                table: "Wagers",
                column: "GamblerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Exclusions");

            migrationBuilder.DropTable(
                name: "Perks");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Wagers");

            migrationBuilder.DropTable(
                name: "Gamblers");
        }
    }
}
