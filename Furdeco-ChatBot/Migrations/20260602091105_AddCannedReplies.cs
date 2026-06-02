using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Furdeco_ChatBot.Migrations
{
    public partial class AddCannedReplies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CannedReplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CannedReplies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CannedReplies_SortOrder",
                table: "CannedReplies",
                column: "SortOrder");

            // Seed the original default replies so the team list isn't empty.
            var seededAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "CannedReplies",
                columns: new[] { "Id", "Text", "SortOrder", "CreatedAt" },
                values: new object[,]
                {
                    { new Guid("a1b1c1d1-0000-4000-8000-000000000001"), "Thanks for reaching out — I'll take a look right now.", 0, seededAt },
                    { new Guid("a1b1c1d1-0000-4000-8000-000000000002"), "Could you share a screenshot of what you're seeing?", 1, seededAt },
                    { new Guid("a1b1c1d1-0000-4000-8000-000000000003"), "I've escalated this to our engineering team and will follow up within the hour.", 2, seededAt },
                    { new Guid("a1b1c1d1-0000-4000-8000-000000000004"), "Your refund has been processed — please allow 3–5 business days.", 3, seededAt },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CannedReplies");
        }
    }
}
