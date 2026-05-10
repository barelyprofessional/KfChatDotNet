using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KfChatDotNetBot.Migrations
{
    /// <inheritdoc />
    public partial class ImageTagList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagList",
                table: "Images",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TagList",
                table: "Images");
        }
    }
}
