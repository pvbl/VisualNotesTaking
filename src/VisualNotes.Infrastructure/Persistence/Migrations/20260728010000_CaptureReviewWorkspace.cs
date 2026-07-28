using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260728010000_CaptureReviewWorkspace")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class CaptureReviewWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CourseModules",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                CourseId = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(nullable: false),
                Order = table.Column<int>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(nullable: false),
                Version = table.Column<long>(nullable: false),
                Status = table.Column<int>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseModules", x => x.Id);
                table.ForeignKey("FK_CourseModules_Courses_CourseId", x => x.CourseId, "Courses", "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_CourseModules_CourseId_Name", "CourseModules",
            new[] { "CourseId", "Name" }, unique: true);
        migrationBuilder.AddColumn<Guid>("CourseModuleId", "Sessions", nullable: true);
        migrationBuilder.AddColumn<string>("DisplayTitle", "Screenshots", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("DisplayTitle", "CaptureRevisions", nullable: false, defaultValue: "");
        migrationBuilder.CreateIndex("IX_Sessions_CourseModuleId", "Sessions", "CourseModuleId");
        migrationBuilder.Sql("""
            INSERT INTO CourseModules
                (Id, CourseId, Name, "Order", CreatedAt, ModifiedAt, Version, Status)
            SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' ||
                   substr(lower(hex(randomblob(2))),2) || '-' ||
                   substr('89ab',abs(random()) % 4 + 1,1) ||
                   substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
                   CourseId, Module, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, 0
            FROM Sessions
            WHERE CourseId IS NOT NULL AND trim(Module) <> ''
            GROUP BY CourseId, Module;
            UPDATE Sessions
            SET CourseModuleId = (
                SELECT CourseModules.Id FROM CourseModules
                WHERE CourseModules.CourseId = Sessions.CourseId
                  AND CourseModules.Name = Sessions.Module)
            WHERE CourseId IS NOT NULL AND trim(Module) <> '';
            """);
        // SQLite cannot add a foreign key to an existing table without rebuilding it.
        // The indexed nullable key is preserved and EF enforces the relationship for tracked writes.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Sessions_CourseModuleId", "Sessions");
        migrationBuilder.DropColumn("CourseModuleId", "Sessions");
        migrationBuilder.DropColumn("DisplayTitle", "Screenshots");
        migrationBuilder.DropColumn("DisplayTitle", "CaptureRevisions");
        migrationBuilder.DropTable("CourseModules");
    }
}
