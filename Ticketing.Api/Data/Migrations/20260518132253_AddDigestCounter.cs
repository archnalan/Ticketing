using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigestCounters",
                columns: table => new
                {
                    Source = table.Column<int>(type: "int", nullable: false),
                    UnsentCount = table.Column<int>(type: "int", nullable: false),
                    LastSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestCounters", x => x.Source);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigestCounters");
        }
    }
}
