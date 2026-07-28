using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260728020000_SectionAcademicContext")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class SectionAcademicContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("CourseId", "Sections", nullable: true);
        migrationBuilder.AddColumn<Guid>("CourseModuleId", "Sections", nullable: true);
        migrationBuilder.CreateIndex("IX_Sections_CourseId", "Sections", "CourseId");
        migrationBuilder.CreateIndex("IX_Sections_CourseModuleId", "Sections", "CourseModuleId");

        // Existing sessions had one academic location. Preserve it on every existing
        // section; from this migration onward each section can choose a different one.
        migrationBuilder.Sql("""
            UPDATE Sections
            SET CourseId = (
                    SELECT Sessions.CourseId FROM Sessions
                    WHERE Sessions.Id = Sections.SessionId),
                CourseModuleId = (
                    SELECT Sessions.CourseModuleId FROM Sessions
                    WHERE Sessions.Id = Sections.SessionId)
            WHERE CourseId IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Sections_CourseId", "Sections");
        migrationBuilder.DropIndex("IX_Sections_CourseModuleId", "Sections");
        migrationBuilder.DropColumn("CourseId", "Sections");
        migrationBuilder.DropColumn("CourseModuleId", "Sections");
    }
}
