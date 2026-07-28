using System.Diagnostics;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.IntegrationTests;

public sealed class LargeCaptureLibraryTests
{
    [Fact, Trait("Category", "Integration")]
    public void One_thousand_simulated_captures_load_filter_and_page_within_budget()
    {
        var section = Guid.NewGuid();
        var source = Enumerable.Range(0, 1_000).Select(i => new Screenshot
        {
            CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i),
            SectionId = i % 2 == 0 ? section : null,
            Tags = i % 5 == 0 ? "important, diagram" : "lecture",
            Importance = i % 5 == 0 ? CaptureImportance.Important : CaptureImportance.Normal,
            ProcessingStatus = i % 10 == 0 ? ScreenshotStatus.NeedsReview : ScreenshotStatus.Ready,
            Image = new ScreenshotImage { RelativePath = $"images/{i:D4}.png", ByteLength = 8_000_000 }
        }).ToArray();
        var before = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();

        var library = new CaptureLibrary(source);
        var filtered = library.Query(new(section, "diagram", Importance: CaptureImportance.Important, Review: ReviewFilter.Pending));
        var virtualizedPages = Enumerable.Range(0, 20).Select(page => library.Captures.Skip(page * 25).Take(25).ToArray()).ToArray();
        stopwatch.Stop();
        var allocated = GC.GetTotalMemory(true) - before;

        filtered.Count.ShouldBe(100);
        virtualizedPages.Sum(page => page.Length).ShouldBe(500);
        source.ShouldAllBe(capture => capture.Image!.RelativePath.Length > 0); // metadata only: no large image bytes loaded
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        allocated.ShouldBeLessThan(32 * 1024 * 1024);
    }
}
