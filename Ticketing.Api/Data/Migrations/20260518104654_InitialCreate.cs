using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SpanId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ExceptionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FeedbackId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReporterEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ReporterName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DateModified = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_FeedbackId",
                table: "Tickets",
                column: "FeedbackId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Fingerprint",
                table: "Tickets",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Source_Status",
                table: "Tickets",
                columns: new[] { "Source", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tickets");
        }
    }
}
