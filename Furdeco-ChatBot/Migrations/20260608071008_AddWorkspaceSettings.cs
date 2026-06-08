using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Furdeco_ChatBot.Migrations
{
    public partial class AddWorkspaceSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AutoAssignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxConcurrentChats = table.Column<int>(type: "integer", nullable: false),
                    ResponseTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxAssignAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSettings", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceSettings");
        }
    }
}
