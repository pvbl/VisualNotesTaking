using System.Globalization;
using System.Text;

namespace VisualNotes.Core.Services;

public enum SearchField { Title, Context, Extraction, Note, Tag }

public sealed record SearchBlock(
    Guid Id,
    Guid SessionId,
    string Title = "",
    string Context = "",
    string Extraction = "",
    string Note = "",
    IReadOnlyCollection<string>? Tags = null);

public sealed record SearchFilter(
    Guid? SessionId = null,
    IReadOnlyCollection<SearchField>? Fields = null,
    IReadOnlyCollection<string>? Tags = null);

public sealed record SearchMatch(SearchField Field, int Start, int Length);

public sealed record SearchResult(
    Guid BlockId,
    Guid SessionId,
    string Title,
    string Fragment,
    SearchMatch Navigation,
    double Score);

/// <summary>Unicode-aware tokenizer shared by indexing and queries.</summary>
public static class SearchTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var normalized = RemoveDiacritics(text.Normalize(NormalizationForm.FormD));
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
                UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber ||
                rune.Value == '_')
            {
                current.Append(rune.ToString().ToLowerInvariant());
            }
            else
            {
                AddToken(tokens, current);
            }
        }
        AddToken(tokens, current);
        return tokens;
    }

    private static string RemoveDiacritics(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                result.Append(rune);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0) return;
        tokens.Add(current.ToString());
        current.Clear();
    }
}

/// <summary>
/// In-memory inverted index. Upsert is atomic from the caller's perspective and
/// replaces all old postings, so edits and deletions never leave stale results.
/// </summary>
public sealed class LocalSearchIndex
{
    private const int FragmentRadius = 70;
    private readonly Dictionary<Guid, IndexedBlock> _blocks = [];
    private readonly Dictionary<string, HashSet<Guid>> _postings = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Count { get { lock (_gate) return _blocks.Count; } }

    public void Upsert(SearchBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        lock (_gate)
        {
            RemoveCore(block.Id);
            var indexed = IndexedBlock.Create(block);
            _blocks.Add(block.Id, indexed);
            foreach (var token in indexed.Tokens)
            {
                if (!_postings.TryGetValue(token, out var ids)) _postings[token] = ids = [];
                ids.Add(block.Id);
            }
        }
    }

    public bool Remove(Guid blockId)
    {
        lock (_gate) return RemoveCore(blockId);
    }

    public IReadOnlyList<SearchResult> Search(string query, SearchFilter? filter = null, int limit = 50)
    {
        if (limit <= 0) return [];
        var queryTokens = SearchTokenizer.Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTokens.Length == 0) return [];

        lock (_gate)
        {
            HashSet<Guid>? candidates = null;
            foreach (var token in queryTokens)
            {
                if (!_postings.TryGetValue(token, out var posting)) return [];
                candidates = candidates is null ? [.. posting] : Intersect(candidates, posting);
                if (candidates.Count == 0) return [];
            }

            return candidates.Select(id => Match(_blocks[id], queryTokens, filter))
                .Where(result => result is not null)
                .Select(result => result!)
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .ToArray();
        }
    }

    private bool RemoveCore(Guid blockId)
    {
        if (!_blocks.Remove(blockId, out var old)) return false;
        foreach (var token in old.Tokens)
        {
            var ids = _postings[token];
            ids.Remove(blockId);
            if (ids.Count == 0) _postings.Remove(token);
        }
        return true;
    }

    private static HashSet<Guid> Intersect(HashSet<Guid> left, HashSet<Guid> right)
    {
        left.IntersectWith(right);
        return left;
    }

    private static SearchResult? Match(IndexedBlock indexed, string[] queryTokens, SearchFilter? filter)
    {
        if (filter?.SessionId is not null && indexed.Block.SessionId != filter.SessionId) return null;
        if (filter?.Tags is { Count: > 0 } requiredTags && !requiredTags.All(tag =>
            indexed.NormalizedTags.Contains(NormalizeTag(tag)))) return null;

        var allowed = filter?.Fields is { Count: > 0 } fields ? fields : null;
        var eligible = FieldOrder.Where(field => allowed is null || allowed.Contains(field.Field))
            .Select(field => (field.Field, Value: field.Value(indexed.Block),
                Tokens: SearchTokenizer.Tokenize(field.Value(indexed.Block))))
            .ToArray();
        if (queryTokens.Any(token => !eligible.Any(field => field.Tokens.Contains(token)))) return null;

        foreach (var field in eligible.OrderByDescending(field => queryTokens.Count(field.Tokens.Contains)))
        {
            var value = field.Value;
            var position = FindPosition(value, queryTokens[0]);
            var start = Math.Max(0, position - FragmentRadius);
            var end = Math.Min(value.Length, position + queryTokens[0].Length + FragmentRadius);
            var fragment = (start > 0 ? "…" : "") + value[start..end] + (end < value.Length ? "…" : "");
            var score = queryTokens.Length * 10 + (field.Field == SearchField.Title ? 5 : 0);
            return new(indexed.Block.Id, indexed.Block.SessionId, indexed.Block.Title, fragment,
                new(field.Field, position, Math.Min(queryTokens[0].Length, value.Length - position)), score);
        }
        return null;
    }

    private static int FindPosition(string value, string normalizedToken)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var remaining = value[index..];
            if (SearchTokenizer.Tokenize(remaining).FirstOrDefault() == normalizedToken) return index;
        }
        return 0;
    }

    private static string NormalizeTag(string tag) => string.Join(" ", SearchTokenizer.Tokenize(tag));

    private static readonly (SearchField Field, Func<SearchBlock, string> Value)[] FieldOrder =
    [
        (SearchField.Title, block => block.Title),
        (SearchField.Context, block => block.Context),
        (SearchField.Extraction, block => block.Extraction),
        (SearchField.Note, block => block.Note),
        (SearchField.Tag, block => string.Join(" ", block.Tags ?? []))
    ];

    private sealed record IndexedBlock(SearchBlock Block, HashSet<string> Tokens, HashSet<string> NormalizedTags)
    {
        public static IndexedBlock Create(SearchBlock block)
        {
            var tokens = FieldOrder.SelectMany(field => SearchTokenizer.Tokenize(field.Value(block))).ToHashSet(StringComparer.Ordinal);
            var tags = (block.Tags ?? []).Select(NormalizeTag).Where(tag => tag.Length > 0).ToHashSet(StringComparer.Ordinal);
            return new(block, tokens, tags);
        }
    }
}
