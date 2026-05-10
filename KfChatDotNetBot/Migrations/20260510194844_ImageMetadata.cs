using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class ImageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Images",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Images");
        }
    }
}
