using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727050000_StructuredAnalysisResponse")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class StructuredAnalysisResponse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("OriginalResponseJson", "CaptureAnalyses", nullable: true);
        migrationBuilder.AddColumn<string>("NormalizedResponseJson", "CaptureAnalyses", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("OriginalResponseJson", "CaptureAnalyses");
        migrationBuilder.DropColumn("NormalizedResponseJson", "CaptureAnalyses");
    }
}
