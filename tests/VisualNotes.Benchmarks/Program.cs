using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using VisualNotes.Core.Models;

BenchmarkRunner.Run<DocumentBenchmarks>();

[MemoryDiagnoser]
public class DocumentBenchmarks
{
    private readonly NoteSession _session = new() { Sections = Enumerable.Range(0, 100).Select(i => new NoteSection { Title = $"Section {i}", Content = new string('x', 200), Order = i }).ToList() };

    [Benchmark]
    public string ComposeMarkdown() => string.Join("\n", _session.Sections.OrderBy(x => x.Order).Select(x => $"## {x.Title}\n{x.Content}"));
}
