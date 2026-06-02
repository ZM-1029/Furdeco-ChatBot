using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Furdeco_ChatBot.Migrations
{
    public partial class AddPhoneToAgentUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Agents",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Agents");
        }
    }
}
