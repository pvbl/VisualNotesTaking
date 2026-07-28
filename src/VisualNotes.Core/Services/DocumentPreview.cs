using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VisualNotes.Core.Services;

public enum ExportScope { Document, Section, Selection }
public enum PreviewContentState { Ready, NeedsReview, MissingFile }

/// <summary>A semantic node prepared for review, ordering and export.</summary>
public sealed class PreviewItem
{
    internal PreviewItem(SemanticNode node, string? sectionKey, int order, PreviewContentState state)
    {
        Node = node; SectionKey = sectionKey; Order = order; State = state;
    }

    public SemanticNode Node { get; }
    public string StableKey => Node.StableKey;
    public string? SectionKey { get; }
    public int Order { get; internal set; }
    public bool IsIncluded { get; internal set; } = true;
    public bool IsSelected { get; set; }
    public PreviewContentState State { get; }
}

/// <summary>Editable preview projected directly from the semantic model.</summary>
public sealed class SemanticDocumentPreview
{
    private readonly List<PreviewItem> _items;

    public SemanticDocumentPreview(SemanticDocument document,
        IReadOnlyDictionary<string, PreviewContentState>? states = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Title = document.Title;
        _items = document.Sections.SelectMany(section =>
            new[] { new PreviewItem(section, section.StableKey, 0, State(section, states)) }
                .Concat(section.Nodes.Select((node, index) =>
                    new PreviewItem(node, section.StableKey, index + 1, State(node, states)))))
            .ToList();
        Items = new ReadOnlyCollection<PreviewItem>(_items);
    }

    public string Title { get; }
    public IReadOnlyList<PreviewItem> Items { get; }
    public bool HasWarnings => _items.Any(item => item.State != PreviewContentState.Ready);

    public void SetIncluded(IEnumerable<string> stableKeys, bool included)
    {
        var keys = stableKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var item in _items.Where(item => keys.Contains(item.StableKey))) item.IsIncluded = included;
    }

    public void Move(string stableKey, int targetIndex)
    {
        var item = _items.Single(item => item.StableKey == stableKey);
        if (item.Node.Type == SemanticNodeType.Section) throw new InvalidOperationException("Las secciones no se reordenan como elementos.");
        var siblings = _items.Where(candidate => candidate.SectionKey == item.SectionKey && candidate.Node.Type != SemanticNodeType.Section)
            .OrderBy(candidate => candidate.Order).ToList();
        siblings.Remove(item);
        siblings.Insert(Math.Clamp(targetIndex, 0, siblings.Count), item);
        for (var index = 0; index < siblings.Count; index++) siblings[index].Order = index + 1;
    }

    public SemanticDocument CreateDocument(ExportScope scope, string? sectionKey = null)
    {
        var sections = _items.Where(item => item.Node.Type == SemanticNodeType.Section && item.IsIncluded)
            .Where(item => scope != ExportScope.Section || item.StableKey == sectionKey)
            .Select(section => section.Node with
            {
                Nodes = _items.Where(item => item.SectionKey == section.StableKey && item.Node.Type != SemanticNodeType.Section && item.IsIncluded)
                    .Where(item => scope != ExportScope.Selection || item.IsSelected)
                    .OrderBy(item => item.Order).Select(item => item.Node).ToArray()
            })
            .Where(section => scope != ExportScope.Selection || section.Nodes.Count != 0)
            .ToArray();
        return new(Title, sections);
    }

    private static PreviewContentState State(SemanticNode node, IReadOnlyDictionary<string, PreviewContentState>? states) =>
        states?.GetValueOrDefault(node.StableKey) ?? PreviewContentState.Ready;
}

public sealed record ExportRecord(
    int Version, string Path, string Configuration, string ContentFingerprint,
    DateTimeOffset ExportedAt, long FileLength, DateTime LastWriteTimeUtc);

/// <summary>Produces stable fingerprints and detects semantic or exported-file changes.</summary>
public static class ExportChangeDetector
{
    public static string Fingerprint(SemanticDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static bool HasChanges(SemanticDocument document, ExportRecord? lastExport) =>
        lastExport is null || !string.Equals(Fingerprint(document), lastExport.ContentFingerprint, StringComparison.Ordinal);

    public static bool ExportedFileChanged(ExportRecord record) => !File.Exists(record.Path) ||
        new FileInfo(record.Path).Length != record.FileLength || File.GetLastWriteTimeUtc(record.Path) != record.LastWriteTimeUtc;
}
