using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisualNotes.Infrastructure.Persistence.Migrations;

public partial class EffectivePromptSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("EffectivePromptSnapshotJson", "AnalysisJobs", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("EffectivePromptSnapshotJson", "AnalysisJobs");
}
