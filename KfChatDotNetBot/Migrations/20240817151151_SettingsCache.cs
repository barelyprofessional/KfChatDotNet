using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class SettingsCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CacheDuration",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheDuration",
                table: "Settings");
        }
    }
}
