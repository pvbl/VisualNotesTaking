using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727040000_EffectivePromptSnapshot")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class EffectivePromptSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("EffectivePromptSnapshotJson", "AnalysisJobs", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("EffectivePromptSnapshotJson", "AnalysisJobs");
}
