using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Furdeco_ChatBot.Migrations
{
    public partial class AddSlaBreachNotified : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SlaBreachNotified",
                table: "Tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlaBreachNotified",
                table: "Tickets");
        }
    }
}
