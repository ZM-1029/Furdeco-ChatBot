using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Furdeco_ChatBot.Migrations
{
    public partial class AddOrderSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderSnapshot",
                table: "ChatSessions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderSnapshot",
                table: "ChatSessions");
        }
    }
}
