using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727020000_CaptureMetadata")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class CaptureMetadataMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("MonitorDeviceName", "ScreenshotContexts", nullable: true);
        migrationBuilder.AddColumn<long>("WindowHandle", "ScreenshotContexts", nullable: true);
        migrationBuilder.AddColumn<int>("CaptureMode", "ScreenshotContexts", nullable: true);
        migrationBuilder.AddColumn<int>("PhysicalX", "ScreenshotContexts", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("PhysicalY", "ScreenshotContexts", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<uint>("DpiX", "ScreenshotContexts", nullable: false, defaultValue: 96u);
        migrationBuilder.AddColumn<uint>("DpiY", "ScreenshotContexts", nullable: false, defaultValue: 96u);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "MonitorDeviceName", "WindowHandle", "CaptureMode", "PhysicalX", "PhysicalY", "DpiX", "DpiY" })
            migrationBuilder.DropColumn(column, "ScreenshotContexts");
    }
}
