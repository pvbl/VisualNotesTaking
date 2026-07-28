using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727010000_SessionWorkspace")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class SessionWorkspace : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("Module", "Sessions", nullable: false, defaultValue: "");
        m.AddColumn<string>("Topic", "Sessions", nullable: false, defaultValue: "");
        m.AddColumn<string>("Professor", "Sessions", nullable: true);
        m.AddColumn<string>("Language", "Sessions", nullable: false, defaultValue: "Español");
        m.AddColumn<string>("WorkingFolder", "Sessions", nullable: false, defaultValue: "");
        m.AddColumn<string>("PlannedDocumentName", "Sessions", nullable: false, defaultValue: "");
        m.AddColumn<string>("InstructionTemplate", "Sessions", nullable: false, defaultValue: "");
        m.AddColumn<Guid>("ActiveSectionId", "Sessions", nullable: true);
        m.AddColumn<int>("ProcessingStatus", "Sessions", nullable: false, defaultValue: 0);
        m.AddColumn<DateTimeOffset>("LastExportedAt", "Sessions", nullable: true);
        m.AddColumn<string>("Description", "Sections", nullable: false, defaultValue: "");
        m.AddColumn<Guid>("ParentSectionId", "Sections", nullable: true);
        m.CreateIndex("IX_Sessions_ActiveSectionId", "Sessions", "ActiveSectionId");
        m.CreateIndex("IX_Sections_ParentSectionId", "Sections", "ParentSectionId");
        if (!IsSqlite(m))
        {
            m.AddForeignKey("FK_Sessions_Sections_ActiveSectionId", "Sessions", "ActiveSectionId", "Sections", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            m.AddForeignKey("FK_Sections_Sections_ParentSectionId", "Sections", "ParentSectionId", "Sections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }
    }

    protected override void Down(MigrationBuilder m)
    {
        if (!IsSqlite(m))
        {
            m.DropForeignKey("FK_Sessions_Sections_ActiveSectionId", "Sessions");
            m.DropForeignKey("FK_Sections_Sections_ParentSectionId", "Sections");
        }
        m.DropIndex("IX_Sessions_ActiveSectionId", "Sessions");
        m.DropIndex("IX_Sections_ParentSectionId", "Sections");
        foreach (var column in new[] { "Module", "Topic", "Professor", "Language", "WorkingFolder", "PlannedDocumentName", "InstructionTemplate", "ActiveSectionId", "ProcessingStatus", "LastExportedAt" }) m.DropColumn(column, "Sessions");
        m.DropColumn("Description", "Sections"); m.DropColumn("ParentSectionId", "Sections");
    }

    private static bool IsSqlite(MigrationBuilder migrationBuilder) =>
        string.Equals(migrationBuilder.ActiveProvider, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);
}
