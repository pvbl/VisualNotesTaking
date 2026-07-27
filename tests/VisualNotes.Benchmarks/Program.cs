using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

BenchmarkRunner.Run<DocumentBenchmarks>();
BenchmarkRunner.Run<ScreenshotStorageBenchmarks>();

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
