using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

BenchmarkRunner.Run<DocumentBenchmarks>();
BenchmarkRunner.Run<ScreenshotStorageBenchmarks>();
BenchmarkRunner.Run<CaptureListBenchmarks>();

[MemoryDiagnoser]
public class DocumentBenchmarks
{
    private readonly NoteSession _session = new() { Sections = Enumerable.Range(0, 100).Select(i => new NoteSection { Title = $"Section {i}", Content = new string('x', 200), Order = i }).ToList() };

    [Benchmark]
    public string ComposeMarkdown() => string.Join("\n", _session.Sections.OrderBy(x => x.Order).Select(x => $"## {x.Title}\n{x.Content}"));
}

[MemoryDiagnoser]
public class ScreenshotStorageBenchmarks
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visualnotes-benchmarks");
    private byte[] _source = [];
    [Params(1920, 2560, 3840)] public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Directory.CreateDirectory(_root);
        var height = Width * 9 / 16;
        using var image = new Image<Rgba32>(Width, height, new Rgba32(35, 90, 150));
        using var stream = new MemoryStream(); image.SaveAsPng(stream); _source = stream.ToArray();
    }

    [Benchmark]
    public Task<StoredScreenshot> Store1080p1440pOr4K() => new ScreenshotStorageService(_root, 0).StoreAsync(
        new(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(_source)));

    [GlobalCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

[MemoryDiagnoser]
public class CaptureListBenchmarks
{
    private CaptureLibrary _library = null!;
    [GlobalSetup]
    public void Setup() => _library = new CaptureLibrary(Enumerable.Range(0, 1_000).Select(i => new Screenshot
    {
        CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i), Tags = i % 4 == 0 ? "diagram" : "lecture",
        ProcessingStatus = i % 10 == 0 ? ScreenshotStatus.NeedsReview : ScreenshotStatus.Ready,
        Image = new ScreenshotImage { RelativePath = $"captures/{i}.png", ByteLength = 12_000_000 }
    }));

    [Benchmark(Description = "Carga y filtro de 1.000 capturas")]
    public IReadOnlyList<Screenshot> Filter() => _library.Query(new(Tag: "diagram", Review: ReviewFilter.Pending));

    [Benchmark(Description = "Scroll virtualizado (40 ventanas)")]
    public int ScrollWindows() => Enumerable.Range(0, 40).Sum(page => _library.Captures.Skip(page * 20).Take(30).Count());
}
