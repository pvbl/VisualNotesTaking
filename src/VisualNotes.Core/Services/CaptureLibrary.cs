using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public enum ReviewFilter { All, Pending, Reviewed }

public sealed record CaptureFilter(
    Guid? SectionId = null,
    string? Tag = null,
    ScreenshotStatus? Status = null,
    CaptureImportance? Importance = null,
    ReviewFilter Review = ReviewFilter.All);

/// <summary>
/// Pure, UI-independent capture list operations. A single snapshot is recorded for
/// each batch so destructive multi-selection operations can be undone atomically.
/// </summary>
public sealed class CaptureLibrary
{
    private readonly List<Screenshot> _captures;
    private readonly Stack<IReadOnlyList<CaptureState>> _undo = new();

    public CaptureLibrary(IEnumerable<Screenshot> captures) =>
        _captures = captures.OrderBy(capture => capture.CapturedAt).ToList();

    public IReadOnlyList<Screenshot> Captures => _captures;
    public bool CanUndo => _undo.Count > 0;

    public IReadOnlyList<Screenshot> Query(CaptureFilter filter) => _captures
        .Where(capture => filter.SectionId is null || capture.SectionId == filter.SectionId)
        .Where(capture => filter.Status is null || capture.ProcessingStatus == filter.Status)
        .Where(capture => filter.Importance is null || capture.Importance == filter.Importance)
        .Where(capture => string.IsNullOrWhiteSpace(filter.Tag) || Tags(capture).Contains(filter.Tag, StringComparer.OrdinalIgnoreCase))
        .Where(capture => filter.Review switch
        {
            ReviewFilter.Pending => capture.ProcessingStatus == ScreenshotStatus.NeedsReview,
            ReviewFilter.Reviewed => capture.ProcessingStatus != ScreenshotStatus.NeedsReview,
            _ => true
        })
        .ToArray();

    public void Reorder(IReadOnlyCollection<Guid> ids, int destinationIndex)
    {
        Save(ids);
        var moving = _captures.Where(capture => ids.Contains(capture.Id)).ToArray();
        _captures.RemoveAll(capture => ids.Contains(capture.Id));
        _captures.InsertRange(Math.Clamp(destinationIndex, 0, _captures.Count), moving);
    }

    public void MoveToSection(IReadOnlyCollection<Guid> ids, Guid? sectionId) =>
        Mutate(ids, capture => capture.SectionId = sectionId);

    public void Exclude(IReadOnlyCollection<Guid> ids) => Mutate(ids, capture =>
    {
        capture.IncludeInDocument = false;
        capture.ProcessingStatus = ScreenshotStatus.Excluded;
    });

    public void ResolveDuplicate(Guid retainedId, IReadOnlyCollection<Guid> duplicateIds, DuplicateResolution resolution)
    {
        if (_captures.All(capture => capture.Id != retainedId)) throw new ArgumentException("Retained capture was not found.", nameof(retainedId));
        if (duplicateIds.Contains(retainedId)) throw new ArgumentException("The retained capture cannot also be a duplicate.", nameof(duplicateIds));
        if (resolution == DuplicateResolution.Keep) return;

        Save(duplicateIds.Append(retainedId).ToArray());
        var retained = _captures.Single(capture => capture.Id == retainedId);
        foreach (var duplicate in _captures.Where(capture => duplicateIds.Contains(capture.Id)))
        {
            duplicate.IncludeInDocument = false;
            duplicate.ProcessingStatus = ScreenshotStatus.Excluded;
            if (resolution == DuplicateResolution.Combine)
            {
                retained.Tags = MergeCommaSeparated(retained.Tags, duplicate.Tags);
                retained.UserContext = string.Join(Environment.NewLine, new[] { retained.UserContext, duplicate.UserContext }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }
    }

    public void Delete(IReadOnlyCollection<Guid> ids) =>
        Mutate(ids, capture => capture.Status = EntityStatus.Deleted);

    public void Restore(IReadOnlyCollection<Guid> ids) => Mutate(ids, capture =>
    {
        capture.Status = EntityStatus.Active;
        capture.IncludeInDocument = true;
        if (capture.ProcessingStatus == ScreenshotStatus.Excluded)
            capture.ProcessingStatus = ScreenshotStatus.Ready;
    });

    public void Reprocess(IReadOnlyCollection<Guid> ids) =>
        Mutate(ids, capture => capture.ProcessingStatus = ScreenshotStatus.Queued);

    public bool Undo()
    {
        if (!_undo.TryPop(out var states)) return false;
        foreach (var state in states)
        {
            var capture = _captures.Single(item => item.Id == state.Id);
            capture.SectionId = state.SectionId;
            capture.Status = state.Status;
            capture.ProcessingStatus = state.ProcessingStatus;
            capture.IncludeInDocument = state.IncludeInDocument;
            capture.Tags = state.Tags;
            capture.UserContext = state.UserContext;
        }
        _captures.Sort((left, right) => Index(states, left.Id).CompareTo(Index(states, right.Id)));
        return true;
    }

    private void Mutate(IReadOnlyCollection<Guid> ids, Action<Screenshot> mutation)
    {
        Save(ids);
        foreach (var capture in _captures.Where(capture => ids.Contains(capture.Id))) mutation(capture);
    }

    private void Save(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return;
        _undo.Push(_captures.Select((capture, index) => new CaptureState(capture.Id, index, capture.SectionId,
            capture.Status, capture.ProcessingStatus, capture.IncludeInDocument, capture.Tags, capture.UserContext)).ToArray());
    }

    private static int Index(IReadOnlyList<CaptureState> states, Guid id) => states.Single(state => state.Id == id).Index;
    private static IEnumerable<string> Tags(Screenshot capture) => capture.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string MergeCommaSeparated(string left, string right) => string.Join(", ",
        left.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Concat(right.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    private sealed record CaptureState(Guid Id, int Index, Guid? SectionId, EntityStatus Status, ScreenshotStatus ProcessingStatus,
        bool IncludeInDocument, string Tags, string UserContext);
}
