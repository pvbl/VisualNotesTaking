using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable
namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727000000_InitialCreate")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
CREATE TABLE Courses (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE Sessions (Id TEXT NOT NULL PRIMARY KEY, CourseId TEXT NULL REFERENCES Courses(Id), Name TEXT NOT NULL, StartedAt TEXT NOT NULL, EndedAt TEXT NULL, IsPaused INTEGER NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE Sections (Id TEXT NOT NULL PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, Title TEXT NOT NULL, Content TEXT NOT NULL, "Order" INTEGER NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE Screenshots (Id TEXT NOT NULL PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, SectionId TEXT NULL REFERENCES Sections(Id), CapturedAt TEXT NOT NULL, ProcessingStatus INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, PerceptualHash TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE ScreenshotImages (Id TEXT NOT NULL PRIMARY KEY, ScreenshotId TEXT NOT NULL UNIQUE REFERENCES Screenshots(Id) ON DELETE CASCADE, RelativePath TEXT NOT NULL, MediaType TEXT NOT NULL, ByteLength INTEGER NOT NULL, Sha256 TEXT NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE ScreenshotContexts (Id TEXT NOT NULL PRIMARY KEY, ScreenshotId TEXT NOT NULL UNIQUE REFERENCES Screenshots(Id) ON DELETE CASCADE, WindowTitle TEXT NULL, ApplicationName TEXT NULL, SourceUri TEXT NULL, MetadataJson TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE PromptProfiles (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Template TEXT NOT NULL, IsDefault INTEGER NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE ProviderProfiles (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Provider TEXT NOT NULL, Model TEXT NOT NULL, Endpoint TEXT NULL, ApiKeyReference TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE AnalysisJobs (Id TEXT NOT NULL PRIMARY KEY, ScreenshotId TEXT NOT NULL REFERENCES Screenshots(Id) ON DELETE CASCADE, PromptProfileId TEXT NULL REFERENCES PromptProfiles(Id), ProviderProfileId TEXT NULL REFERENCES ProviderProfiles(Id), JobStatus INTEGER NOT NULL, Attempts INTEGER NOT NULL, Error TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE CaptureAnalyses (Id TEXT NOT NULL PRIMARY KEY, AnalysisJobId TEXT NOT NULL UNIQUE REFERENCES AnalysisJobs(Id) ON DELETE CASCADE, ExtractedText TEXT NOT NULL, Summary TEXT NULL, RawResultRelativePath TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE VisualRegions (Id TEXT NOT NULL PRIMARY KEY, CaptureAnalysisId TEXT NOT NULL REFERENCES CaptureAnalyses(Id) ON DELETE CASCADE, X INTEGER NOT NULL, Y INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, Label TEXT NULL, CropRelativePath TEXT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE GeneratedNotes (Id TEXT NOT NULL PRIMARY KEY, SectionId TEXT NOT NULL REFERENCES Sections(Id) ON DELETE CASCADE, CaptureAnalysisId TEXT NULL REFERENCES CaptureAnalyses(Id), Content TEXT NOT NULL, Format TEXT NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE ExportRecords (Id TEXT NOT NULL PRIMARY KEY, SessionId TEXT NOT NULL, Format TEXT NOT NULL, RelativePath TEXT NOT NULL, ExportedAt TEXT NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE TABLE Settings (Id TEXT NOT NULL PRIMARY KEY, Key TEXT NOT NULL, JsonValue TEXT NOT NULL, IsSecret INTEGER NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL, Version INTEGER NOT NULL, Status INTEGER NOT NULL);
CREATE UNIQUE INDEX IX_Settings_Key ON Settings(Key);
CREATE INDEX IX_Sessions_CourseId ON Sessions(CourseId);
CREATE INDEX IX_Sections_SessionId ON Sections(SessionId);
CREATE INDEX IX_Screenshots_SessionId ON Screenshots(SessionId);
CREATE INDEX IX_Screenshots_SectionId ON Screenshots(SectionId);
CREATE INDEX IX_Screenshots_CapturedAt ON Screenshots(CapturedAt);
CREATE INDEX IX_Screenshots_ProcessingStatus ON Screenshots(ProcessingStatus);
CREATE INDEX IX_Screenshots_PerceptualHash ON Screenshots(PerceptualHash);
""");
    }
    protected override void Down(MigrationBuilder m)
    {
        foreach (var table in new[] { "Settings", "ExportRecords", "GeneratedNotes", "VisualRegions", "CaptureAnalyses", "AnalysisJobs", "ProviderProfiles", "PromptProfiles", "ScreenshotContexts", "ScreenshotImages", "Screenshots", "Sections", "Sessions", "Courses" }) m.DropTable(table);
    }
}
