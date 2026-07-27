using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727030000_CaptureEditing")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class CaptureEditing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("UserContext", "Screenshots", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("CaptureInstruction", "Screenshots", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("Tags", "Screenshots", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<int>("Importance", "Screenshots", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>("IncludeInDocument", "Screenshots", nullable: false, defaultValue: true);
        migrationBuilder.CreateTable("CaptureRevisions", table => new
        {
            Id = table.Column<Guid>(nullable: false), ScreenshotId = table.Column<Guid>(nullable: false), RevisionNumber = table.Column<int>(nullable: false),
            UserContext = table.Column<string>(nullable: false), CaptureInstruction = table.Column<string>(nullable: false), Tags = table.Column<string>(nullable: false),
            Importance = table.Column<int>(nullable: false), IncludeInDocument = table.Column<bool>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false),
            ModifiedAt = table.Column<DateTimeOffset>(nullable: false), Version = table.Column<long>(nullable: false), Status = table.Column<int>(nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_CaptureRevisions", x => x.Id); table.ForeignKey("FK_CaptureRevisions_Screenshots_ScreenshotId", x => x.ScreenshotId, "Screenshots", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_CaptureRevisions_ScreenshotId_RevisionNumber", "CaptureRevisions", new[] { "ScreenshotId", "RevisionNumber" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CaptureRevisions");
        foreach (var column in new[] { "UserContext", "CaptureInstruction", "Tags", "Importance", "IncludeInDocument" }) migrationBuilder.DropColumn(column, "Screenshots");
    }
}
