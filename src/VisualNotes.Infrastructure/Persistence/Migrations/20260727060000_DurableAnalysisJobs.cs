using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace VisualNotes.Infrastructure.Persistence.Migrations;

[Migration("20260727060000_DurableAnalysisJobs")]
[DbContext(typeof(VisualNotesDbContext))]
public sealed class DurableAnalysisJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("Trigger", "AnalysisJobs", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("IdempotencyKey", "AnalysisJobs", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<int>("MaximumAttempts", "AnalysisJobs", nullable: false, defaultValue: 3);
        migrationBuilder.AddColumn<DateTimeOffset>("NextAttemptAt", "AnalysisJobs", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("StartedAt", "AnalysisJobs", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("CompletedAt", "AnalysisJobs", nullable: true);
        migrationBuilder.AddColumn<string>("LeaseOwner", "AnalysisJobs", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("LeaseExpiresAt", "AnalysisJobs", nullable: true);
        migrationBuilder.Sql("UPDATE AnalysisJobs SET IdempotencyKey = lower(hex(Id)) WHERE IdempotencyKey = '';");
        migrationBuilder.CreateTable("AnalysisJobAttempts", table => new
        {
            Id = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), ModifiedAt = table.Column<DateTimeOffset>(nullable: false),
            Version = table.Column<long>(nullable: false), Status = table.Column<int>(nullable: false), AnalysisJobId = table.Column<Guid>(nullable: false),
            AttemptNumber = table.Column<int>(nullable: false), ProviderProfileId = table.Column<Guid>(nullable: true), StartedAt = table.Column<DateTimeOffset>(nullable: false),
            FinishedAt = table.Column<DateTimeOffset>(nullable: true), Error = table.Column<string>(nullable: true), RetryAt = table.Column<DateTimeOffset>(nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_AnalysisJobAttempts", x => x.Id); table.ForeignKey("FK_AnalysisJobAttempts_AnalysisJobs_AnalysisJobId", x => x.AnalysisJobId, "AnalysisJobs", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_AnalysisJobs_IdempotencyKey", "AnalysisJobs", "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex("IX_AnalysisJobs_JobStatus_NextAttemptAt", "AnalysisJobs", new[] { "JobStatus", "NextAttemptAt" });
        migrationBuilder.CreateIndex("IX_AnalysisJobAttempts_AnalysisJobId_AttemptNumber", "AnalysisJobAttempts", new[] { "AnalysisJobId", "AttemptNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AnalysisJobAttempts");
        migrationBuilder.DropIndex("IX_AnalysisJobs_IdempotencyKey", "AnalysisJobs");
        migrationBuilder.DropIndex("IX_AnalysisJobs_JobStatus_NextAttemptAt", "AnalysisJobs");
        foreach (var column in new[] { "Trigger", "IdempotencyKey", "MaximumAttempts", "NextAttemptAt", "StartedAt", "CompletedAt", "LeaseOwner", "LeaseExpiresAt" }) migrationBuilder.DropColumn(column, "AnalysisJobs");
    }
}
